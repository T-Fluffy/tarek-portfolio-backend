# 1. Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 🚀 This line is the fix: It copies EVERYTHING in the repo to the build container
COPY . .

# Now we run restore on whatever .csproj file it finds in the current directory
RUN dotnet restore

# Build and Publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# 2. Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TarekPortfolio.Backend.dll"]