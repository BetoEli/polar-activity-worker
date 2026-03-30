using Microsoft.EntityFrameworkCore;
using Paw.Core.Domain;

namespace Paw.Infrastructure;

public class PawDbContext : DbContext
{
    private readonly DbContextOptions<PawDbContext> _options;

    public PawDbContext(DbContextOptions<PawDbContext> options) : base(options) { _options = options; }

    public DbContextOptions<PawDbContext> ContextOptions => _options;

    // QEPTest database tables
    public DbSet<PolarLink> PolarLinks => Set<PolarLink>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<PolarTransaction> PolarTransactions => Set<PolarTransaction>();
    public DbSet<QepActivity> Activities => Set<QepActivity>();
    public DbSet<HeartRateZones> HeartRateZones => Set<HeartRateZones>();
    public DbSet<QepActivityType> ActivityTypes => Set<QepActivityType>();
    
    // Note: QEPTest uses PersonID (string) instead of UserID (Guid)
    // Activities are linked to students via PersonID from PolarLinks

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ============================================================================
        // QEP PolarLinks Table Configuration
        // Maps to existing QEP database table for backward compatibility
        // Table contains: PolarID, Username, PersonID, Email, DeviceType, TargetZone, AccessToken
        // PolarID is set to Polar's X-User-ID from OAuth token (not auto-increment)
        // ============================================================================
        
        modelBuilder.Entity<PolarLink>()
            .ToTable("PolarLinks", "dbo")
            .HasKey(p => p.PolarID);
        
        // PolarID is NOT auto-generated - it's set from Polar's X-User-ID.
        // BIGINT because Polar user IDs can exceed INT range (2^31-1).
        modelBuilder.Entity<PolarLink>()
            .Property(p => p.PolarID)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        // AccessToken column to store OAuth access token
        // Allow NULL for existing rows that don't have tokens yet
        modelBuilder.Entity<PolarLink>()
            .Property(p => p.AccessToken)
            .HasMaxLength(500)
            .IsRequired(false); // Changed to allow NULL for migration

        // Create indexes for common queries
        modelBuilder.Entity<PolarLink>()
            .HasIndex(p => p.Email)
            .HasDatabaseName("IX_PolarLinks_Email");
        
        modelBuilder.Entity<PolarLink>()
            .HasIndex(p => p.PersonID)
            .HasDatabaseName("IX_PolarLinks_PersonID");
        
        // Set column types for SQL Server
        modelBuilder.Entity<PolarLink>()
            .Property(p => p.Username)
            .HasMaxLength(150)
            .IsRequired();
        
        modelBuilder.Entity<PolarLink>()
            .Property(p => p.PersonID)
            .HasMaxLength(10)
            .IsRequired();
        
        modelBuilder.Entity<PolarLink>()
            .Property(p => p.Email)
            .HasMaxLength(255)
            .IsRequired();
        
        modelBuilder.Entity<PolarLink>()
            .Property(p => p.DeviceType)
            .HasMaxLength(10);
        
        modelBuilder.Entity<PolarLink>()
            .Property(p => p.TargetZone)
            .HasMaxLength(10);

        // ============================================================================
        // WebhookEvents Table Configuration
        // Stores incoming webhook events from Polar for processing
        // ============================================================================
        
        modelBuilder.Entity<WebhookEvent>()
            .ToTable("WebhookEvents", "dbo")
            .HasKey(w => w.Id);

        modelBuilder.Entity<WebhookEvent>()
            .Property(w => w.Id)
            .ValueGeneratedOnAdd();

        // Index for efficient queries by Provider, ExternalUserId, and EntityID
        modelBuilder.Entity<WebhookEvent>()
            .HasIndex(w => new { w.Provider, w.ExternalUserId, w.EntityID })
            .HasDatabaseName("IX_WebhookEvents_Provider_ExternalUserId_EntityID");

        // Index for finding pending events
        modelBuilder.Entity<WebhookEvent>()
            .HasIndex(w => new { w.Status, w.ReceivedAtUtc })
            .HasDatabaseName("IX_WebhookEvents_Status_ReceivedAtUtc");

        // Provider as int
        modelBuilder.Entity<WebhookEvent>()
            .Property(w => w.Provider)
            .HasConversion<int>();

        // ReceivedAtUtc with default
        modelBuilder.Entity<WebhookEvent>()
            .Property(w => w.ReceivedAtUtc)
            .HasDefaultValueSql("GETUTCDATE()");

