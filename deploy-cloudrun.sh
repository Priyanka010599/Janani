#!/usr/bin/env bash
# Janani Google Cloud Native Multi-Agent System - Production Deployment
# Covers: Vertex AI / GCS / Pub/Sub / Cloud SQL / Secret Manager / Cloud Run (gen2, scaled)
#
# Cost note: Cloud SQL is NOT covered by GCP's Always Free tier - the db-f1-micro
# instance created below costs roughly $8-15/month (covered by trial credit if you
# have one). Cloud Run, GCS, and Pub/Sub usage at this scale should stay within
# the free tier. MIN_INSTANCES defaults to 0 to avoid an always-on Cloud Run cost.

set -euo pipefail

PROJECT_ID="${GCP_PROJECT_ID:-${1:-}}"
REGION="${GCP_REGION:-us-central1}"
SERVICE_NAME="janani-app"
BUCKET_NAME="${GCP_STORAGE_BUCKET:-janani-assets}"
SQL_INSTANCE="${JANANI_SQL_INSTANCE:-janani-db}"
SQL_TIER="${JANANI_SQL_TIER:-db-f1-micro}"
SQL_DB_NAME="${JANANI_SQL_DB:-janani}"
SQL_USER="${JANANI_SQL_USER:-janani_app}"
MIN_INSTANCES="${JANANI_MIN_INSTANCES:-0}"
# Deployed separately via `gcloud run deploy janani-agents --source agents/
# ...` (see agents/Dockerfile) - this script does not build or deploy it,
# only points janani-app at its URL.
AGENT_SERVICE_URL="${JANANI_AGENT_SERVICE_URL:-https://janani-agents-52541450553.us-central1.run.app}"

if [[ -z "$PROJECT_ID" ]]; then
  echo "Error: GCP_PROJECT_ID not set. Usage: ./deploy-cloudrun.sh <PROJECT_ID>"
  exit 1
fi

CLOUD_RUN_URL="https://${SERVICE_NAME}-${PROJECT_ID}.${REGION}.run.app"

echo "=== Janani Google Cloud Native Multi-Agent Deployment ==="
echo "  Project:  $PROJECT_ID"
echo "  Region:   $REGION"
echo "  Service:  $SERVICE_NAME"
echo "  Bucket:   $BUCKET_NAME"

# -- 1. Enable GCP APIs ------------------------------------------------------
echo -e "\n[1/10] Enabling APIs..."
gcloud services enable \
  run.googleapis.com \
  artifactregistry.googleapis.com \
  aiplatform.googleapis.com \
  storage.googleapis.com \
  pubsub.googleapis.com \
  secretmanager.googleapis.com \
  sqladmin.googleapis.com \
  cloudbuild.googleapis.com \
  --project "$PROJECT_ID"

# Cloud Build runs as the project's default Compute Engine service account.
# Newer GCP projects no longer auto-grant it Editor, so without these explicit
# grants "gcloud builds submit" fails partway through - once reading its own
# uploaded source tarball, once pushing the built image to Artifact Registry.
PROJECT_NUMBER=$(gcloud projects describe "$PROJECT_ID" --format="value(projectNumber)")
CLOUD_BUILD_SA="${PROJECT_NUMBER}-compute@developer.gserviceaccount.com"
for ROLE in roles/storage.objectViewer roles/artifactregistry.writer roles/logging.logWriter; do
  gcloud projects add-iam-policy-binding "$PROJECT_ID" \
    --member="serviceAccount:${CLOUD_BUILD_SA}" \
    --role="$ROLE" \
    --quiet &>/dev/null || true
done

# Dedicated Cloud Run service account - referenced by the Pub/Sub push
# subscriptions below and granted IAM roles later, so it must exist before
# either of those, not just before the Cloud Run deploy step itself.
SERVICE_ACCOUNT="${SERVICE_NAME}@${PROJECT_ID}.iam.gserviceaccount.com"
if ! gcloud iam service-accounts describe "$SERVICE_ACCOUNT" --project="$PROJECT_ID" &>/dev/null; then
  gcloud iam service-accounts create "$SERVICE_NAME" --display-name="Janani Cloud Run service account" --project="$PROJECT_ID"
  echo "  Waiting for IAM propagation..."
  sleep 10
fi

