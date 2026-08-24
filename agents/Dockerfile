# Janani Agent Service — ADK agents behind a FastAPI wrapper (main.py).
# Deployed as its own Cloud Run service (janani-agents), called by the
# .NET app (janani-app) over authenticated service-to-service requests —
# see Services/GoogleIdTokenHandler.cs and deploy-cloudrun.ps1.
FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

ENV PORT=8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD python -c "import urllib.request,os; urllib.request.urlopen(f'http://localhost:{os.environ.get(\"PORT\",8080)}/health')" || exit 1

CMD exec uvicorn main:app --host 0.0.0.0 --port ${PORT}
