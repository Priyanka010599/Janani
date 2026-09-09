# PowerShell deployment script for Janani Google Cloud Native Multi-Agent System
# Covers: Vertex AI, GCS, Pub/Sub, Cloud SQL, Secret Manager, Cloud Run scaling
#
# Cost note: Cloud SQL is NOT covered by GCP's Always Free tier - the db-f1-micro
# instance created below costs roughly $8-15/month (covered by trial credit if you
# have one). Cloud Run, GCS, and Pub/Sub usage at this scale should stay within
# the free tier. min-instances defaults to 0 to avoid an always-on Cloud Run cost.

param (
    [string]$ProjectId   = $env:GCP_PROJECT_ID,
    [string]$Region      = "us-central1",
    [string]$ServiceName = "janani-app",
    [string]$BucketName  = "janani-assets",
    [string]$SqlInstance = "janani-db",
    [string]$SqlTier     = "db-f1-micro",
    [string]$SqlDbName   = "janani",
    [string]$SqlUser     = "janani_app",
    [int]   $MinInstances = 0,
    # Deployed separately via `gcloud run deploy janani-agents --source
    # agents/ ...` (see agents/Dockerfile) — this script does not build or
    # deploy it, only points janani-app at its URL. Update if the agent
    # service is ever redeployed under a different URL.
    [string]$AgentServiceUrl = "https://janani-agents-52541450553.us-central1.run.app"
)

if (-not $ProjectId) {
    Write-Error "GCP_PROJECT_ID is not set. Pass -ProjectId or set the environment variable."
    exit 1
}

Write-Host "=== Deploying Janani Google Cloud Native Multi-Agent App ===" -ForegroundColor Green
Write-Host "Project: $ProjectId | Region: $Region | Service: $ServiceName"

# -- 1. Enable GCP APIs -------------------------------------------------------
Write-Host "`n[1/10] Enabling APIs..." -ForegroundColor Yellow
gcloud services enable `
    run.googleapis.com `
    artifactregistry.googleapis.com `
    aiplatform.googleapis.com `
    storage.googleapis.com `
    pubsub.googleapis.com `
    secretmanager.googleapis.com `
    sqladmin.googleapis.com `
    cloudbuild.googleapis.com `
    --project $ProjectId

# Cloud Build runs as the project's default Compute Engine service account.
# Newer GCP projects no longer auto-grant it Editor, so without these explicit
# grants "gcloud builds submit" fails partway through - once reading its own
# uploaded source tarball, once pushing the built image to Artifact Registry.
$ProjectNumber = gcloud projects describe $ProjectId --format="value(projectNumber)"
$CloudBuildSA = "$ProjectNumber-compute@developer.gserviceaccount.com"
foreach ($Role in @("roles/storage.objectViewer", "roles/artifactregistry.writer", "roles/logging.logWriter")) {
    gcloud projects add-iam-policy-binding $ProjectId `
        --member="serviceAccount:$CloudBuildSA" `
        --role=$Role `
        --quiet 2>$null
}

# Dedicated Cloud Run service account - referenced by the Pub/Sub push
# subscriptions below and granted IAM roles later, so it must exist before
# either of those, not just before the Cloud Run deploy step itself.
$ServiceAccount = "$ServiceName@$ProjectId.iam.gserviceaccount.com"
gcloud iam service-accounts describe $ServiceAccount --project=$ProjectId 2>$null
if ($LASTEXITCODE -ne 0) {
    gcloud iam service-accounts create $ServiceName --display-name="Janani Cloud Run service account" --project=$ProjectId
    Write-Host "  Waiting for IAM propagation..." -ForegroundColor Cyan
    Start-Sleep -Seconds 10
}

# -- 2. Secret Manager - Pub/Sub token + calendar OAuth -----------------------
# No Gemini API key secret here: janani-agents authenticates to Gemini via
# Vertex AI IAM (the runtime service account), not a Developer API key, and
# janani-app itself never read this secret's env var -- GEMINI_API_KEY was
# vestigial on janani-app all along.
Write-Host "`n[2/10] Ensuring secrets exist in Secret Manager..." -ForegroundColor Yellow
gcloud secrets describe janani-pubsub-token --project=$ProjectId 2>$null
if ($LASTEXITCODE -ne 0) {
    $NewToken = [System.Guid]::NewGuid().ToString()
    $NewToken | gcloud secrets create janani-pubsub-token --data-file=- --project=$ProjectId
}
$PubSubToken = (gcloud secrets versions access latest --secret=janani-pubsub-token --project=$ProjectId | Out-String).Trim()