# -- 2. Secret Manager - GEMINI_API_KEY + Pub/Sub token ----------------------
echo -e "\n[2/10] Ensuring secrets exist in Secret Manager..."
if ! gcloud secrets describe janani-gemini-key --project="$PROJECT_ID" &>/dev/null; then
  echo "  Secret not found - creating placeholder."
  echo "REPLACE_WITH_REAL_KEY" | gcloud secrets create janani-gemini-key --data-file=- --project="$PROJECT_ID"
fi
GEMINI_KEY=$(gcloud secrets versions access latest --secret=janani-gemini-key --project="$PROJECT_ID")
if [[ "$GEMINI_KEY" == "REPLACE_WITH_REAL_KEY" ]]; then
  echo -e "\nGEMINI_API_KEY is still the placeholder value."
  echo "Get a key from https://aistudio.google.com/apikey, then run:"
  echo "  echo \"YOUR_KEY\" | gcloud secrets versions add janani-gemini-key --data-file=- --project=$PROJECT_ID"
  echo "...then re-run this script."
  exit 1
fi

if ! gcloud secrets describe janani-pubsub-token --project="$PROJECT_ID" &>/dev/null; then
  NEW_TOKEN=$(python3 -c "import uuid; print(uuid.uuid4())")
  echo "$NEW_TOKEN" | gcloud secrets create janani-pubsub-token --data-file=- --project="$PROJECT_ID"
fi
PUBSUB_TOKEN=$(gcloud secrets versions access latest --secret=janani-pubsub-token --project="$PROJECT_ID")

# Google Calendar sync client credentials (optional feature - warn, don't
# block deploy, since the app degrades gracefully without it configured).
if ! gcloud secrets describe janani-google-calendar-client-id --project="$PROJECT_ID" &>/dev/null; then
  echo "REPLACE_WITH_REAL_CLIENT_ID" | gcloud secrets create janani-google-calendar-client-id --data-file=- --project="$PROJECT_ID"
fi
if ! gcloud secrets describe janani-google-calendar-client-secret --project="$PROJECT_ID" &>/dev/null; then
  echo "REPLACE_WITH_REAL_CLIENT_SECRET" | gcloud secrets create janani-google-calendar-client-secret --data-file=- --project="$PROJECT_ID"
fi
CALENDAR_CLIENT_ID=$(gcloud secrets versions access latest --secret=janani-google-calendar-client-id --project="$PROJECT_ID")
if [[ "$CALENDAR_CLIENT_ID" == "REPLACE_WITH_REAL_CLIENT_ID" ]]; then
  echo "  Note: Google Calendar sync is not configured yet (janani-google-calendar-client-id is a placeholder)."
  echo "  Create an OAuth Client ID at https://console.cloud.google.com/apis/credentials, then run:"
  echo "    echo \"YOUR_CLIENT_ID\" | gcloud secrets versions add janani-google-calendar-client-id --data-file=- --project=$PROJECT_ID"
  echo "    echo \"YOUR_CLIENT_SECRET\" | gcloud secrets versions add janani-google-calendar-client-secret --data-file=- --project=$PROJECT_ID"
  echo "  The app deploys and runs fine without it; the Settings page just won't offer calendar sync yet."
fi

# -- 3. GCS Bucket ------------------------------------------------------------
echo -e "\n[3/10] Ensuring GCS bucket exists..."
gsutil ls "gs://$BUCKET_NAME" &>/dev/null || gsutil mb -p "$PROJECT_ID" -l "$REGION" "gs://$BUCKET_NAME"

# -- 4. Cloud SQL (PostgreSQL) - instance, database, user, connection secret -
echo -e "\n[4/10] Ensuring Cloud SQL Postgres instance exists (this can take several minutes on first run)..."

if ! gcloud secrets describe janani-db-password --project="$PROJECT_ID" &>/dev/null; then
  NEW_DB_PASSWORD=$(python3 -c "import uuid; print(uuid.uuid4())")
  echo "$NEW_DB_PASSWORD" | gcloud secrets create janani-db-password --data-file=- --project="$PROJECT_ID"
fi
DB_PASSWORD=$(gcloud secrets versions access latest --secret=janani-db-password --project="$PROJECT_ID")

if ! gcloud sql instances describe "$SQL_INSTANCE" --project="$PROJECT_ID" &>/dev/null; then
  gcloud sql instances create "$SQL_INSTANCE" \
    --database-version=POSTGRES_15 \
    --tier="$SQL_TIER" \
    --region="$REGION" \
    --project="$PROJECT_ID"
