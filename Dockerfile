# 1. Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# 2. Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Install curl for HEALTHCHECK
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
# Program.cs binds UseUrls to the PORT env var (default 10000); align the default
# so local runs and HEALTHCHECK match EXPOSE. Render overrides PORT to 10000.
ENV PORT=8080
EXPOSE 8080

# Run as the built-in non-root 'app' user (UID 1654) for defense-in-depth
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

# 🚀 CHANGE THIS LINE to match your log:
ENTRYPOINT ["dotnet", "Portfolio.Backend.dll"]