using Microsoft.EntityFrameworkCore;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<UninstallSession> UninstallSessions => Set<UninstallSession>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<Backup> Backups => Set<Backup>();
    public DbSet<LogEntry> Logs => Set<LogEntry>();
    public DbSet<CleanupPlan> CleanupPlans => Set<CleanupPlan>();
    public DbSet<CleanupPlanItem> CleanupPlanItems => Set<CleanupPlanItem>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Application>()
            .HasIndex(a => a.Name);
            
        modelBuilder.Entity<Application>()
            .HasIndex(a => a.RegistryKeyName);

        modelBuilder.Entity<UninstallSession>()
            .HasOne<Application>()
            .WithMany()
            .HasForeignKey(s => s.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Artifact>()
            .HasOne<UninstallSession>()
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Operation>()
            .HasOne<UninstallSession>()
            .WithMany()
            .HasForeignKey(o => o.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Operation>()
            .HasOne<Artifact>()
            .WithMany()
            .HasForeignKey(o => o.ArtifactId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Backup>()
            .HasOne<UninstallSession>()
            .WithMany()
            .HasForeignKey(b => b.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LogEntry>()
            .HasOne<UninstallSession>()
            .WithMany()
            .HasForeignKey(l => l.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CleanupPlan>()
            .HasOne<UninstallSession>()
            .WithMany()
            .HasForeignKey(p => p.UninstallSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CleanupPlan>()
            .OwnsOne(p => p.Summary, s =>
            {
                s.ToJson();
            });

        modelBuilder.Entity<CleanupPlanItem>()
            .HasOne<CleanupPlan>()
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.CleanupPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CleanupPlanItem>()
            .OwnsMany(i => i.Evidence, e =>
            {
                e.ToJson();
            });
    }
}