# Google Calendar sync client credentials (optional feature - warn, don't
# block deploy, since the app degrades gracefully without it configured).
gcloud secrets describe janani-google-calendar-client-id --project=$ProjectId 2>$null
if ($LASTEXITCODE -ne 0) {
    "REPLACE_WITH_REAL_CLIENT_ID" | gcloud secrets create janani-google-calendar-client-id --data-file=- --project=$ProjectId
}
gcloud secrets describe janani-google-calendar-client-secret --project=$ProjectId 2>$null
if ($LASTEXITCODE -ne 0) {
    "REPLACE_WITH_REAL_CLIENT_SECRET" | gcloud secrets create janani-google-calendar-client-secret --data-file=- --project=$ProjectId
}
$CalendarClientId = (gcloud secrets versions access latest --secret=janani-google-calendar-client-id --project=$ProjectId | Out-String).Trim()
if ($CalendarClientId -eq "REPLACE_WITH_REAL_CLIENT_ID") {
    Write-Host "  Note: Google Calendar sync is not configured yet (janani-google-calendar-client-id is a placeholder)." -ForegroundColor Cyan
    Write-Host "  Create an OAuth Client ID at https://console.cloud.google.com/apis/credentials, then run:" -ForegroundColor Cyan
    Write-Host "    `"YOUR_CLIENT_ID`" | gcloud secrets versions add janani-google-calendar-client-id --data-file=- --project=$ProjectId" -ForegroundColor Cyan
    Write-Host "    `"YOUR_CLIENT_SECRET`" | gcloud secrets versions add janani-google-calendar-client-secret --data-file=- --project=$ProjectId" -ForegroundColor Cyan
    Write-Host "  The app deploys and runs fine without it; the Settings page just won't offer calendar sync yet." -ForegroundColor Cyan
}

# -- 3. Google Cloud Storage bucket -------------------------------------------
Write-Host "`n[3/10] Ensuring GCS bucket exists..." -ForegroundColor Yellow
gsutil ls "gs://$BucketName" 2>$null
if ($LASTEXITCODE -ne 0) {
    gsutil mb -p $ProjectId -l $Region "gs://$BucketName"
    gsutil cors set cors-config.json "gs://$BucketName" 2>$null   # optional: set CORS if cors-config.json exists
}

# -- 4. Cloud SQL (PostgreSQL) - instance, database, user, connection secret --
Write-Host "`n[4/10] Ensuring Cloud SQL Postgres instance exists (this can take several minutes on first run)..." -ForegroundColor Yellow

gcloud secrets describe janani-db-password --project=$ProjectId 2>$null
if ($LASTEXITCODE -ne 0) {
    $NewDbPassword = [System.Guid]::NewGuid().ToString()
    $NewDbPassword | gcloud secrets create janani-db-password --data-file=- --project=$ProjectId
}
$DbPassword = (gcloud secrets versions access latest --secret=janani-db-password --project=$ProjectId | Out-String).Trim()

gcloud sql instances describe $SqlInstance --project=$ProjectId 2>$null
if ($LASTEXITCODE -ne 0) {
    gcloud sql instances create $SqlInstance `
        --database-version=POSTGRES_15 `
        --tier=$SqlTier `
        --region=$Region `
        --project=$ProjectId
}

gcloud sql databases describe $SqlDbName --instance=$SqlInstance --project=$ProjectId 2>$null
if ($LASTEXITCODE -ne 0) {
    gcloud sql databases create $SqlDbName --instance=$SqlInstance --project=$ProjectId
}

$UserExists = gcloud sql users list --instance=$SqlInstance --project=$ProjectId --format="value(name)" | Select-String -SimpleMatch $SqlUser
if (-not $UserExists) {
    gcloud sql users create $SqlUser --instance=$SqlInstance --password=$DbPassword --project=$ProjectId
} else {
    # Keep the live DB user's password in sync with the secret so the connection
    # string below always matches, even on re-runs against an existing instance.
    gcloud sql users set-password $SqlUser --instance=$SqlInstance --password=$DbPassword --project=$ProjectId
}
# gcloud can return before the password change is actually live on the
# instance - give it a moment before anything tries to authenticate with it.
Start-Sleep -Seconds 10

