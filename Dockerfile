# syntax=docker/dockerfile:1
# Builds the API only. The Blazor WebAssembly client (src/TaskManager.Web) is a static site that can be hosted anywhere.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/TaskManager.Api/TaskManager.Api.csproj src/TaskManager.Api/
RUN dotnet restore src/TaskManager.Api

COPY src/TaskManager.Api src/TaskManager.Api
RUN dotnet publish src/TaskManager.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

RUN mkdir -p /data && chown -R app /data
VOLUME /data

# Jwt__Key is deliberately NOT set here: the API refuses to start without a secret of at least 32 characters.
# Set Cors__AllowedOrigins__0 to the address of your web client.
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/taskmanager.db" \
    HttpsRedirection=false
EXPOSE 8080

USER app
ENTRYPOINT ["dotnet", "TaskManager.Api.dll"]
