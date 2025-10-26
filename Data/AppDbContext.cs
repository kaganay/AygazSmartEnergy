using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using AygazSmartEnergy.Models;

namespace AygazSmartEnergy.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<SensorData> SensorDatas { get; set; }
        public DbSet<Device> Devices { get; set; }
        public DbSet<EnergyConsumption> EnergyConsumptions { get; set; }
        public DbSet<Alert> Alerts { get; set; }
        public DbSet<AlertNotification> AlertNotifications { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Device configurations
            modelBuilder.Entity<Device>(entity =>
            {
                entity.HasOne(d => d.User)
                    .WithMany(u => u.Devices)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.InstalledAt).HasDefaultValueSql("GETDATE()");
            });

            // EnergyConsumption configurations
            modelBuilder.Entity<EnergyConsumption>(entity =>
            {
                entity.HasOne(e => e.Device)
                    .WithMany(d => d.EnergyConsumptions)
                    .HasForeignKey(e => e.DeviceId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.RecordedAt).HasDefaultValueSql("GETDATE()");
            });

            // SensorData configurations
            modelBuilder.Entity<SensorData>(entity =>
            {
                entity.HasOne(s => s.Device)
                    .WithMany(d => d.SensorDatas)
                    .HasForeignKey(s => s.DeviceId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.Property(e => e.RecordedAt).HasDefaultValueSql("GETDATE()");
            });

            // Alert configurations
            modelBuilder.Entity<Alert>(entity =>
            {
                entity.HasOne(a => a.User)
                    .WithMany(u => u.Alerts)
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Device)
    .WithMany()
    .HasForeignKey(a => a.DeviceId)
    .OnDelete(DeleteBehavior.NoAction);


                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
            });

            // AlertNotification configurations
            modelBuilder.Entity<AlertNotification>(entity =>
            {
                entity.HasOne(an => an.Alert)
                    .WithMany(a => a.AlertNotifications)
                    .HasForeignKey(an => an.AlertId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.SentAt).HasDefaultValueSql("GETDATE()");
            });
        }
    }
}