$SqlConnectionName = "${ProjectId}:${Region}:${SqlInstance}"
$DatabaseUrl = "Host=/cloudsql/${SqlConnectionName};Database=${SqlDbName};Username=${SqlUser};Password=${DbPassword};SSL Mode=Disable"

# Always push a fresh version reflecting the current password - unlike the
# other secrets here, this one is fully derived (not user-provided), so it
# must never be allowed to drift out of sync with the DB user's real password
# set above. Only creating it once on first run was the bug that caused
# password auth failures after any password/connection-string change.
#
# Written via a temp file, not piped with --data-file=-: piping a string to
# a native process's stdin from Windows PowerShell can prepend a UTF-8 BOM
# depending on console/locale settings. Npgsql chokes on that (the BOM
# attaches to the first keyword, producing an unparseable "?host" token and
# crashing the container at startup) - confirmed as the cause of a failed
# deploy on 2026-08-18. WriteAllText with a BOM-less UTF8Encoding avoids it.
$DatabaseUrlFile = New-TemporaryFile
[System.IO.File]::WriteAllText($DatabaseUrlFile, $DatabaseUrl, (New-Object System.Text.UTF8Encoding($false)))
try {
    gcloud secrets describe janani-database-url --project=$ProjectId 2>$null
    if ($LASTEXITCODE -ne 0) {
        gcloud secrets create janani-database-url --data-file=$DatabaseUrlFile --project=$ProjectId
    } else {
        gcloud secrets versions add janani-database-url --data-file=$DatabaseUrlFile --project=$ProjectId
    }
} finally {
    Remove-Item $DatabaseUrlFile -Force
}

# -- 5. Pub/Sub topics & push subscriptions -----------------------------------
Write-Host "`n[5/10] Ensuring Pub/Sub topics & push subscriptions exist..." -ForegroundColor Yellow
$Topics = @("janani-mood-events", "janani-meal-events", "janani-birthplan-events", "janani-journal-events")
$CloudRunBase = "https://$ServiceName-$ProjectId.$Region.run.app"

foreach ($Topic in $Topics) {
    gcloud pubsub topics describe $Topic --project=$ProjectId 2>$null
    if ($LASTEXITCODE -ne 0) {
        gcloud pubsub topics create $Topic --project=$ProjectId
    }
    $SubName = "$Topic-push"
    gcloud pubsub subscriptions describe $SubName --project=$ProjectId 2>$null
    if ($LASTEXITCODE -ne 0) {
        gcloud pubsub subscriptions create $SubName `
            --topic=$Topic `
            --push-endpoint="$CloudRunBase/api/events/pubsub?token=$PubSubToken" `
            --push-auth-service-account="$ServiceName@$ProjectId.iam.gserviceaccount.com" `
            --project=$ProjectId
    }
}

# -- 6. Artifact Registry ------------------------------------------------------
Write-Host "`n[6/10] Ensuring Artifact Registry repository exists..." -ForegroundColor Yellow
gcloud artifacts repositories describe janani-repo --location=$Region --project=$ProjectId 2>$null
if ($LASTEXITCODE -ne 0) {
    gcloud artifacts repositories create janani-repo `
        --repository-format=docker `
        --location=$Region `
        --description="Janani multi-agent container images" `
        --project=$ProjectId
}

$ImageTag = "$Region-docker.pkg.dev/$ProjectId/janani-repo/$ServiceName`:latest"

# -- 7. Cloud Build ------------------------------------------------------------
Write-Host "`n[7/10] Building container image..." -ForegroundColor Yellow
gcloud builds submit --tag $ImageTag --project $ProjectId .
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nCloud Build failed - stopping before Cloud Run deploy (no image to deploy)." -ForegroundColor Red
    exit 1
}

