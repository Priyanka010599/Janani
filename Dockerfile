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

# DEMO SETTING — deliberate, not an oversight.
# Development keeps the elder vitals ingest endpoint open so the edge-device
# simulator (edge-device-sim/) can POST readings without a provisioned
# DEVICE_INGEST_TOKEN. Accepted risk for a time-boxed demo on an unlisted URL.
#
# The blast radius is contained on purpose: everything in Program.cs whose
# safety depends on being deployed keys off K_SERVICE (which Cloud Run always
# sets) rather than off this variable, so the production exception handler,
# HSTS, Secure cookies and PHI log redaction all stay ON despite this line.
# The one thing it opens is elder vitals ingest.
#
# TO CLOSE AFTER THE DEMO: delete this line, then provision the secret —
#   gcloud secrets create janani-device-ingest-token --data-file=-
#   (add DEVICE_INGEST_TOKEN=janani-device-ingest-token:latest to --set-secrets)
# and run the simulator with --token <value>. It already supports the flag.
ENV ASPNETCORE_ENVIRONMENT=Development

EXPOSE 8080

# Copy published output
COPY --from=build /app/publish .

# Health check probe
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Janani.dll"]
