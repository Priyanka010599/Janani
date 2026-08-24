// Data/AppDbContext.cs
// SQLite locally; swap connection string to Cloud SQL (PostgreSQL) for GCP deployment.
// EF Core abstracts the provider — no application code changes needed on swap.

using Janani.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Janani.Data;

// IDataProtectionKeyContext: without this, ASP.NET Core's Data Protection
// key ring is ephemeral per container instance — every Cloud Run revision
// (or even a second replica) generates its own keys, silently making every
// previously-encrypted value (antiforgery cookies, CalendarSyncService's and
// EmailNotificationService's stored OAuth tokens) undecryptable. Persisting
// keys here means they survive redeploys and are shared across instances.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<MoodCheckIn> MoodCheckIns => Set<MoodCheckIn>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<AppointmentEntry> Appointments => Set<AppointmentEntry>();
    public DbSet<WaterIntakeEntry> WaterIntake => Set<WaterIntakeEntry>();
    public DbSet<KickSession> KickSessions => Set<KickSession>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<MedicineDose> MedicineDoses => Set<MedicineDose>();
    public DbSet<JournalPhoto> JournalPhotos => Set<JournalPhoto>();
    public DbSet<ConnectedCalendarAccount> ConnectedCalendarAccounts => Set<ConnectedCalendarAccount>();
    public DbSet<ElderProfile> ElderProfiles => Set<ElderProfile>();
    public DbSet<VitalsReading> VitalsReadings => Set<VitalsReading>();
    public DbSet<ElderCheckIn> ElderCheckIns => Set<ElderCheckIn>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<InfantProfile> InfantProfiles => Set<InfantProfile>();
    public DbSet<FeedingLog> FeedingLogs => Set<FeedingLog>();
    public DbSet<SleepLog> SleepLogs => Set<SleepLog>();
    public DbSet<GrowthEntry> GrowthEntries => Set<GrowthEntry>();
    public DbSet<VaccinationRecord> VaccinationRecords => Set<VaccinationRecord>();
    public DbSet<InfantAlert> InfantAlerts => Set<InfantAlert>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<ConnectedEmailAccount> ConnectedEmailAccounts => Set<ConnectedEmailAccount>();
    public DbSet<SharedCareAccess> SharedCareAccess => Set<SharedCareAccess>();
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // No UserProfile seed here anymore — profiles are created at
        // registration time (see Register.razor). The pre-auth seeded row
        // (Id=1, "Mama") already exists on disk for the developer's real
        // janani.db; Program.cs's startup migration + the first-registrant
        // backfill in Register.razor take care of claiming it. A brand-new
        // install now starts with zero UserProfiles until someone registers,
        // instead of a phantom row that would sit unclaimed forever.

        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();

        // Every history/list page sorts or filters on these — cheap now given
        // small data volumes, but avoids a full scan as entries accumulate.
        // UserId indexes matter more once queries always filter by owner first.
        modelBuilder.Entity<UserProfile>().HasIndex(p => p.UserId);
        modelBuilder.Entity<WaterIntakeEntry>().HasIndex(w => w.Date);
        modelBuilder.Entity<WaterIntakeEntry>().HasIndex(w => w.UserId);
        modelBuilder.Entity<KickSession>().HasIndex(k => k.Date);
        modelBuilder.Entity<KickSession>().HasIndex(k => k.UserId);
        modelBuilder.Entity<AppointmentEntry>().HasIndex(a => a.AppointmentDate);
        modelBuilder.Entity<AppointmentEntry>().HasIndex(a => a.UserId);
        modelBuilder.Entity<JournalEntry>().HasIndex(j => j.CreatedAt);
        modelBuilder.Entity<JournalEntry>().HasIndex(j => j.UserId);
        modelBuilder.Entity<MoodCheckIn>().HasIndex(m => m.CheckedAt);
        modelBuilder.Entity<MoodCheckIn>().HasIndex(m => m.UserId);
        modelBuilder.Entity<Medicine>().HasIndex(m => m.UserId);
        modelBuilder.Entity<EmergencyContact>().HasIndex(c => c.UserId);
        modelBuilder.Entity<MedicineDose>().HasIndex(d => new { d.MedicineId, d.Date });
        modelBuilder.Entity<JournalPhoto>().HasIndex(p => p.JournalEntryId);
        modelBuilder.Entity<ConnectedCalendarAccount>().HasIndex(c => c.UserId);
        modelBuilder.Entity<ConnectedCalendarAccount>().HasIndex(c => new { c.UserId, c.Provider }).IsUnique();
        modelBuilder.Entity<ElderProfile>().HasIndex(e => e.UserId);
        modelBuilder.Entity<VitalsReading>().HasIndex(v => v.ElderProfileId);
        modelBuilder.Entity<VitalsReading>().HasIndex(v => v.RecordedAt);
        modelBuilder.Entity<ElderCheckIn>().HasIndex(c => c.ElderProfileId);
        modelBuilder.Entity<ElderCheckIn>().HasIndex(c => c.CheckedAt);
        modelBuilder.Entity<Alert>().HasIndex(a => a.ElderProfileId);
        modelBuilder.Entity<Alert>().HasIndex(a => a.TriggeredAt);
        modelBuilder.Entity<InfantProfile>().HasIndex(i => i.UserId);
        modelBuilder.Entity<FeedingLog>().HasIndex(f => f.InfantProfileId);
        modelBuilder.Entity<FeedingLog>().HasIndex(f => f.FedAt);
        modelBuilder.Entity<SleepLog>().HasIndex(s => s.InfantProfileId);
        modelBuilder.Entity<GrowthEntry>().HasIndex(g => g.InfantProfileId);
        modelBuilder.Entity<GrowthEntry>().HasIndex(g => g.RecordedAt);
        modelBuilder.Entity<VaccinationRecord>().HasIndex(v => v.InfantProfileId);
        modelBuilder.Entity<InfantAlert>().HasIndex(a => a.InfantProfileId);
        modelBuilder.Entity<InfantAlert>().HasIndex(a => a.TriggeredAt);
        modelBuilder.Entity<PushSubscription>().HasIndex(p => p.UserId);
        modelBuilder.Entity<PushSubscription>().HasIndex(p => p.Token).IsUnique();
        modelBuilder.Entity<ConnectedEmailAccount>().HasIndex(c => c.UserId);
        modelBuilder.Entity<ConnectedEmailAccount>().HasIndex(c => new { c.UserId, c.Provider }).IsUnique();
        modelBuilder.Entity<SharedCareAccess>().HasIndex(s => s.UserId);
        modelBuilder.Entity<SharedCareAccess>().HasIndex(s => s.ElderProfileId);
        modelBuilder.Entity<SharedCareAccess>().HasIndex(s => s.InfantProfileId);
    }
}