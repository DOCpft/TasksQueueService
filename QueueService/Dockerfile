FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY QueueService/QueueService.csproj QueueService/
RUN dotnet restore QueueService/QueueService.csproj

COPY QueueService/ QueueService/

WORKDIR /src/QueueService
RUN dotnet publish QueueService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "QueueService.dll"]