# Multistage Dockerfile for Janani Multi-Agent System (.NET 8)

# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["Janani.csproj", "./"]
RUN dotnet restore "Janani.csproj"

# Copy remaining source code and publish
COPY . .
RUN dotnet publish "Janani.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Configure ASP.NET Core environment defaults
ENV ASPNETCORE_URLS=http://+:8080
ENV AI_PROVIDER=ollama
EXPOSE 8080

# Copy published output
COPY --from=build /app/publish .

# Health check probe
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Janani.dll"]