fi

if ! gcloud sql databases describe "$SQL_DB_NAME" --instance="$SQL_INSTANCE" --project="$PROJECT_ID" &>/dev/null; then
  gcloud sql databases create "$SQL_DB_NAME" --instance="$SQL_INSTANCE" --project="$PROJECT_ID"
fi

if gcloud sql users list --instance="$SQL_INSTANCE" --project="$PROJECT_ID" --format="value(name)" | grep -qx "$SQL_USER"; then
  # Keep the live DB user's password in sync with the secret so the connection
  # string below always matches, even on re-runs against an existing instance.
  gcloud sql users set-password "$SQL_USER" --instance="$SQL_INSTANCE" --password="$DB_PASSWORD" --project="$PROJECT_ID"
else
  gcloud sql users create "$SQL_USER" --instance="$SQL_INSTANCE" --password="$DB_PASSWORD" --project="$PROJECT_ID"
fi
# gcloud can return before the password change is actually live on the
# instance - give it a moment before anything tries to authenticate with it.
sleep 10

SQL_CONNECTION_NAME="${PROJECT_ID}:${REGION}:${SQL_INSTANCE}"
DATABASE_URL="Host=/cloudsql/${SQL_CONNECTION_NAME};Database=${SQL_DB_NAME};Username=${SQL_USER};Password=${DB_PASSWORD};SSL Mode=Disable"

# Always push a fresh version reflecting the current password - unlike the
# other secrets here, this one is fully derived (not user-provided), so it
# must never be allowed to drift out of sync with the DB user's real password
# set above. Only creating it once on first run was the bug that caused
# password auth failures after any password/connection-string change.
if ! gcloud secrets describe janani-database-url --project="$PROJECT_ID" &>/dev/null; then
  echo "$DATABASE_URL" | gcloud secrets create janani-database-url --data-file=- --project="$PROJECT_ID"
else
  echo "$DATABASE_URL" | gcloud secrets versions add janani-database-url --data-file=- --project="$PROJECT_ID"
fi

# -- 5. Pub/Sub Topics & Push Subscriptions -----------------------------------
echo -e "\n[5/10] Ensuring Pub/Sub topics & push subscriptions exist..."
TOPICS=("janani-mood-events" "janani-meal-events" "janani-birthplan-events" "janani-journal-events")

for TOPIC in "${TOPICS[@]}"; do
  gcloud pubsub topics describe "$TOPIC" --project="$PROJECT_ID" &>/dev/null \
    || gcloud pubsub topics create "$TOPIC" --project="$PROJECT_ID"

  SUB="${TOPIC}-push"
  gcloud pubsub subscriptions describe "$SUB" --project="$PROJECT_ID" &>/dev/null || \
    gcloud pubsub subscriptions create "$SUB" \
      --topic="$TOPIC" \
      --push-endpoint="${CLOUD_RUN_URL}/api/events/pubsub?token=${PUBSUB_TOKEN}" \
      --push-auth-service-account="$SERVICE_ACCOUNT" \
      --project="$PROJECT_ID"
done

# -- 6. Artifact Registry ------------------------------------------------------
echo -e "\n[6/10] Ensuring Artifact Registry repository exists..."
gcloud artifacts repositories describe janani-repo --location="$REGION" --project="$PROJECT_ID" &>/dev/null || \
  gcloud artifacts repositories create janani-repo \
    --repository-format=docker \
    --location="$REGION" \
    --description="Janani multi-agent container images" \
    --project="$PROJECT_ID"

IMAGE_TAG="${REGION}-docker.pkg.dev/${PROJECT_ID}/janani-repo/${SERVICE_NAME}:latest"

# -- 7. Cloud Build ------------------------------------------------------------
echo -e "\n[7/10] Building container image via Cloud Build..."
gcloud builds submit --tag "$IMAGE_TAG" --project "$PROJECT_ID" .

