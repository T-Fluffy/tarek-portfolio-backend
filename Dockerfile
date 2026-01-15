# 1. Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore
COPY ["TarekPortfolio.Backend.csproj", "./"]
RUN dotnet restore

# Copy all files and build
COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# 2. Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# 🚀 IMPORTANT: Tell .NET to listen on the port Render provides
# If Render doesn't provide a PORT, it defaults to 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "TarekPortfolio.Backend.dll"]