        // String column configurations
        modelBuilder.Entity<WebhookEvent>()
            .Property(w => w.EventType)
            .HasMaxLength(50)
            .IsRequired();

        modelBuilder.Entity<WebhookEvent>()
            .Property(w => w.EntityID)
            .HasMaxLength(255)
            .IsRequired();

        modelBuilder.Entity<WebhookEvent>()
            .Property(w => w.Status)
            .HasMaxLength(50)
            .IsRequired()
            .HasDefaultValue("Pending");

        modelBuilder.Entity<WebhookEvent>()
            .Property(w => w.ResourceUrl)
            .HasMaxLength(1000);

        modelBuilder.Entity<WebhookEvent>()
            .Property(w => w.RawPayload)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        modelBuilder.Entity<WebhookEvent>()
            .Property(w => w.ErrorMessage)
            .HasMaxLength(2000);
        
        // ============================================================================
        // PolarTransactions Table Configuration
        // Stores raw exercise data fetched from Polar for processing by QEP
        // Database uses INT for ID columns, not BIGINT
        // ============================================================================
        
        modelBuilder.Entity<PolarTransaction>()
            .ToTable("PolarTransactions", "dbo")
            .HasKey(pt => pt.PolarTransactionID);

        modelBuilder.Entity<PolarTransaction>()
            .Property(pt => pt.PolarTransactionID)
            .ValueGeneratedOnAdd()
            .HasColumnType("int");

        // BIGINT to match PolarLinks.PolarID — widened from INT in migration WidenPolarTransactionPolarIdToBigint.
        modelBuilder.Entity<PolarTransaction>()
            .Property(pt => pt.PolarID)
            .HasColumnType("bigint");

        // Index for quick lookup by PolarID
        modelBuilder.Entity<PolarTransaction>()
            .HasIndex(pt => pt.PolarID)
            .HasDatabaseName("IX_PolarTransactions_PolarID");

        // Index for finding by exercise ID (Location)
        modelBuilder.Entity<PolarTransaction>()
            .HasIndex(pt => pt.Location)
            .HasDatabaseName("IX_PolarTransactions_Location");

        // Composite index for finding unprocessed items by user
        modelBuilder.Entity<PolarTransaction>()
            .HasIndex(pt => new { pt.PolarID, pt.IsProcessed })
            .HasDatabaseName("IX_PolarTransactions_PolarID_IsProcessed");

        // String column configurations
        modelBuilder.Entity<PolarTransaction>()
            .Property(pt => pt.Location)
            .HasMaxLength(255)
            .IsRequired();

        modelBuilder.Entity<PolarTransaction>()
            .Property(pt => pt.Response)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        // Boolean columns - we set these explicitly in code, so don't generate from DB
        modelBuilder.Entity<PolarTransaction>()
            .Property(pt => pt.IsCommitted)
            .HasColumnType("bit")
            .HasDefaultValue(false)
            .ValueGeneratedNever(); // We set this in code, don't read from OUTPUT

        modelBuilder.Entity<PolarTransaction>()
            .Property(pt => pt.IsProcessed)
            .HasColumnType("bit")
            .HasDefaultValue(false)
            .ValueGeneratedNever(); // We set this in code, don't read from OUTPUT

        modelBuilder.Entity<PolarTransaction>()
            .Property(pt => pt.Attempt)
            .HasColumnType("int")
            .HasDefaultValue(0)
            .ValueGeneratedNever(); // We set this in code, don't read from OUTPUT
        
        // ============================================================================
        // QEP Activity Table Configuration
        // Maps to existing QEPTest.dbo.Activity table
        // IMPORTANT: ActivityID is BIGINT (confirmed from actual data: 1009382, 326091)
        //            Duration is in SECONDS (not hours): 334.7625 seconds
        // ============================================================================
        