# -- 8. Grant IAM roles to Cloud Run SA -----------------------------------------
# Must happen before the deploy below - Cloud Run validates that the revision's
# service account can already read the mounted secrets at deploy time, it
# doesn't just check at runtime.
Write-Host "`n[8/10] Granting required IAM roles to Cloud Run service account..." -ForegroundColor Yellow
foreach ($Role in @("roles/aiplatform.user", "roles/storage.objectAdmin", "roles/pubsub.editor", "roles/secretmanager.secretAccessor", "roles/cloudsql.client")) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        gcloud projects add-iam-policy-binding $ProjectId `
            --member="serviceAccount:$ServiceAccount" `
            --role=$Role `
            --quiet 2>$null
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -lt 5) {
            Write-Host "  $Role grant failed (attempt $attempt/5) - service account still propagating, retrying in 15s..." -ForegroundColor Cyan
            Start-Sleep -Seconds 15
        } else {
            Write-Host "  WARNING: could not grant $Role after 5 attempts. Run this manually once propagation catches up:" -ForegroundColor Red
            Write-Host "    gcloud projects add-iam-policy-binding $ProjectId --member=`"serviceAccount:$ServiceAccount`" --role=$Role" -ForegroundColor Yellow
        }
    }
}
# IAM role grants can take a few seconds to become visible to Cloud Run's own
# deploy-time validation, even after add-iam-policy-binding itself succeeded -
# and a freshly-pushed image isn't always immediately visible either.
Write-Host "  Waiting for role grants and image to propagate..." -ForegroundColor Cyan
Start-Sleep -Seconds 20

# -- 9. Cloud Run deploy with scaling, secrets, Cloud SQL & session affinity --
Write-Host "`n[9/10] Deploying to Cloud Run..." -ForegroundColor Yellow
# gemini-1.5-flash and gemini-2.5-flash are both retired/unavailable to this
# key's tier as of 2026-08-18 (confirmed 404s); gemini-3.6-flash is current.
# AGENT_SERVICE_REQUIRES_AUTH=true switches on GoogleIdTokenHandler so calls
# to janani-agents (deployed --no-allow-unauthenticated) carry an ID token -
# janani-app's own service account already has roles/run.invoker on it.
$EnvVars = "AI_PROVIDER=vertex,GCP_PROJECT_ID=$ProjectId,GCP_STORAGE_BUCKET=$BucketName,VERTEX_CHAT_DEPLOYMENT=gemini-2.5-flash,AGENT_SERVICE_URL=$AgentServiceUrl,AGENT_SERVICE_REQUIRES_AUTH=true"

for ($attempt = 1; $attempt -le 3; $attempt++) {
    gcloud run deploy $ServiceName `
        --image $ImageTag `
        --platform managed `
        --region $Region `
        --allow-unauthenticated `
        --execution-environment gen2 `
        --session-affinity `
        --service-account $ServiceAccount `
        --add-cloudsql-instances $SqlConnectionName `
        --cpu 2 `
        --memory 1Gi `
        --min-instances $MinInstances `
        --max-instances 10 `
        --concurrency 80 `
        --timeout 300 `
        --set-env-vars $EnvVars `
        --set-secrets "PUBSUB_VERIFICATION_TOKEN=janani-pubsub-token:latest,DATABASE_URL=janani-database-url:latest,GOOGLE_CALENDAR_CLIENT_ID=janani-google-calendar-client-id:latest,GOOGLE_CALENDAR_CLIENT_SECRET=janani-google-calendar-client-secret:latest" `
        --project $ProjectId
    if ($LASTEXITCODE -eq 0) { break }
    if ($attempt -lt 3) {
        Write-Host "  Deploy failed (attempt $attempt/3) - retrying in 20s..." -ForegroundColor Cyan
        Start-Sleep -Seconds 20
    } else {
        Write-Host "`nCloud Run deploy failed after 3 attempts." -ForegroundColor Red
        exit 1
    }
}

# -- 10. Done -------------------------------------------------------------------
Write-Host "`n[10/10] === Janani Deployment Complete! ===" -ForegroundColor Green
Write-Host "Service URL: $CloudRunBase" -ForegroundColor Cyan
Write-Host "Health:      $CloudRunBase/health" -ForegroundColor Cyan
Write-Host "Cloud SQL:   $SqlConnectionName ($SqlTier)" -ForegroundColor Cyan
Write-Host "min-instances=$MinInstances - pass -MinInstances 1 to avoid cold starts (always-on cost)." -ForegroundColor Cyan
