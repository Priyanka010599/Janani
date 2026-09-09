// Program.cs
// Janani — Google Cloud Native Multi-Agent System.
// AI:       all agent calls go to the Python ADK service (agents/) over
//           HTTP — see AGENT_SERVICE_URL below. That service talks to
//           Gemini via Vertex AI directly (no OpenAI-compat layer, no API
//           key in this app).
// Database: DATABASE_URL set → Cloud SQL (PostgreSQL); otherwise SQLite (local dev)
// Storage:  GCP_STORAGE_BUCKET → Google Cloud Storage
// Events:   GCP_PROJECT_ID → Google Cloud Pub/Sub topics

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Janani.Components;
using Janani.Data;
using Janani.Models;
using Janani.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ─────────────────────────────────────────────────────────────────
// K_SERVICE is set automatically on every Cloud Run instance (a Knative env
// var) but never locally, so this only switches on in production. JSON lines
// on stdout are what Cloud Logging's agent parses into a queryable
// jsonPayload instead of one opaque text blob per entry -- no new GCP
// resource or client library needed, Cloud Run already ships stdout there.
// Local `dotnet run` keeps the default human-readable console.
// K_SERVICE is set on every Cloud Run instance and nowhere else, which makes
// it the one reliable "am I actually deployed?" signal — more reliable here
// than IsDevelopment(), because ASPNETCORE_ENVIRONMENT is deliberately set to
// Development on the deployed image so the edge-device demo can post vitals
// without a provisioned DEVICE_INGEST_TOKEN. Anything whose safety depends on
// being deployed keys off this, not off the environment name.
var isCloudRun = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("K_SERVICE"));

if (isCloudRun)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.UseUtcTimestamp = true;
        options.IncludeScopes = true;
    });
}

// ── Database ────────────────────────────────────────────────────────────────
// Cloud SQL (PostgreSQL) on GCP when DATABASE_URL is set; SQLite for local dev.
// Cloud Run: set DATABASE_URL via Secret Manager or --set-secrets.
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
void ConfigureDb(DbContextOptionsBuilder options)
{
    if (!string.IsNullOrEmpty(databaseUrl))
        options.UseNpgsql(databaseUrl);          // Cloud SQL (PostgreSQL)
    else
        options.UseSqlite("Data Source=janani.db"); // Local dev fallback
}
// Registered as a factory (not AddDbContext) so DbContextOptions<AppDbContext>
// has exactly one, singleton-compatible registration — AddDbContext's own
// DbContextOptions registration is Scoped, which conflicts with any
// singleton-lifetime factory for the same context type and fails
// ASP.NET Core's service-provider validation at startup. The scoped
// AppDbContext everywhere else injects is then derived from the factory
// below, so page code is unaffected.
builder.Services.AddDbContextFactory<AppDbContext>(ConfigureDb);
// Independent instance per resolution — needed for anything that runs
// alongside a page's own DbContext usage in the same render batch (NavMenu
// via UserProfileCache); see UserProfileCache's comment for why a shared
// scoped DbContext isn't safe there even after memoizing in-flight calls.
builder.Services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
builder.Services.AddScoped<UserProfileCache>();
builder.Services.AddScoped<CareAccessService>();
builder.Services.AddSingleton<IElderDeviceAuthenticator, GlobalElderDeviceAuthenticator>();
builder.Services.AddScoped<IPregnancyDeviceAuthenticator, PerUserPregnancyDeviceAuthenticator>();

// Persist the Data Protection key ring in the same database — see
// AppDbContext's IDataProtectionKeyContext comment for why this matters.
// The key ring protects BOTH the auth cookie and the Google Calendar
// access/refresh tokens (see GoogleCalendarSyncService). Persisted alone, it
// sits in plaintext in the same database as the ciphertext it protects — so a
// single database read would yield the power to forge any user's session and
// to decrypt every connected user's Google refresh token. DP_KMS_KEY_NAME
// wraps the ring with Cloud KMS so the database alone is no longer enough.
//
// NOT YET FIXED — known, accepted risk. The intended fix is
// .ProtectKeysWithGoogleKms(...), but the only published package
// (Google.Cloud.AspNetCore.DataProtection.Kms) is 1.0.0-alpha04, and an alpha
// dependency sitting on the auth-cookie path is a worse trade than the risk it
// closes. Alternatives when this is picked up: wrap with an X.509 cert via
// ProtectKeysWithCertificate, or move the calendar tokens off DataProtection
// onto AES-GCM with a key held in Secret Manager. Until then, treat database
// backups as containing session-forging and OAuth-token material, and restrict
// them accordingly.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("Janani");

// ── Auth ────────────────────────────────────────────────────────────────────
// Cookie auth + PasswordHasher<TUser> (both ship in the ASP.NET Core shared
// framework already referenced via Sdk.Web — no new packages needed). Not
// full ASP.NET Core Identity: this app is single-tenant-per-user with no
// roles/email-confirmation/etc., so UserManager/SignInManager would be far
// more ceremony than the app needs.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "Janani.Auth";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        // SameSite stays Lax rather than Strict: the Google Calendar OAuth
        // callback (/calendar/callback/google) is a cross-site top-level
        // navigation back from Google's consent screen, and Strict would
        // withhold the auth cookie there — the callback would fail auth and
        // calendar connect would break. Lax still blocks the cross-site POST
        // and subresource cases, and UseAntiforgery covers form posts.
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Always over HTTPS once deployed. Local dev runs on plain
        // http://localhost, where an Always cookie would simply never be set
        // and login would fail silently — so it's relaxed there only.
        options.Cookie.SecurePolicy = isCloudRun
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<ICalendarSyncService, GoogleCalendarSyncService>();
// Resend when a key is present, Gmail SMTP otherwise. Not a preference toggle —
// it's so the currently-deployed instance, which only holds the SMTP secrets,
// keeps sending doctor alerts until the Resend key is actually provisioned.
// Once RESEND_API_KEY is set everywhere, the SMTP implementation and its
// secrets can be retired. See ResendEmailNotificationService's header for why
// neither can authenticate as the Cloud Run service account.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RESEND_API_KEY")?.Trim()))
    builder.Services.AddHttpClient<IEmailNotificationService, ResendEmailNotificationService>();
else
    builder.Services.AddSingleton<IEmailNotificationService, SmtpEmailNotificationService>();

// ── Rate limiting ───────────────────────────────────────────────────────────
// Login had no throttle of any kind: unlimited password guesses, no lockout,
// against a 30-day sliding cookie. The device-ingest endpoints were equally
// unbounded, and every agent-backed page costs real Vertex AI spend per call.
//
// This is a global limiter that partitions BY PATH and lets everything else
// through unlimited on purpose — Blazor Server's SignalR circuit (/_blazor)
// carries every UI interaction over one long-lived connection, so a blanket
// limiter would throttle ordinary clicking and break the app.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path.Value ?? "";
        // Behind Cloud Run, RemoteIpAddress is the real client only because
        // UseForwardedHeaders has already rewritten it from X-Forwarded-For.
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (path.StartsWith("/login", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/register", StringComparison.OrdinalIgnoreCase))
        {
            // 20 rather than a tighter 5-10: several people demoing from
            // behind one NAT share a source IP, and being locked out at the
            // login screen mid-demo is its own kind of failure. 20 attempts
            // per quarter-hour is still nowhere near enough to brute-force a
            // password, which is what this is here to stop.
            return RateLimitPartition.GetFixedWindowLimiter($"auth:{client}", _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(15),
                    QueueLimit = 0
                });
        }

        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            // Generous enough for a device flushing a store-and-forward
            // backlog in capture order after a reconnect (edge-device-sim/).
            return RateLimitPartition.GetFixedWindowLimiter($"api:{client}", _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
        }

        return RateLimitPartition.GetNoLimiter("unlimited");
    });
});

// ── Response compression ───────────────────────────────────────────────────
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

// ── Google Cloud Services ───────────────────────────────────────────────────
builder.Services.AddSingleton<GoogleCloudStorageService>();
builder.Services.AddSingleton<BookRecommendationService>();
builder.Services.AddSingleton<GooglePubSubEventService>();
builder.Services.AddHttpClient<PushNotificationService>();