        modelBuilder.Entity<QepActivity>()
            .ToTable("Activity", "dbo")
            .HasKey(a => a.ActivityID);

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.ActivityID)
            .ValueGeneratedOnAdd()
            .HasColumnType("bigint"); 

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.EntityID)
            .HasColumnName("EntityID")
            .HasMaxLength(50);

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.ActivityTypeID)
            .HasColumnType("int");

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.UserID)
            .HasMaxLength(10)
            .IsRequired();

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.Username)
            .HasMaxLength(150)
            .IsRequired();

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.Measurement)
            .HasColumnType("float");

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.Minutes)
            .HasColumnType("int");

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.Duration)
            .HasColumnType("float");

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.Distance)
            .HasColumnType("float");

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.AerobicPoints)
            .HasColumnType("int");

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.DateDone)
            .HasColumnType("datetime");

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.DateEntered)
            .HasColumnType("datetime");

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.DeviceType)
            .HasMaxLength(10);

        modelBuilder.Entity<QepActivity>()
            .Property(a => a.TargetZone)
            .HasMaxLength(10);

        // Relationship with HeartRateZones
        modelBuilder.Entity<QepActivity>()
            .HasMany(a => a.HeartRateZones)
            .WithOne(h => h.Activity)
            .HasForeignKey(h => h.ActivityID)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for quick lookup by UserID
        modelBuilder.Entity<QepActivity>()
            .HasIndex(a => a.UserID)
            .HasDatabaseName("IX_Activity_UserID");

        // Index for date range queries
        modelBuilder.Entity<QepActivity>()
            .HasIndex(a => a.DateDone)
            .HasDatabaseName("IX_Activity_DateDone");

        // ============================================================================
        // QEP HeartRateZones Table Configuration
        // Maps to existing QEPTest.dbo.HeartRateZones table
        // ============================================================================
        
        modelBuilder.Entity<HeartRateZones>()
            .ToTable("HeartRateZones", "dbo")
            .HasKey(h => h.HeartRateZoneID);

        modelBuilder.Entity<HeartRateZones>()
            .Property(h => h.HeartRateZoneID)
            .ValueGeneratedOnAdd()
            .HasColumnType("bigint");

        modelBuilder.Entity<HeartRateZones>()
            .Property(h => h.EntityID)
            .HasColumnName("EntityID")
            .HasMaxLength(50);

        modelBuilder.Entity<HeartRateZones>()
            .Property(h => h.ActivityID)
            .HasColumnType("bigint");

        modelBuilder.Entity<HeartRateZones>()
            .Property(h => h.Zone)
            .HasColumnType("int");

        modelBuilder.Entity<HeartRateZones>()
            .Property(h => h.Lower)
            .HasColumnType("int");

        modelBuilder.Entity<HeartRateZones>()
            .Property(h => h.Upper)
            .HasColumnType("int");

        modelBuilder.Entity<HeartRateZones>()
            .Property(h => h.Duration)
            .HasColumnType("float"); 

        // Index for finding zones by EntityID
        modelBuilder.Entity<HeartRateZones>()
            .HasIndex(h => h.EntityID)
            .HasDatabaseName("IX_HeartRateZones_EntityID");
        
        // ============================================================================
        // QEP ActivityType Table Configuration
        // Maps to existing QEPTest.dbo.ActivityType table (read-only lookup)
        // ============================================================================
        
        modelBuilder.Entity<QepActivityType>()
            .ToTable("ActivityType", "dbo")
            .HasKey(at => at.ActivityTypeID);

        modelBuilder.Entity<QepActivityType>()
            .Property(at => at.ActivityTypeID)
            .ValueGeneratedNever(); // Existing table, don't auto-generate

        modelBuilder.Entity<QepActivityType>()
            .Property(at => at.Description)
            .HasMaxLength(255)
            .IsRequired();

        modelBuilder.Entity<QepActivityType>()
            .Property(at => at.Units)
            .HasMaxLength(10);

        modelBuilder.Entity<QepActivityType>()
            .Property(at => at.Active)
            .HasColumnType("bit")
            .HasDefaultValue(true);

        modelBuilder.Entity<QepActivityType>()
            .Property(at => at.AutomatedEntry)
            .HasColumnType("bit")
            .HasDefaultValue(false);

        modelBuilder.Entity<QepActivityType>()
            .Property(at => at.InformationID)
            .HasColumnType("int");

        modelBuilder.Entity<QepActivityType>()
            .Property(at => at.InformationURL)
            .HasMaxLength(500);

        modelBuilder.Entity<QepActivityType>()
            .Property(at => at.InformationIsID)
            .HasColumnType("bit")
            .HasDefaultValue(false);

        modelBuilder.Entity<QepActivityType>()
            .Property(at => at.InformationIsURL)
            .HasColumnType("bit")
            .HasDefaultValue(false);

        // Index for quick lookup by Description
        modelBuilder.Entity<QepActivityType>()
            .HasIndex(at => at.Description)
            .HasDatabaseName("IX_ActivityType_Description");
    }
}