# -- 8. IAM Roles for Cloud Run Service Account -------------------------------
# Must happen before the deploy below - Cloud Run validates that the revision's
# service account can already read the mounted secrets at deploy time, it
# doesn't just check at runtime.
echo -e "\n[8/10] Granting required IAM roles to Cloud Run service account..."
for ROLE in roles/aiplatform.user roles/storage.objectAdmin roles/pubsub.editor roles/secretmanager.secretAccessor roles/cloudsql.client; do
  ROLE_OK=0
  for attempt in 1 2 3 4 5; do
    if gcloud projects add-iam-policy-binding "$PROJECT_ID" \
      --member="serviceAccount:${SERVICE_ACCOUNT}" \
      --role="$ROLE" \
      --quiet &>/dev/null; then
      ROLE_OK=1
      break
    fi
    if [[ $attempt -lt 5 ]]; then
      echo "  $ROLE grant failed (attempt $attempt/5) - service account still propagating, retrying in 15s..."
      sleep 15
    fi
  done
  if [[ $ROLE_OK -eq 0 ]]; then
    echo "  WARNING: could not grant $ROLE after 5 attempts. Run this manually once propagation catches up:"
    echo "    gcloud projects add-iam-policy-binding $PROJECT_ID --member=\"serviceAccount:${SERVICE_ACCOUNT}\" --role=$ROLE"
  fi
done
# IAM role grants can take a few seconds to become visible to Cloud Run's own
# deploy-time validation, even after add-iam-policy-binding itself succeeded -
# and a freshly-pushed image isn't always immediately visible either.
echo "  Waiting for role grants and image to propagate..."
sleep 20

# -- 9. Cloud Run Deploy -------------------------------------------------------
# (set -e above already stops the script here if the build failed - no
# explicit check needed, unlike the PowerShell version.)
echo -e "\n[9/10] Deploying to Cloud Run (gen2, scaled)..."
# gemini-1.5-flash and gemini-2.5-flash are both retired/unavailable to this
# key's tier as of 2026-08-18 (confirmed 404s); gemini-3.6-flash is current.
# AGENT_SERVICE_REQUIRES_AUTH=true switches on GoogleIdTokenHandler so calls
# to janani-agents (deployed --no-allow-unauthenticated) carry an ID token -
# janani-app's own service account already has roles/run.invoker on it.
ENV_VARS="AI_PROVIDER=vertex,GCP_PROJECT_ID=${PROJECT_ID},GCP_STORAGE_BUCKET=${BUCKET_NAME},VERTEX_CHAT_DEPLOYMENT=gemini-2.5-flash,AGENT_SERVICE_URL=${AGENT_SERVICE_URL},AGENT_SERVICE_REQUIRES_AUTH=true"

DEPLOY_OK=0
for attempt in 1 2 3; do
  if gcloud run deploy "$SERVICE_NAME" \
    --image "$IMAGE_TAG" \
    --platform managed \
    --region "$REGION" \
    --allow-unauthenticated \
    --execution-environment gen2 \
    --session-affinity \
    --service-account "$SERVICE_ACCOUNT" \
    --add-cloudsql-instances "$SQL_CONNECTION_NAME" \
    --cpu 2 \
    --memory 1Gi \
    --min-instances "$MIN_INSTANCES" \
    --max-instances 10 \
    --concurrency 80 \
    --timeout 300 \
    --set-env-vars "$ENV_VARS" \
    --set-secrets "GEMINI_API_KEY=janani-gemini-key:latest,PUBSUB_VERIFICATION_TOKEN=janani-pubsub-token:latest,DATABASE_URL=janani-database-url:latest,GOOGLE_CALENDAR_CLIENT_ID=janani-google-calendar-client-id:latest,GOOGLE_CALENDAR_CLIENT_SECRET=janani-google-calendar-client-secret:latest" \
    --project "$PROJECT_ID"; then
    DEPLOY_OK=1
    break
  fi
  if [[ $attempt -lt 3 ]]; then
    echo "  Deploy failed (attempt $attempt/3) - retrying in 20s..."
    sleep 20
  fi
done
if [[ $DEPLOY_OK -eq 0 ]]; then
  echo -e "\nCloud Run deploy failed after 3 attempts."
  exit 1
fi

# -- 10. Done -------------------------------------------------------------------
echo -e "\n[10/10] === Janani Deployment Complete! ==="
echo "  Service URL: ${CLOUD_RUN_URL}"
echo "  Health:      ${CLOUD_RUN_URL}/health"
echo "  Pub/Sub:     ${CLOUD_RUN_URL}/api/events/pubsub"
echo "  Cloud SQL:   ${SQL_CONNECTION_NAME} (${SQL_TIER})"
echo "  min-instances=${MIN_INSTANCES} - set JANANI_MIN_INSTANCES=1 to avoid cold starts (always-on cost)."
