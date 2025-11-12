using Microsoft.EntityFrameworkCore;
using Runnatics.Data.EF.Config;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.EventOrganizers;

namespace Runnatics.Data.EF
{
    public class RaceSyncDbContext(DbContextOptions<RaceSyncDbContext> options) : DbContext(options)
    {    
        #region DbSets for your entities
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<Event> Events { get; set; }
        public DbSet<EventSettings> EventSettings { get; set; }
        public DbSet<LeaderboardSettings> LeaderboardSettings { get; set; }
        public DbSet<Race> Races { get; set; }
        public DbSet<RaceSettings> RaceSettings { get; set; }
        public DbSet<Participant> Participants { get; set; }
        public DbSet<Checkpoint> Checkpoints { get; set; }
        public DbSet<Chip> Chips { get; set; }
        public DbSet<ChipAssignment> ChipAssignments { get; set; }
        public DbSet<ReaderDevice> ReaderDevices { get; set; }
        public DbSet<ReaderAssignment> ReaderAssignments { get; set; }
        public DbSet<ReadRaw> ReadRaws { get; set; }
        public DbSet<ReadNormalized> ReadNormalizeds { get; set; }
        public DbSet<SplitTime> SplitTimes { get; set; }
        public DbSet<Results> Results { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<EventOrganizer> EventOrganizers { get; set; }
        // public DbSet<ImportBatch> ImportBatches { get; set; }
        // public DbSet<AuditLog> AuditLogs { get; set; }

        #endregion
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
            modelBuilder.ApplyConfiguration(new UserConfiguration());
            modelBuilder.ApplyConfiguration(new UserSessionConfiguration());
            modelBuilder.ApplyConfiguration(new PasswordResetTokenConfiguration());
            modelBuilder.ApplyConfiguration(new EventConfiguration());
            modelBuilder.ApplyConfiguration(new EventSettingsConfiguration());
            modelBuilder.ApplyConfiguration(new LeaderboardSettingsConfiguration());
            modelBuilder.ApplyConfiguration(new RaceConfiguration());
            modelBuilder.ApplyConfiguration(new RaceSettingsConfiguration());
            modelBuilder.ApplyConfiguration(new ParticipantConfiguration());
            modelBuilder.ApplyConfiguration(new CheckpointConfiguration());
            modelBuilder.ApplyConfiguration(new ChipConfiguration());
            modelBuilder.ApplyConfiguration(new ChipAssignmentConfiguration());
            modelBuilder.ApplyConfiguration(new ReaderDeviceConfiguration());
            modelBuilder.ApplyConfiguration(new ReaderAssignmentConfiguration());
            modelBuilder.ApplyConfiguration(new ReadRawConfiguration());
            modelBuilder.ApplyConfiguration(new ReadNormalizedConfiguration());
            modelBuilder.ApplyConfiguration(new SplitTimeConfiguration());
            modelBuilder.ApplyConfiguration(new ResultConfiguration());
            modelBuilder.ApplyConfiguration(new NotificationConfiguration());
            modelBuilder.ApplyConfiguration(new EventOrganizerConfiguration());
            //modelBuilder.DefaultFilters();

            // Configure entity relationships and constraints here
        }

        public void CreateDatabase()
        {
            Database.EnsureDeleted();
            Database.EnsureCreated();
        }
    }
}