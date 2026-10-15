# QueueService

Мини-API для задач: создание, завершение, удаление.  
При завершении публикует событие в RabbitMQ.

---

## Содержание

- [Стек](#стек)
- [Структура](#структура)
- [Запуск](#запуск)
- [API](#api)
- [Примеры запросов](#примеры-запросов)
- [RabbitMQ](#rabbitmq)
- [База данных](#база-данных)
- [Тесты](#тесты)
- [Конфигурация](#конфигурация)
- [Заметки](#заметки)
- [Быстрая проверка сценария](#быстрая-проверка-сценария)

---

## Стек

- **.NET 9**, ASP.NET Core Minimal API
- **PostgreSQL 17** + EF Core (`xmin` как rowversion)
- **RabbitMQ 4** (topic exchange `task.events`, routing key `task.completed`)
- **Docker Compose** для локального запуска
- **Testcontainers** для интеграционного теста

---

## Структура

```
.
├── docker-compose.yml
├── README.md
├── QueueService/
│   ├── Dockerfile
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Models/
│   │   ├── TaskItem.cs
│   │   └── Priority.cs
│   ├── Data/
│   │   └── TaskDbContext.cs
│   ├── Endpoints/
│   │   └── TaskEndpoints.cs
│   └── Messaging/
│       ├── ITaskEventPublisher.cs
│       ├── RabbitMqPublisher.cs
│       ├── RabbitMqOptions.cs
│       └── TaskCompletedEvent.cs
└── QueueService.Tests/
    ├── CompleteTaskIntegrationTest.cs
    ├── TaskApiFactory.cs
    └── TestConsumer.cs
```

---

## Запуск

```bash
docker compose up --build
```

Поднимаются три контейнера:

| Сервис     | Адрес                                       |
|------------|---------------------------------------------|
| `api`      | http://localhost:8080                       |
| `postgres` | localhost:5432 (db `queue_service`)         |
| `rabbitmq` | AMQP 5672, UI http://localhost:15672        |

Логин/пароль RabbitMQ: `guest` / `guest`.  
Логин/пароль Postgres: `postgres` / `postgres`.

Остановить:

```bash
docker compose down          # контейнеры, данные БД сохраняются
docker compose down -v       # + удалить volume с БД
```

Пересобрать только API:

```bash
docker compose up --build api
```

Логи:

```bash
docker compose logs -f api
docker compose logs -f postgres
docker compose logs -f rabbitmq
```

---

## API

| Метод  | Путь                    | Успех | Ошибки                |
|--------|-------------------------|-------|-----------------------|
| POST   | `/tasks`                | 201   | 400 — невалидный вход |
| GET    | `/tasks`                | 200   | —                     |
| PUT    | `/tasks/{id}/complete`  | 204   | 404, 409              |
| DELETE | `/tasks/{id}`           | 204   | 404                   |

### POST `/tasks`

Тело запроса:

```json
{
  "title": "Buy milk",
  "priority": "High"
}
```

- `title` — обязательный, 1–200 символов.
- `priority` — `Low` | `Medium` | `High`, по умолчанию `Medium`.

Ответ `201 Created`:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Buy milk",
  "isCompleted": false,
  "createdAt": "2026-05-07T12:00:00+00:00",
  "completedAt": null,
  "priority": "High"
}
```

### PUT `/tasks/{id}/complete`

- `204` — задача завершена, событие опубликовано.
- `404` — задачи нет.
- `409` — задача уже завершена или параллельный запрос успел первым.

### DELETE `/tasks/{id}`

- `204` — удалено.
- `404` — задачи нет.

---

## Примеры запросов

### PowerShell

```powershell
# Создать
$created = Invoke-RestMethod -Method Post -Uri http://localhost:8080/tasks `
  -ContentType "application/json" `
  -Body '{"title":"Buy milk","priority":"High"}'
$id = $created.id
"Created: $id"

# Все задачи
Invoke-RestMethod http://localhost:8080/tasks | ConvertTo-Json -Depth 5

# Завершить -> 204
curl.exe -i -X PUT "http://localhost:8080/tasks/$id/complete"

# Повторно -> 409
curl.exe -i -X PUT "http://localhost:8080/tasks/$id/complete"

# Удалить -> 204
curl.exe -i -X DELETE "http://localhost:8080/tasks/$id"
```

> В Windows PowerShell 5.1 у `Invoke-WebRequest` **нет** `-SkipHttpErrorCheck`  
> (это параметр PowerShell 7). Ловите 409 через `try/catch` либо используйте `curl.exe -i`.

### bash

```bash
# Создать
curl -X POST http://localhost:8080/tasks \
  -H "Content-Type: application/json" \
  -d '{"title":"Buy milk","priority":"High"}'

# Все задачи
curl http://localhost:8080/tasks

# Завершить
curl -i -X PUT http://localhost:8080/tasks/<id>/complete

# Удалить
curl -i -X DELETE http://localhost:8080/tasks/<id>
```

---

## RabbitMQ

При `PUT /tasks/{id}/complete` сервис публикует в exchange `task.events`  
с routing key `task.completed` сообщение:

```json
{
  "taskId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Buy milk",
  "completedAt": "2026-05-07T12:00:00+00:00",
  "priority": "High"
}
```

Consumer’а в проекте нет — сообщение уходит в exchange. Чтобы **увидеть** его:

1. Открыть http://localhost:15672 (guest / guest).
2. **Queues** → **Add a new queue** → name `test.completed`, durable — выключить.
3. В очереди `test.completed` → **Bindings** → **Add binding**:
   - From exchange: `task.events`
   - Routing key: `task.completed`
4. Завершить любую задачу через API.
5. **Queues** → `test.completed` → **Get messages** — увидите JSON.

Если RabbitMQ недоступен:

- Задача всё равно завершается (в БД).
- Ошибка публикации пишется в лог `api`.
- Сообщение теряется — это осознанный трейд-офф (по ТЗ).

### Настройки публикации

- Обменник: `task.events`, тип `topic`, `durable: true`.
- Routing key: `task.completed`.
- Сериализация: `System.Text.Json`, camelCase, enum строками.
- `DeliveryMode = Persistent`.
- Публикация без confirms, без ожидания ответа брокера.

### Graceful shutdown

При остановке контейнера (`docker compose stop api`, Ctrl+C, SIGTERM):

1. `HostOptions.ShutdownTimeout = 5s`.
2. `RabbitMqPublisher.StopAsync` закрывает writer канала.
3. Ждёт до 4 секунд, пока `BackgroundService` дочитает очередь и опубликует оставшееся.
4. Закрывает `IChannel` и `IConnection`.

---

## База данных

Зайти в psql:

```bash
docker compose exec postgres psql -U postgres -d queue_service
```

Полезное:

```sql
\dt
SELECT id, title, "IsCompleted", "CompletedAt", xmin FROM "Tasks";
\q
```

- `xmin` — системная колонка PostgreSQL, используется как concurrency token.
- Схема создаётся через `EnsureCreatedAsync` при старте (миграции по ТЗ не обязательны).

---

## Тесты

Интеграционный тест `CompleteTaskIntegrationTest` проверяет полный сценарий:

1. `POST /tasks` → 201, `isCompleted = false`, `completedAt = null`.
2. `PUT /tasks/{id}/complete` → 204.
3. В RabbitMQ прилетает сообщение с корректными `taskId`, `title`, `priority`, `completedAt`.

Тест поднимает **свои** Postgres и RabbitMQ через **Testcontainers**,  
поэтому рабочий `docker compose` для теста не нужен. Достаточно запущенного Docker Desktop.

Запуск:

```bash
cd QueueService.Tests
dotnet test
```

Требования:

- Docker Desktop запущен.
- Свободные порты для Testcontainers (динамические).
- В первый раз образы `postgres:17-alpine` и `rabbitmq:4-management-alpine` скачаются автоматически.

---

## Конфигурация

Через переменные окружения (двойное подчёркивание заменяет `:`):

```
ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=queue_service;Username=postgres;Password=postgres
RabbitMq__HostName=rabbitmq
RabbitMq__Port=5672
RabbitMq__UserName=guest
RabbitMq__Password=guest
ASPNETCORE_URLS=http://+:8080
ASPNETCORE_ENVIRONMENT=Development
```

В `docker-compose.yml` эти значения прописаны в сервисе `api`.

---

## Заметки

- Публикация события — **после** `SaveChangesAsync`. Если БД упадёт, сообщение не уйдёт.
- `xmin` даёт `409` при параллельном завершении одной задачи без явных локов.
- Сервисный слой не выделен: бизнес-логика тонкая, вход один (HTTP).  
  Если логика разрослась бы, её стоило бы вынести в `TaskService`.
- Swagger не подключён — по ТЗ не требуется.
- Пагинация/фильтры/сортировка в `GET /tasks` не реализованы — по ТЗ не обязательны.

---

## Быстрая проверка сценария

```powershell
# 1. Создать
$created = Invoke-RestMethod -Method Post -Uri http://localhost:8080/tasks `
  -ContentType "application/json" `
  -Body '{"title":"Buy milk","priority":"High"}'
$id = $created.id

# 2. Завершить (ожидаем 204)
curl.exe -i -X PUT "http://localhost:8080/tasks/$id/complete"

# 3. Повторить (ожидаем 409)
curl.exe -i -X PUT "http://localhost:8080/tasks/$id/complete"

# 4. Проверить БД
docker compose exec postgres psql -U postgres -d queue_service `
  -c 'SELECT id, title, "IsCompleted", "CompletedAt" FROM "Tasks";'
```

Если шаг 2 → `204`, шаг 3 → `409`, а в RabbitMQ UI во временной очереди  
`test.completed` лежит ожидаемый JSON — сервис работает корректно.