// ── Blazor ──────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Agents ported to the Python ADK service (agents/). AGENT_SERVICE_URL
// points at that service: the local dev default assumes
// `uvicorn main:app --port 8001` running alongside this app. Cloud Run sets
// the real inter-service URL, and the agent service there requires
// authentication (--no-allow-unauthenticated) — AGENT_SERVICE_REQUIRES_AUTH
// switches on the ID-token handler for that case; local dev talks to the
// agent service directly over plain HTTP with no auth needed.
var agentServiceUrl = Environment.GetEnvironmentVariable("AGENT_SERVICE_URL") ?? "http://localhost:8001";
var agentServiceRequiresAuth = Environment.GetEnvironmentVariable("AGENT_SERVICE_REQUIRES_AUTH") == "true";

var mealAgentClient = builder.Services.AddHttpClient<MealAgent>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
var dayNurtureClient = builder.Services.AddHttpClient<DayNurtureService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
var partnerAgentClient = builder.Services.AddHttpClient<PartnerAgent>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
// Longer timeout than the structured-output agents above — a full
// conversational/document reply streams for longer than a single JSON response.
var companionClient = builder.Services.AddHttpClient<CompanionService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
});
var birthPlanClient = builder.Services.AddHttpClient<BirthPlanAgent>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
});
var elderNurtureClient = builder.Services.AddHttpClient<ElderNurtureService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
var healthMonitorClient = builder.Services.AddHttpClient<HealthMonitorService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
var pregnancyVitalsMonitorClient = builder.Services.AddHttpClient<PregnancyVitalsMonitorService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
var postpartumRecoveryMonitorClient = builder.Services.AddHttpClient<PostpartumRecoveryMonitorService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<PostpartumCheckInService>();
builder.Services.AddScoped<VitalsIngestService>();
builder.Services.AddScoped<PregnancyVitalsIngestService>();
var caregiverClient = builder.Services.AddHttpClient<CaregiverCoordinationService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
var infantCareClient = builder.Services.AddHttpClient<InfantCareService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
// Google Search grounding takes noticeably longer than the other one-shot
// agents above — matches the streaming/document agents' timeout, not the
// plain structured-output agents'.
var medicineLookupClient = builder.Services.AddHttpClient<MedicineLookupService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(45);
});
var recipeClient = builder.Services.AddHttpClient<RecipeService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(45);
});
var elderTutorClient = builder.Services.AddHttpClient<ElderTutorService>(client =>
{
    client.BaseAddress = new Uri(agentServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(45);
});
if (agentServiceRequiresAuth)
{
    mealAgentClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    dayNurtureClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    partnerAgentClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    companionClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    birthPlanClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    elderNurtureClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    healthMonitorClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    pregnancyVitalsMonitorClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    postpartumRecoveryMonitorClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    caregiverClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    infantCareClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    medicineLookupClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    recipeClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
    elderTutorClient.AddHttpMessageHandler(() => new GoogleIdTokenHandler(agentServiceUrl));
}

builder.Services.AddScoped<BloomAgentRegistry>();
builder.Services.AddScoped<AgentHealthService>();
var app = builder.Build();

// Apply EF Core migrations on startup
var isSqlite = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DATABASE_URL"));
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // WAL mode: SQLite only — PostgreSQL handles its own transaction isolation.
    if (isSqlite)
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    // Everything below is SQLite-only retrofit logic for the existing local
    // janani.db (SQLite-specific syntax: PRAGMA table_info, AUTOINCREMENT).
    // A fresh Postgres database doesn't need any of it - EnsureCreated()
    // above already builds the complete, correct schema (tables, indexes,
    // and UserId columns) straight from the EF model in one pass.
    if (isSqlite)
    {

    // EnsureCreated() only builds the schema on a brand-new database, so it
    // won't retrofit these indexes onto an existing janani.db - create them
    // directly (idempotent) so upgrades pick them up without a migration.
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_WaterIntake_Date ON WaterIntake (Date);");
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_KickSessions_Date ON KickSessions (Date);");
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Appointments_AppointmentDate ON Appointments (AppointmentDate);");
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_JournalEntries_CreatedAt ON JournalEntries (CreatedAt);");
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_MoodCheckIns_CheckedAt ON MoodCheckIns (CheckedAt);");

    // EmergencyContacts was added after janani.db already existed for some
    // users — EnsureCreated() won't add a new table to an existing database,
    // so create it directly (idempotent, matches EF's default conventions).
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS EmergencyContacts (
            Id INTEGER NOT NULL CONSTRAINT PK_EmergencyContacts PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Relation TEXT NOT NULL,
            Phone TEXT NULL,
            Email TEXT NULL,
            IsDoctor INTEGER NOT NULL,
            CreatedAt TEXT NOT NULL
        );
        """);

    // Same story for the medicine/vitamin reminder tables.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS Medicines (
            Id INTEGER NOT NULL CONSTRAINT PK_Medicines PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Dosage TEXT NULL,
            ScheduleTimes TEXT NOT NULL,
            IsActive INTEGER NOT NULL,
            CreatedAt TEXT NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS MedicineDoses (
            Id INTEGER NOT NULL CONSTRAINT PK_MedicineDoses PRIMARY KEY AUTOINCREMENT,
            MedicineId INTEGER NOT NULL,
            Date TEXT NOT NULL,
            ScheduledTime TEXT NOT NULL,
            Taken INTEGER NOT NULL,
            TakenAt TEXT NULL,
            CONSTRAINT FK_MedicineDoses_Medicines_MedicineId FOREIGN KEY (MedicineId) REFERENCES Medicines (Id) ON DELETE CASCADE
        );
        """);
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_MedicineDoses_MedicineId_Date ON MedicineDoses (MedicineId, Date);");

    // Same story for journal photos.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS JournalPhotos (
            Id INTEGER NOT NULL CONSTRAINT PK_JournalPhotos PRIMARY KEY AUTOINCREMENT,
            JournalEntryId INTEGER NOT NULL,
            ImageData TEXT NOT NULL,
            ContentType TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            CONSTRAINT FK_JournalPhotos_JournalEntries_JournalEntryId FOREIGN KEY (JournalEntryId) REFERENCES JournalEntries (Id) ON DELETE CASCADE
        );
        """);
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_JournalPhotos_JournalEntryId ON JournalPhotos (JournalEntryId);");

    // Same story for connected calendar accounts (per-user Google Calendar
    // OAuth, added after janani.db already existed for some users).
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS ConnectedCalendarAccounts (
            Id INTEGER NOT NULL CONSTRAINT PK_ConnectedCalendarAccounts PRIMARY KEY AUTOINCREMENT,
            UserId INTEGER NOT NULL,
            Provider TEXT NOT NULL,
            EncryptedAccessToken TEXT NOT NULL,
            EncryptedRefreshToken TEXT NOT NULL,
            ExpiresAt TEXT NOT NULL,
            GoogleCalendarId TEXT NOT NULL,
            ConnectedAt TEXT NOT NULL,
            SyncEnabled INTEGER NOT NULL,
            LastSyncFailed INTEGER NOT NULL,
            LastSyncError TEXT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ConnectedCalendarAccounts_UserId ON ConnectedCalendarAccounts (UserId);");
    db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_ConnectedCalendarAccounts_UserId_Provider ON ConnectedCalendarAccounts (UserId, Provider);");

    // SQLite has no "ALTER TABLE ... ADD COLUMN IF NOT EXISTS" (unlike
    // CREATE TABLE below), so check via PRAGMA table_info first. DEFAULT 0
    // is deliberate: it's the "unowned / pre-auth data" sentinel that the
    // first-registrant backfill in Register.razor looks for and claims.
    bool ColumnExists(string table, string column)
    {
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // GoogleEventId added after Appointments already existed for some users —
    // check via PRAGMA table_info first (SQLite has no ADD COLUMN IF NOT EXISTS).
    if (!ColumnExists("Appointments", "GoogleEventId"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE Appointments ADD COLUMN GoogleEventId TEXT NULL;");
    }

    // Same story for the Users table (multi-user login).
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS Users (
            Id INTEGER NOT NULL CONSTRAINT PK_Users PRIMARY KEY AUTOINCREMENT,
            Username TEXT NOT NULL,
            Email TEXT NOT NULL,
            PasswordHash TEXT NOT NULL,
            CreatedAt TEXT NOT NULL
        );
        """);
    // Login switched from email to username after some real accounts were
    // already registered against the old "Email" column — rename in place
    // (SQLite 3.25+ supports this) rather than dropping the table, so
    // existing accounts and all their backfilled data survive untouched.
    if (!ColumnExists("Users", "Username") && ColumnExists("Users", "Email"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE Users RENAME COLUMN Email TO Username;");
    }
    // Username and Email are now separate (username is public/nav-bar,
    // email is private/never displayed) — add Email back as its own column
    // for tables that only have the old single Username/Email column so far.
    // Existing accounts get an empty Email until they re-save it somewhere;
    // there's no profile-editing page yet to backfill a real value from.
    if (!ColumnExists("Users", "Email"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN Email TEXT NOT NULL DEFAULT '';");
    }
    db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users (Username);");
    db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Email ON Users (Email);");

    void AddUserIdColumnIfMissing(string table)
    {
        if (ColumnExists(table, "UserId")) return;
        db.Database.ExecuteSqlRaw($"ALTER TABLE {table} ADD COLUMN UserId INTEGER NOT NULL DEFAULT 0;");
        db.Database.ExecuteSqlRaw($"CREATE INDEX IF NOT EXISTS IX_{table}_UserId ON {table} (UserId);");
    }

    foreach (var table in new[]
    {
        "UserProfiles", "MoodCheckIns", "JournalEntries", "Appointments",
        "WaterIntake", "KickSessions", "Medicines", "EmergencyContacts"
    })
    {
        AddUserIdColumnIfMissing(table);
    }

    // TracksPregnancy added after UserProfiles already existed for some
    // users — default 1 (true) so existing accounts keep seeing what they
    // already see; Profile.razor lets them turn it off.
    if (!ColumnExists("UserProfiles", "TracksPregnancy"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN TracksPregnancy INTEGER NOT NULL DEFAULT 1;");
    }

    // Language (AppLanguage enum ordinal, 0 = English) — same story.
    if (!ColumnExists("UserProfiles", "Language"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN Language INTEGER NOT NULL DEFAULT 0;");
    }

    // CaresForElder/CaresForInfant — same story, default 0 (false) since
    // most existing accounts aren't caring for either.
    if (!ColumnExists("UserProfiles", "CaresForElder"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN CaresForElder INTEGER NOT NULL DEFAULT 0;");
    }
    if (!ColumnExists("UserProfiles", "CaresForInfant"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN CaresForInfant INTEGER NOT NULL DEFAULT 0;");
    }

    // DoctorName/DoctorEmail — who a Critical pregnancy vitals alert
    // auto-emails, same story as the elder-care columns above.
    if (!ColumnExists("UserProfiles", "DoctorName"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN DoctorName TEXT NULL;");
    }
    if (!ColumnExists("UserProfiles", "DoctorEmail"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN DoctorEmail TEXT NULL;");
    }

    // Postpartum Recovery Guide capture — DeliveryDate/DeliveryType/
    // FeedingMethod are nullable (unset until she's actually delivered);
    // DeliveryComplications is a [Flags] enum, defaults to 0 (None).
    if (!ColumnExists("UserProfiles", "DeliveryDate"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN DeliveryDate TEXT NULL;");
    }
    if (!ColumnExists("UserProfiles", "DeliveryType"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN DeliveryType INTEGER NULL;");
    }
    if (!ColumnExists("UserProfiles", "DeliveryComplications"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN DeliveryComplications INTEGER NOT NULL DEFAULT 0;");
    }
    if (!ColumnExists("UserProfiles", "FeedingMethod"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN FeedingMethod INTEGER NULL;");
    }

    // Bereavement capture — all nullable; unset means "no outcome recorded",
    // which CareContext treats as equivalent to LiveBirth (no suppression).
    if (!ColumnExists("UserProfiles", "BirthOutcome"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN BirthOutcome INTEGER NULL;");
    }
    if (!ColumnExists("UserProfiles", "BabyName"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN BabyName TEXT NULL;");
    }
    if (!ColumnExists("UserProfiles", "BabyDate"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN BabyDate TEXT NULL;");
    }

    // Pregnancy vitals device pairing — null until the user generates one in
    // Settings. See UserProfile.DevicePairingToken's comment for why this is
    // per-user rather than one global token like elder/infant vitals ingest.
    if (!ColumnExists("UserProfiles", "DevicePairingToken"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN DevicePairingToken TEXT NULL;");
    }

    } // isSqlite

    // Same TracksPregnancy/Language backfill as above, Postgres side —
    // unlike SQLite, Postgres supports "ADD COLUMN IF NOT EXISTS" natively
    // so this doesn't need a separate existence check.
    if (!isSqlite)
    {
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "TracksPregnancy" boolean NOT NULL DEFAULT true;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "Language" integer NOT NULL DEFAULT 0;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "CaresForElder" boolean NOT NULL DEFAULT false;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "CaresForInfant" boolean NOT NULL DEFAULT false;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "DoctorName" text NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "DoctorEmail" text NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "DeliveryDate" date NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "DeliveryType" integer NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "DeliveryComplications" integer NOT NULL DEFAULT 0;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "FeedingMethod" integer NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "BirthOutcome" integer NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "BabyName" text NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "BabyDate" date NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "DevicePairingToken" text NULL;""");
    }

    // Elder-care tables were added after BOTH the local janani.db AND the
    // live Cloud SQL Postgres database already existed — EnsureCreated()
    // only builds schema for a brand-new database, so unlike everything in
    // the isSqlite block above, this has to run for Postgres too, not just
    // SQLite. Branch on syntax, not on whether to run at all.
    //
    // Every identifier below is double-quoted. Postgres folds unquoted
    // identifiers to lowercase, but EF Core always generates double-quoted,
    // case-preserved identifiers in its queries (e.g. FROM "ElderProfiles") —
    // an earlier unquoted version of this block created lowercase tables
    // Postgres was happy with but EF Core could never find
    // (42P01 "relation does not exist" on every query). SQLite accepts
    // double-quoted identifiers too, so this is safe for both providers.
    var idColumn = isSqlite
        ? "\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT"
        : "\"Id\" SERIAL PRIMARY KEY";
    var textType = isSqlite ? "TEXT" : "text";
    var timestampType = isSqlite ? "TEXT" : "timestamp with time zone";
    var decimalType = isSqlite ? "TEXT" : "numeric";
    // SQLite has no native boolean (existing bool columns use INTEGER 0/1,
    // e.g. Medicine.IsActive above); Postgres does, and EF's Npgsql provider
    // expects a real "boolean" column to translate bool queries against —
    // every other bool column here came from EnsureCreated() on Postgres,
    // which already generates "boolean", so this raw SQL has to match it.
    var boolType = isSqlite ? "INTEGER" : "boolean";

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "ElderProfiles" (
            {idColumn},
            "UserId" INTEGER NOT NULL,
            "Name" {textType} NOT NULL,
            "Age" INTEGER NOT NULL,
            "Relation" {textType} NOT NULL,
            "MedicalNotes" {textType} NULL,
            "CreatedAt" {timestampType} NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_ElderProfiles_UserId" ON "ElderProfiles" ("UserId");""");

    // DoctorName/DoctorEmail added after ElderProfiles already existed —
    // same ADD COLUMN dance as everywhere else in this file: SQLite has no
    // "IF NOT EXISTS" for ADD COLUMN so it needs an existence check first,
    // Postgres supports it natively.
    if (isSqlite)
    {
        bool ElderColumnExists(string column)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA table_info(\"ElderProfiles\");";
            if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        if (!ElderColumnExists("DoctorName"))
            db.Database.ExecuteSqlRaw("ALTER TABLE ElderProfiles ADD COLUMN DoctorName TEXT NULL;");
        if (!ElderColumnExists("DoctorEmail"))
            db.Database.ExecuteSqlRaw("ALTER TABLE ElderProfiles ADD COLUMN DoctorEmail TEXT NULL;");
    }
    else
    {
        db.Database.ExecuteSqlRaw("""ALTER TABLE "ElderProfiles" ADD COLUMN IF NOT EXISTS "DoctorName" text NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "ElderProfiles" ADD COLUMN IF NOT EXISTS "DoctorEmail" text NULL;""");
    }

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "VitalsReadings" (
            {idColumn},
            "ElderProfileId" INTEGER NOT NULL,
            "RecordedAt" {timestampType} NOT NULL,
            "SystolicBp" INTEGER NULL,
            "DiastolicBp" INTEGER NULL,
            "HeartRate" INTEGER NULL,
            "TemperatureC" {decimalType} NULL,
            "OxygenSaturation" INTEGER NULL,
            "Notes" {textType} NULL,
            "Source" {textType} NULL,
            "DeviceReadingId" {textType} NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_VitalsReadings_ElderProfileId" ON "VitalsReadings" ("ElderProfileId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_VitalsReadings_RecordedAt" ON "VitalsReadings" ("RecordedAt");""");

    // Source added after VitalsReadings already existed — same ADD COLUMN
    // dance as DoctorName/DoctorEmail above.
    if (isSqlite)
    {
        bool VitalsColumnExists(string column)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA table_info(\"VitalsReadings\");";
            if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        if (!VitalsColumnExists("Source"))
            db.Database.ExecuteSqlRaw("ALTER TABLE VitalsReadings ADD COLUMN Source TEXT NULL;");
        if (!VitalsColumnExists("DeviceReadingId"))
            db.Database.ExecuteSqlRaw("ALTER TABLE VitalsReadings ADD COLUMN DeviceReadingId TEXT NULL;");
    }
    else
    {
        db.Database.ExecuteSqlRaw("""ALTER TABLE "VitalsReadings" ADD COLUMN IF NOT EXISTS "Source" text NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "VitalsReadings" ADD COLUMN IF NOT EXISTS "DeviceReadingId" text NULL;""");
    }

    // Idempotency for device-uploaded readings: a retried upload of the same
    // reading (same DeviceReadingId) must never create a second row. Partial
    // (WHERE ... IS NOT NULL) so it never conflicts with the many manually
    // entered readings that have none — identical syntax on both providers.
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_VitalsReadings_DeviceReadingId" ON "VitalsReadings" ("DeviceReadingId") WHERE "DeviceReadingId" IS NOT NULL;""");

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "ElderCheckIns" (
            {idColumn},
            "ElderProfileId" INTEGER NOT NULL,
            "CheckedAt" {timestampType} NOT NULL,
            "Mood" INTEGER NOT NULL,
            "AdditionalNote" {textType} NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_ElderCheckIns_ElderProfileId" ON "ElderCheckIns" ("ElderProfileId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_ElderCheckIns_CheckedAt" ON "ElderCheckIns" ("CheckedAt");""");

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "Alerts" (
            {idColumn},
            "ElderProfileId" INTEGER NOT NULL,
            "VitalsReadingId" INTEGER NULL,
            "Severity" INTEGER NOT NULL,
            "Message" {textType} NOT NULL,
            "TriggeredAt" {timestampType} NOT NULL,
            "Acknowledged" {boolType} NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Alerts_ElderProfileId" ON "Alerts" ("ElderProfileId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Alerts_TriggeredAt" ON "Alerts" ("TriggeredAt");""");

    // Date-only (no time-of-day) — SQLite's provider stores DateOnly as TEXT
    // (ISO date string), Npgsql maps it to a native "date" column.
    var dateType = isSqlite ? "TEXT" : "date";

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "InfantProfiles" (
            {idColumn},
            "UserId" INTEGER NOT NULL,
            "Name" {textType} NOT NULL,
            "DateOfBirth" {dateType} NOT NULL,
            "CreatedAt" {timestampType} NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_InfantProfiles_UserId" ON "InfantProfiles" ("UserId");""");

    if (isSqlite)
    {
        bool InfantColumnExists(string column)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA table_info(\"InfantProfiles\");";
            if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        if (!InfantColumnExists("DoctorName"))
            db.Database.ExecuteSqlRaw("ALTER TABLE InfantProfiles ADD COLUMN DoctorName TEXT NULL;");
        if (!InfantColumnExists("DoctorEmail"))
            db.Database.ExecuteSqlRaw("ALTER TABLE InfantProfiles ADD COLUMN DoctorEmail TEXT NULL;");
    }
    else
    {
        db.Database.ExecuteSqlRaw("""ALTER TABLE "InfantProfiles" ADD COLUMN IF NOT EXISTS "DoctorName" text NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "InfantProfiles" ADD COLUMN IF NOT EXISTS "DoctorEmail" text NULL;""");
    }

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "FeedingLogs" (
            {idColumn},
            "InfantProfileId" INTEGER NOT NULL,
            "FedAt" {timestampType} NOT NULL,
            "Type" INTEGER NOT NULL,
            "Notes" {textType} NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FeedingLogs_InfantProfileId" ON "FeedingLogs" ("InfantProfileId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FeedingLogs_FedAt" ON "FeedingLogs" ("FedAt");""");

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "SleepLogs" (
            {idColumn},
            "InfantProfileId" INTEGER NOT NULL,
            "SleepStart" {timestampType} NOT NULL,
            "SleepEnd" {timestampType} NULL,
            "Notes" {textType} NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SleepLogs_InfantProfileId" ON "SleepLogs" ("InfantProfileId");""");

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "GrowthEntries" (
            {idColumn},
            "InfantProfileId" INTEGER NOT NULL,
            "RecordedAt" {timestampType} NOT NULL,
            "WeightKg" {decimalType} NULL,
            "HeightCm" {decimalType} NULL,
            "HeadCircumferenceCm" {decimalType} NULL,
            "Source" {textType} NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_GrowthEntries_InfantProfileId" ON "GrowthEntries" ("InfantProfileId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_GrowthEntries_RecordedAt" ON "GrowthEntries" ("RecordedAt");""");

    if (isSqlite)
    {
        bool GrowthColumnExists(string column)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA table_info(\"GrowthEntries\");";
            if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        if (!GrowthColumnExists("Source"))
            db.Database.ExecuteSqlRaw("ALTER TABLE GrowthEntries ADD COLUMN Source TEXT NULL;");
    }
    else
    {
        db.Database.ExecuteSqlRaw("""ALTER TABLE "GrowthEntries" ADD COLUMN IF NOT EXISTS "Source" text NULL;""");
    }

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "VaccinationRecords" (
            {idColumn},
            "InfantProfileId" INTEGER NOT NULL,
            "VaccineName" {textType} NOT NULL,
            "GivenAt" {timestampType} NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_VaccinationRecords_InfantProfileId" ON "VaccinationRecords" ("InfantProfileId");""");

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "InfantAlerts" (
            {idColumn},
            "InfantProfileId" INTEGER NOT NULL,
            "GrowthEntryId" INTEGER NULL,
            "Severity" INTEGER NOT NULL,
            "Message" {textType} NOT NULL,
            "TriggeredAt" {timestampType} NOT NULL,
            "Acknowledged" {boolType} NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_InfantAlerts_InfantProfileId" ON "InfantAlerts" ("InfantProfileId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_InfantAlerts_TriggeredAt" ON "InfantAlerts" ("TriggeredAt");""");

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "PushSubscriptions" (
            {idColumn},
            "UserId" INTEGER NOT NULL,
            "Token" {textType} NOT NULL,
            "CreatedAt" {timestampType} NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PushSubscriptions_UserId" ON "PushSubscriptions" ("UserId");""");
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_PushSubscriptions_Token" ON "PushSubscriptions" ("Token");""");


    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "SharedCareAccess" (
            {idColumn},
            "ElderProfileId" INTEGER NULL,
            "InfantProfileId" INTEGER NULL,
            "UserId" INTEGER NOT NULL,
            "GrantedByUserId" INTEGER NOT NULL,
            "GrantedAt" {timestampType} NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SharedCareAccess_UserId" ON "SharedCareAccess" ("UserId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SharedCareAccess_ElderProfileId" ON "SharedCareAccess" ("ElderProfileId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SharedCareAccess_InfantProfileId" ON "SharedCareAccess" ("InfantProfileId");""");

    // Backing store for PersistKeysToDbContext (see AppDbContext) — schema
    // matches Microsoft.AspNetCore.DataProtection.EntityFrameworkCore's
    // DataProtectionKey entity exactly (Id/FriendlyName/Xml).
    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "DataProtectionKeys" (
            {idColumn},
            "FriendlyName" {textType} NULL,
            "Xml" {textType} NULL
        );
        """);

    // CaresForElder/CaresForInfant default false for everyone (including
    // existing rows backfilled above) — without this, an account that
    // already added an elder/infant before these nav-gating flags existed
    // would suddenly lose its own Elder Care/Infant Care nav links.
    var trueLiteral = isSqlite ? "1" : "true";
    db.Database.ExecuteSqlRaw($"""
        UPDATE "UserProfiles" SET "CaresForElder" = {trueLiteral}
        WHERE EXISTS (SELECT 1 FROM "ElderProfiles" WHERE "ElderProfiles"."UserId" = "UserProfiles"."UserId");
        """);
    db.Database.ExecuteSqlRaw($"""
        UPDATE "UserProfiles" SET "CaresForInfant" = {trueLiteral}
        WHERE EXISTS (SELECT 1 FROM "InfantProfiles" WHERE "InfantProfiles"."UserId" = "UserProfiles"."UserId");
        """);

    // Pregnancy-side counterpart to VitalsReadings/Alerts — UserId-keyed
    // (no separate shareable profile table for pregnancy, UserProfile is it)
    // rather than ElderProfileId-keyed. See PregnancyVitalsReading/PregnancyAlert
    // in Models.cs for why these are their own tables, not a widened Alerts.
    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "PregnancyVitalsReadings" (
            {idColumn},
            "UserId" INTEGER NOT NULL,
            "RecordedAt" {timestampType} NOT NULL,
            "SystolicBp" INTEGER NULL,
            "DiastolicBp" INTEGER NULL,
            "HeartRate" INTEGER NULL,
            "TemperatureC" {decimalType} NULL,
            "OxygenSaturation" INTEGER NULL,
            "Notes" {textType} NULL,
            "Source" {textType} NULL,
            "DeviceReadingId" {textType} NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PregnancyVitalsReadings_UserId" ON "PregnancyVitalsReadings" ("UserId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PregnancyVitalsReadings_RecordedAt" ON "PregnancyVitalsReadings" ("RecordedAt");""");

    // DeviceReadingId added after PregnancyVitalsReadings already existed —
    // same ADD COLUMN dance as VitalsReadings (elder).
    if (isSqlite)
    {
        bool PregnancyVitalsColumnExists(string column)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA table_info(\"PregnancyVitalsReadings\");";
            if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        if (!PregnancyVitalsColumnExists("DeviceReadingId"))
            db.Database.ExecuteSqlRaw("ALTER TABLE PregnancyVitalsReadings ADD COLUMN DeviceReadingId TEXT NULL;");
    }
    else
    {
        db.Database.ExecuteSqlRaw("""ALTER TABLE "PregnancyVitalsReadings" ADD COLUMN IF NOT EXISTS "DeviceReadingId" text NULL;""");
    }
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_PregnancyVitalsReadings_DeviceReadingId" ON "PregnancyVitalsReadings" ("DeviceReadingId") WHERE "DeviceReadingId" IS NOT NULL;""");

    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "PregnancyAlerts" (
            {idColumn},
            "UserId" INTEGER NOT NULL,
            "PregnancyVitalsReadingId" INTEGER NULL,
            "Severity" INTEGER NOT NULL,
            "Message" {textType} NOT NULL,
            "TriggeredAt" {timestampType} NOT NULL,
            "Acknowledged" {boolType} NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PregnancyAlerts_UserId" ON "PregnancyAlerts" ("UserId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PregnancyAlerts_TriggeredAt" ON "PregnancyAlerts" ("TriggeredAt");""");

    // Recovery-plan content itself is computed on read (see
    // Services/RecoveryPlanGenerator.cs) -- this table only persists which
    // items a mother has marked done, keyed by the item's stable string Key.
    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "RecoveryPlanItemCompletions" (
            {idColumn},
            "UserId" INTEGER NOT NULL,
            "ItemKey" {textType} NOT NULL,
            "CompletedAt" {timestampType} NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_RecoveryPlanItemCompletions_UserId_ItemKey" ON "RecoveryPlanItemCompletions" ("UserId", "ItemKey");""");

    // Daily postpartum check-in — see Services/PostpartumRecoveryChecker.cs.
    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "PostpartumCheckIns" (
            {idColumn},
            "UserId" INTEGER NOT NULL,
            "CheckedAt" {timestampType} NOT NULL,
            "PainLevel" INTEGER NOT NULL,
            "Bleeding" INTEGER NOT NULL,
            "TemperatureC" {decimalType} NULL,
            "Wound" INTEGER NOT NULL,
            "Mood" INTEGER NOT NULL,
            "Notes" {textType} NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PostpartumCheckIns_UserId" ON "PostpartumCheckIns" ("UserId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PostpartumCheckIns_CheckedAt" ON "PostpartumCheckIns" ("CheckedAt");""");

    // Fed by either PostpartumRecoveryChecker (daily check-in) or
    // EpdsScreeningChecker (EPDS-10) — exactly one of the two FKs is set.
    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "PostpartumAlerts" (
            {idColumn},
            "UserId" INTEGER NOT NULL,
            "PostpartumCheckInId" INTEGER NULL,
            "EpdsScreeningId" INTEGER NULL,
            "Severity" INTEGER NOT NULL,
            "Message" {textType} NOT NULL,
            "TriggeredAt" {timestampType} NOT NULL,
            "Acknowledged" {boolType} NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PostpartumAlerts_UserId" ON "PostpartumAlerts" ("UserId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PostpartumAlerts_TriggeredAt" ON "PostpartumAlerts" ("TriggeredAt");""");

    // Edinburgh Postnatal Depression Scale — see EpdsScreeningChecker.cs.
    db.Database.ExecuteSqlRaw($"""
        CREATE TABLE IF NOT EXISTS "EpdsScreenings" (
            {idColumn},
            "UserId" INTEGER NOT NULL,
            "AdministeredAt" {timestampType} NOT NULL,
            "ScheduledDay" INTEGER NOT NULL,
            "Item1" INTEGER NOT NULL,
            "Item2" INTEGER NOT NULL,
            "Item3" INTEGER NOT NULL,
            "Item4" INTEGER NOT NULL,
            "Item5" INTEGER NOT NULL,
            "Item6" INTEGER NOT NULL,
            "Item7" INTEGER NOT NULL,
            "Item8" INTEGER NOT NULL,
            "Item9" INTEGER NOT NULL,
            "Item10" INTEGER NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_EpdsScreenings_UserId" ON "EpdsScreenings" ("UserId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_EpdsScreenings_AdministeredAt" ON "EpdsScreenings" ("AdministeredAt");""");

    SeedDemoAccount(db);
}

// Gives a judge/reviewer a populated account to look at instead of an empty
// shell — one account, one elder, one infant, weeks of vitals/growth history,
// and both an acknowledged and an active alert. Runs on every startup but is
// idempotent (checked via the "demo" username) so it only ever inserts once.
// Everything below runs inside one transaction so a crash mid-seed (it has
// happened — see the 2026-08-19 incident where a DateTime.Kind bug crashed
// the container partway through, leaving a demo user with no infant data)
// rolls back cleanly instead of leaving a half-seeded account that the
// existence check above would then skip forever.
void SeedDemoAccount(AppDbContext db)
{
    var existingDemoUser = db.Users.FirstOrDefault(u => u.Username == "demo");
    if (existingDemoUser != null)
    {
        var isComplete = db.InfantProfiles.Any(i => i.UserId == existingDemoUser.Id)
            && db.VaccinationRecords.Any(v => db.InfantProfiles.Where(i => i.UserId == existingDemoUser.Id).Select(i => i.Id).Contains(v.InfantProfileId));
        if (isComplete) return;

        // A prior seed attempt was interrupted partway through — wipe the
        // partial rows and reseed from scratch rather than leaving them.
        var staleElderIds = db.ElderProfiles.Where(e => e.UserId == existingDemoUser.Id).Select(e => e.Id).ToList();
        var staleInfantIds = db.InfantProfiles.Where(i => i.UserId == existingDemoUser.Id).Select(i => i.Id).ToList();
        db.Alerts.RemoveRange(db.Alerts.Where(a => staleElderIds.Contains(a.ElderProfileId)));
        db.VitalsReadings.RemoveRange(db.VitalsReadings.Where(v => staleElderIds.Contains(v.ElderProfileId)));
        db.ElderCheckIns.RemoveRange(db.ElderCheckIns.Where(c => staleElderIds.Contains(c.ElderProfileId)));
        db.ElderProfiles.RemoveRange(db.ElderProfiles.Where(e => staleElderIds.Contains(e.Id)));
        db.InfantAlerts.RemoveRange(db.InfantAlerts.Where(a => staleInfantIds.Contains(a.InfantProfileId)));
        db.GrowthEntries.RemoveRange(db.GrowthEntries.Where(g => staleInfantIds.Contains(g.InfantProfileId)));
        db.FeedingLogs.RemoveRange(db.FeedingLogs.Where(f => staleInfantIds.Contains(f.InfantProfileId)));
        db.SleepLogs.RemoveRange(db.SleepLogs.Where(s => staleInfantIds.Contains(s.InfantProfileId)));
        db.VaccinationRecords.RemoveRange(db.VaccinationRecords.Where(v => staleInfantIds.Contains(v.InfantProfileId)));
        db.InfantProfiles.RemoveRange(db.InfantProfiles.Where(i => staleInfantIds.Contains(i.Id)));
        db.EmergencyContacts.RemoveRange(db.EmergencyContacts.Where(c => c.UserId == existingDemoUser.Id));
        db.MoodCheckIns.RemoveRange(db.MoodCheckIns.Where(m => m.UserId == existingDemoUser.Id));
        db.JournalEntries.RemoveRange(db.JournalEntries.Where(j => j.UserId == existingDemoUser.Id));
        db.Appointments.RemoveRange(db.Appointments.Where(a => a.UserId == existingDemoUser.Id));
        db.Medicines.RemoveRange(db.Medicines.Where(m => m.UserId == existingDemoUser.Id));
        db.WaterIntake.RemoveRange(db.WaterIntake.Where(w => w.UserId == existingDemoUser.Id));
        db.KickSessions.RemoveRange(db.KickSessions.Where(k => k.UserId == existingDemoUser.Id));
        db.UserProfiles.RemoveRange(db.UserProfiles.Where(p => p.UserId == existingDemoUser.Id));
        db.Users.Remove(existingDemoUser);
        db.SaveChanges();
    }

    using var transaction = db.Database.BeginTransaction();

    var now = DateTime.UtcNow;
    var user = new User
    {
        Username = "demo",
        Email = "demo@janani.app",
        CreatedAt = now
    };
    user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "JananiDemo2026!");
    db.Users.Add(user);
    db.SaveChanges();

    db.UserProfiles.Add(new UserProfile
    {
        UserId = user.Id,
        Name = "Priya",
        PregnancyStartDate = DateOnly.FromDateTime(now.AddDays(-7 * 28)),
        WorkingWomanMode = true,
        TracksPregnancy = true,
        CaresForElder = true,
        CaresForInfant = true,
        Language = AppLanguage.English
    });

    db.EmergencyContacts.AddRange(
        new EmergencyContact { UserId = user.Id, Name = "Dr. Anita Shah", Relation = "OB-GYN", Phone = "+91 98765 43210", IsDoctor = true, CreatedAt = now },
        new EmergencyContact { UserId = user.Id, Name = "Karthik", Relation = "Partner", Phone = "+91 98765 11111", CreatedAt = now }
    );

    db.MoodCheckIns.Add(new MoodCheckIn { UserId = user.Id, CheckedAt = now.AddDays(-1), Mood = MoodType.Energetic, AdditionalNote = "Good day at work, feeling strong." });

    db.JournalEntries.Add(new JournalEntry { UserId = user.Id, CreatedAt = now.AddDays(-3), Title = "Felt the first flutter", Content = "Small, fluttery movements this evening — didn't expect it to feel like that.", WeekOfPregnancy = 20, MoodEmoji = "🥹" });

    db.Appointments.Add(new AppointmentEntry { UserId = user.Id, AppointmentDate = DateTime.SpecifyKind(now.AddDays(5), DateTimeKind.Utc), Title = "Anomaly scan", DoctorName = "Dr. Anita Shah", Location = "City Women's Hospital", QuestionsToAsk = "Ask about iron levels and travel safety for the upcoming trip." });

    var medicine = new Medicine { UserId = user.Id, Name = "Prenatal vitamins", Dosage = "1 tablet", ScheduleTimes = "08:00", IsActive = true, CreatedAt = now.AddDays(-30) };
    db.Medicines.Add(medicine);

    db.WaterIntake.Add(new WaterIntakeEntry { UserId = user.Id, Date = DateOnly.FromDateTime(now), GlassesCount = 5 });
    db.KickSessions.Add(new KickSession { UserId = user.Id, Date = DateOnly.FromDateTime(now.AddDays(-1)), KickCount = 12, DurationMinutes = 30 });

    // ── Elder care ──
    var elder = new ElderProfile { UserId = user.Id, Name = "Lakshmi", Age = 68, Relation = "Mother", MedicalNotes = "Managed hypertension, on daily medication.", DoctorName = "Dr. Ramesh Iyer", DoctorEmail = "demo.doctor@example.com", CreatedAt = now.AddDays(-45) };
    db.ElderProfiles.Add(elder);
    db.SaveChanges();

    var vitalsRandom = new Random(42);
    for (var i = 14; i >= 1; i--)
    {
        var abnormal = i == 1;
        db.VitalsReadings.Add(new VitalsReading
        {
            ElderProfileId = elder.Id,
            RecordedAt = now.AddDays(-i),
            SystolicBp = abnormal ? 188 : 118 + vitalsRandom.Next(-6, 10),
            DiastolicBp = abnormal ? 96 : 74 + vitalsRandom.Next(-5, 6),
            HeartRate = 68 + vitalsRandom.Next(-6, 10),
            TemperatureC = 36.6m,
            OxygenSaturation = 96 + vitalsRandom.Next(0, 3),
            Source = i % 3 == 0 ? "Withings BPM Connect (simulated)" : null
        });
    }
    db.SaveChanges();

    var oldAbnormalReading = db.VitalsReadings.Where(v => v.ElderProfileId == elder.Id).OrderBy(v => v.RecordedAt).Skip(3).First();
    var latestAbnormalReading = db.VitalsReadings.Where(v => v.ElderProfileId == elder.Id).OrderByDescending(v => v.RecordedAt).First();
    db.Alerts.AddRange(
        new Alert { ElderProfileId = elder.Id, VitalsReadingId = oldAbnormalReading.Id, Severity = AlertSeverity.Warning, Message = "Blood pressure was mildly elevated. Encourage rest and recheck this evening.", TriggeredAt = oldAbnormalReading.RecordedAt, Acknowledged = true },
        new Alert { ElderProfileId = elder.Id, VitalsReadingId = latestAbnormalReading.Id, Severity = AlertSeverity.Critical, Message = "Blood pressure reading (188/96) is significantly above the safe range. Please contact the doctor or seek urgent care.", TriggeredAt = latestAbnormalReading.RecordedAt, Acknowledged = false }
    );
    db.ElderCheckIns.Add(new ElderCheckIn { ElderProfileId = elder.Id, CheckedAt = now.AddDays(-1), Mood = ElderMoodType.Calm, AdditionalNote = "Had a good walk this morning." });

    // ── Infant care ──
    var infant = new InfantProfile { UserId = user.Id, Name = "Arjun", DateOfBirth = DateOnly.FromDateTime(now.AddDays(-7 * 22)), DoctorName = "Dr. Meera Nair", DoctorEmail = "demo.doctor@example.com", CreatedAt = now.AddDays(-7 * 22) };
    db.InfantProfiles.Add(infant);
    db.SaveChanges();

    var growthRandom = new Random(7);
    decimal weight = 3.4m;
    for (var w = 21; w >= 0; w--)
    {
        weight += 0.18m + (decimal)(growthRandom.NextDouble() * 0.05);
        db.GrowthEntries.Add(new GrowthEntry
        {
            InfantProfileId = infant.Id,
            RecordedAt = now.AddDays(-7 * w),
            WeightKg = Math.Round(weight, 2),
            HeightCm = Math.Round(50m + (22 - w) * 0.9m, 1),
            HeadCircumferenceCm = Math.Round(35m + (22 - w) * 0.35m, 1),
            Source = w % 4 == 0 ? "Withings Smart Scale (simulated)" : null
        });
    }

    db.FeedingLogs.AddRange(
        new FeedingLog { InfantProfileId = infant.Id, FedAt = now.AddHours(-2), Type = FeedingType.Breastfeeding },
        new FeedingLog { InfantProfileId = infant.Id, FedAt = now.AddHours(-6), Type = FeedingType.Formula, Notes = "120ml" }
    );
    db.SleepLogs.Add(new SleepLog { InfantProfileId = infant.Id, SleepStart = now.AddHours(-10), SleepEnd = now.AddHours(-8) });
    var birthDateTimeUtc = DateTime.SpecifyKind(infant.DateOfBirth.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    db.VaccinationRecords.AddRange(
        new VaccinationRecord { InfantProfileId = infant.Id, VaccineName = "BCG", GivenAt = birthDateTimeUtc },
        new VaccinationRecord { InfantProfileId = infant.Id, VaccineName = "OPV-0", GivenAt = birthDateTimeUtc },
        new VaccinationRecord { InfantProfileId = infant.Id, VaccineName = "Hepatitis B-1", GivenAt = birthDateTimeUtc }
    );

    db.SaveChanges();
    transaction.Commit();
}

// Cloud Run terminates TLS at its edge and forwards to the container over
// plain HTTP, so without this, every Request.Scheme/NavigationManager.Uri in
// the app reads "http" even for a real https:// visitor — that's already
// worked around locally for the OAuth redirect URI below (see
// BuildCalendarRedirectUri), but nothing previously
// fixed it globally. The concrete symptom: after login, NavigateTo(ReturnUrl,
// forceLoad:true) rebuilt ReturnUrl as "http://..." — invisible in a normal
// browser (same tab either way), but the Capacitor Android app's WebViewClient
// treats a scheme mismatch against its configured "https" appUrl as an
// external link and hands it to Chrome instead of keeping it in the app.
// KnownNetworks/KnownProxies are cleared because Cloud Run's edge isn't a
// fixed, listable proxy IP range — the container only ever receives traffic
// through it, so trusting its forwarded headers unconditionally is safe here.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Keyed on isCloudRun rather than !IsDevelopment(): the deployed image sets
// ASPNETCORE_ENVIRONMENT=Development for the demo, and under the old check
// that would have switched OFF the exception handler in production — serving
// the developer exception page, stack traces and all, to the public internet
// on any unhandled error. Whether an error page is safe to show depends on
// where the app is running, not on what the environment is called.
if (isCloudRun || !app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseRateLimiter();

// Baseline security response headers. Deliberately conservative on CSP: this
// is Blazor Server, and a script-src directive would have to account for the
// framework's own inline bootstrap plus the inline onclick handlers in
// Register.razor's privacy dialog, so locking scripts down is a separate
// change with its own testing. What's here can't break rendering:
//   nosniff        — stop MIME-sniffing an upload into something executable
//   frame-ancestors— clickjacking; the app is never meant to be framed
//   base-uri       — stop an injected <base> re-pointing every relative URL
//   object-src     — no plugin content anywhere in the app
//   Referrer-Policy— journal/vitals URLs shouldn't leak to third parties
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Frame-Options"] = "DENY";
    headers["Content-Security-Policy"] =
        "frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
    await next();
});

app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Only long-cache third-party vendor files that don't change with our
        // own edits (bootstrap). Our own CSS/JS (janani.css, janani-voice.js,
        // the Blazor-generated styles bundle) stays on normal ETag
        // revalidation so edits are picked up on the next load instead of
        // being served stale for up to a week.
        if (ctx.File.PhysicalPath?.Contains("bootstrap", StringComparison.OrdinalIgnoreCase) == true)
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=604800,immutable";
        }
        else
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Logout can't happen from inside an interactive Blazor Server circuit —
// SignOutAsync needs to write a Set-Cookie header on a real per-request
// HTTP response, which isn't possible once the connection has upgraded to
// SignalR. NavMenu.razor navigates here with forceLoad:true instead of
// calling SignOutAsync directly from an @onclick handler.
app.MapGet("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization();

// Multi-Agent System health & readiness endpoint
app.MapGet("/health", async (AgentHealthService healthService, CancellationToken ct) =>
{
    var status = await healthService.CheckHealthAsync(ct);
    return status.Status == "Healthy" ? Results.Ok(status) : Results.StatusCode(503);
}).AllowAnonymous();

// ── Pub/Sub Push Endpoint ────────────────────────────────────────────────────
// Cloud Pub/Sub push subscriptions POST here when agent events are delivered.
// Verify the bearer token matches PUBSUB_VERIFICATION_TOKEN to reject forgeries.
app.MapPost("/api/events/pubsub", async (HttpContext ctx, PushNotificationService pushNotifications, ILogger<Program> log) =>
{
    // Trim: Secret Manager values can pick up a trailing newline depending
    // on how they were written, which would otherwise make this exact-match
    // check always fail against a genuinely correct incoming token.
    var verificationToken = Environment.GetEnvironmentVariable("PUBSUB_VERIFICATION_TOKEN")?.Trim();

    // Previously: an unset token skipped the check entirely. That fail-open
    // mattered because the handler below acts on a UserId taken straight from
    // the request body — an unauthenticated caller could push arbitrary
    // notification text to any user's devices, which on a maternal-health app
    // is a ready-made phishing and panic vector. Deployed instances now
    // refuse rather than trust; local dev (no Pub/Sub) keeps working.
    if (string.IsNullOrEmpty(verificationToken))
    {
        if (isCloudRun)
        {
            log.LogError("Rejected Pub/Sub push: PUBSUB_VERIFICATION_TOKEN is not configured.");
            return Results.StatusCode(403);
        }
    }
    else
    {
        // Header first — a query-string secret ends up in Cloud Run request
        // logs, browser history and Referer headers. The query form is still
        // accepted so existing push subscriptions keep working until they're
        // reconfigured to send the header.
        var incoming = ctx.Request.Headers.Authorization.ToString();
        incoming = incoming.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? incoming["Bearer ".Length..].Trim()
            : ctx.Request.Query["token"].ToString();

        if (!TokenComparer.Matches(verificationToken, incoming))
            return Results.StatusCode(403);
    }

    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    try
    {
        using var doc = JsonDocument.Parse(body);
        var messageEl = doc.RootElement.GetProperty("message");
        var data = messageEl.GetProperty("data").GetString() ?? "";
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(data));
        var attrs = messageEl.TryGetProperty("attributes", out var a) ? a.ToString() : "{}";
        // The decoded payload used to be logged in full. Alert bodies name the
        // mother and describe her clinical condition, so that put health data
        // into Cloud Logging — a broader access boundary than the database,
        // and retained on its own schedule. Log shape and size, not content;
        // the full payload stays available in dev where it's useful.
        if (isCloudRun)
            log.LogInformation("Pub/Sub push received — attributes: {Attrs}, payload {Bytes} bytes", attrs, decoded.Length);
        else
            log.LogInformation("Pub/Sub push received — EventType: {Attrs} Payload: {Data}", attrs, decoded);

        // Every push subscription (mood/meal/birthplan/journal + alert-events)
        // lands here -- only alert-notification actually needs to do
        // anything; the rest keep the log-only behavior above.
        using var eventDoc = JsonDocument.Parse(decoded);
        var eventType = eventDoc.RootElement.TryGetProperty("EventType", out var et) ? et.GetString() : null;
        if (eventType == "alert-notification" && eventDoc.RootElement.TryGetProperty("Payload", out var payloadEl))
        {
            var payload = payloadEl.Deserialize<AlertNotificationPayload>();
            if (payload != null)
            {
                await pushNotifications.NotifyUserAsync(payload.UserId, payload.Title, payload.Body, payload.Scope, payload.InfantProfileId);
            }
        }
    }
    catch (Exception ex)
    {
        log.LogWarning("Pub/Sub push parse error: {Message}", ex.Message);
    }

    return Results.NoContent(); // 204 ACKs the message to Pub/Sub
}).AllowAnonymous();

// ── Push notification registration ───────────────────────────────────────────
// Called from janani-push.js after the browser grants notification
// permission and Firebase hands back a device token. Plain minimal API
// (not a Blazor circuit call) so it works via a simple fetch() from JS.
app.MapPost("/api/push/register", async (HttpContext ctx, AppDbContext db, CancellationToken ct) =>
{
    var userIdClaim = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
        return Results.Unauthorized();

    var body = await ctx.Request.ReadFromJsonAsync<PushRegisterRequest>(ct);
    if (string.IsNullOrWhiteSpace(body?.Token))
        return Results.BadRequest();

    var existing = await db.PushSubscriptions.FirstOrDefaultAsync(p => p.Token == body.Token, ct);
    if (existing == null)
    {
        db.PushSubscriptions.Add(new PushSubscription { UserId = userId, Token = body.Token });
    }
    else
    {
        existing.UserId = userId; // same device, possibly a different account now
    }
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
}).RequireAuthorization();

// ── Device vitals ingest ─────────────────────────────────────────────────────
// Elder-care edge devices (real or simulated — see edge-device-sim/) POST
// readings here, in capture order, once back online. AllowAnonymous since a
// device can't hold a login session; guarded instead by an optional
// shared-secret token, same pattern as PUBSUB_VERIFICATION_TOKEN above.
app.MapPost("/api/elders/{elderId:int}/vitals", async (
    int elderId, HttpContext ctx, AppDbContext db, VitalsIngestService vitalsIngest,
    IElderDeviceAuthenticator deviceAuth, CancellationToken ct) =>
{
    var presentedToken = ctx.Request.Query["token"].ToString();
    if (!await deviceAuth.AuthorizeAsync(elderId, presentedToken, ct))
        return Results.StatusCode(403);

    var elder = await db.ElderProfiles.FindAsync([elderId], ct);
    if (elder == null) return Results.NotFound();

    var body = await ctx.Request.ReadFromJsonAsync<DeviceVitalsRequest>(ct);
    if (body == null) return Results.BadRequest();

    // No Blazor circuit/ProfileCache here — look up the owning caregiver's
    // language directly, same field the Razor page reads via ProfileCache.
    var language = await db.UserProfiles.AsNoTracking()
        .Where(p => p.UserId == elder.UserId)
        .Select(p => p.Language)
        .FirstOrDefaultAsync(ct);

    var reading = new VitalsReading
    {
        ElderProfileId = elder.Id,
        // .ToUniversalTime(): System.Text.Json deserializes a DateTime string
        // carrying an explicit offset (e.g. Python's isoformat() "+00:00")
        // with Kind=Local, not Kind=Utc -- Npgsql then rejects it outright
        // for a "timestamp with time zone" column ("Cannot write DateTime
        // with Kind=Local..."). This doesn't shift the instant, just corrects
        // the Kind tag.
        RecordedAt = body.CapturedAt?.ToUniversalTime() ?? DateTime.UtcNow,
        SystolicBp = body.SystolicBp,
        DiastolicBp = body.DiastolicBp,
        HeartRate = body.HeartRate,
        TemperatureC = body.TemperatureC,
        OxygenSaturation = body.OxygenSaturation,
        Notes = body.Notes,
        Source = body.Source,
        DeviceReadingId = body.DeviceReadingId
    };

    var result = await vitalsIngest.IngestAsync(elder, reading, language, ct);
    return Results.Ok(new
    {
        duplicate = result.Duplicate,
        alertRaised = result.AlertRaised,
        severity = result.Severity?.ToString()
    });
}).AllowAnonymous();

// ── Device vitals ingest — pregnancy ─────────────────────────────────────────
// Unlike the elder endpoint above, there's no ElderProfileId-style indirection
// for pregnancy — UserId is the real login-capable account. A single global
// shared secret would let anyone holding it inject fake Critical alerts at
// any user by guessing an id, so this is per-user instead: the caller proves
// it belongs to a specific account by presenting THAT account's own
// UserProfile.DevicePairingToken (generated in Settings), not one shared
// DEVICE_INGEST_TOKEN. Still AllowAnonymous since a device has no login
// session — the pairing token is the auth.
app.MapPost("/api/pregnancy/vitals", async (
    HttpContext ctx, AppDbContext db, PregnancyVitalsIngestService vitalsIngest,
    IPregnancyDeviceAuthenticator deviceAuth, CancellationToken ct) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<PregnancyDeviceVitalsRequest>(ct);
    if (body == null) return Results.BadRequest();

    // Auth runs BEFORE the profile lookup, and a missing profile now returns
    // the same 403 as a bad token. The old order answered 404 for an unknown
    // id and 403 for a known one, which let an unauthenticated caller walk the
    // id space and learn exactly which accounts exist.
    if (!await deviceAuth.AuthorizeAsync(body.UserId, body.PairingToken, ct))
        return Results.StatusCode(403);

    var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == body.UserId, ct);
    if (profile == null) return Results.StatusCode(403);

    var reading = new PregnancyVitalsReading
    {
        UserId = body.UserId,
        // .ToUniversalTime(): System.Text.Json deserializes a DateTime string
        // carrying an explicit offset (e.g. Python's isoformat() "+00:00")
        // with Kind=Local, not Kind=Utc -- Npgsql then rejects it outright
        // for a "timestamp with time zone" column ("Cannot write DateTime
        // with Kind=Local..."). This doesn't shift the instant, just corrects
        // the Kind tag.
        RecordedAt = body.CapturedAt?.ToUniversalTime() ?? DateTime.UtcNow,
        SystolicBp = body.SystolicBp,
        DiastolicBp = body.DiastolicBp,
        HeartRate = body.HeartRate,
        TemperatureC = body.TemperatureC,
        OxygenSaturation = body.OxygenSaturation,
        Notes = body.Notes,
        Source = body.Source,
        DeviceReadingId = body.DeviceReadingId
    };

    var userName = string.IsNullOrWhiteSpace(profile.Name) ? "Mama" : profile.Name;
    var result = await vitalsIngest.IngestAsync(
        body.UserId, userName, profile.CurrentWeek, reading, profile.Language,
        profile.DoctorName, profile.DoctorEmail, ct);

    return Results.Ok(new
    {
        duplicate = result.Duplicate,
        alertRaised = result.AlertRaised,
        severity = result.Severity?.ToString()
    });
}).AllowAnonymous();

// ── Google Calendar Sync — OAuth endpoints ────────────────────────────────────
// Same reasoning as /account/logout above: the authorization-code redirect
// dance needs real HTTP redirects and a cookie, which a live Blazor Server
// circuit can't do — so this is plain minimal-API endpoints, linked to via
// <a href> full-page navigation from Settings.razor, not Blazor code-behind.
const string OAuthStateCookie = "Janani.OAuthState";

// Cloud Run terminates HTTPS at its edge and forwards to the container over
// plain HTTP, so ctx.Request.Scheme reports "http" even for a real https://
// request — Google rejects the OAuth redirect_uri unless it matches what's
// registered (https). Prefer the proxy's X-Forwarded-Proto header instead.
static string BuildCalendarRedirectUri(HttpContext ctx)
{
    var scheme = ctx.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? ctx.Request.Scheme;
    return $"{scheme}://{ctx.Request.Host}/calendar/callback/google";
}

app.MapGet("/calendar/connect/google", (HttpContext ctx, ICalendarSyncService calendarSync) =>
{
    var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    ctx.Response.Cookies.Append(OAuthStateCookie, state, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Expires = DateTimeOffset.UtcNow.AddMinutes(10)
    });

    var redirectUri = BuildCalendarRedirectUri(ctx);
    try
    {
        return Results.Redirect(calendarSync.BuildAuthorizationUrl(state, redirectUri));
    }
    catch (InvalidOperationException)
    {
        return Results.Redirect("/settings?calendar=error");
    }
}).RequireAuthorization();

app.MapGet("/calendar/callback/google", async (HttpContext ctx, ICalendarSyncService calendarSync, CancellationToken ct) =>
{
    var expectedState = ctx.Request.Cookies[OAuthStateCookie];
    ctx.Response.Cookies.Delete(OAuthStateCookie);

    var returnedState = ctx.Request.Query["state"].ToString();
    var code = ctx.Request.Query["code"].ToString();
    if (string.IsNullOrEmpty(expectedState) || returnedState != expectedState || string.IsNullOrEmpty(code))
        return Results.Redirect("/settings?calendar=error");

    var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
    var redirectUri = BuildCalendarRedirectUri(ctx);
    try
    {
        await calendarSync.CompleteConnectionAsync(userId, code, redirectUri, ct);
        return Results.Redirect("/settings?calendar=connected");
    }
    catch (Exception)
    {
        return Results.Redirect("/settings?calendar=error");
    }
}).RequireAuthorization();

app.MapGet("/calendar/disconnect/google", async (HttpContext ctx, ICalendarSyncService calendarSync, CancellationToken ct) =>
{
    var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
    await calendarSync.DisconnectAsync(userId, ct);
    return Results.Redirect("/settings?calendar=disconnected");
}).RequireAuthorization();

app.Run();

record PushRegisterRequest(string Token);

record DeviceVitalsRequest(
    int? SystolicBp, int? DiastolicBp, int? HeartRate, decimal? TemperatureC,
    int? OxygenSaturation, string? Notes, string? Source,
    DateTime? CapturedAt, string? DeviceReadingId);

record PregnancyDeviceVitalsRequest(
    int UserId, string PairingToken,
    int? SystolicBp, int? DiastolicBp, int? HeartRate, decimal? TemperatureC,
    int? OxygenSaturation, string? Notes, string? Source,
    DateTime? CapturedAt, string? DeviceReadingId);