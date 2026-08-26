using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Infrastructure.Persistence;
using Xunit;

namespace Uninstaller.Infrastructure.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task CanSaveAndLoadEntityGraph()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new AppDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();

        var appId = Guid.NewGuid();
        var app = new Application { Id = appId, Name = "TestApp", CreatedAt = DateTime.UtcNow };
        
        var session = new UninstallSession { Id = Guid.NewGuid(), ApplicationId = appId, Status = UninstallSessionStatus.Created, StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        
        var artifact = new Artifact { Id = Guid.NewGuid(), SessionId = session.Id, Path = @"C:\Test", Type = ArtifactType.Directory, Classification = ArtifactClassification.ApplicationOwned, DiscoveredAt = DateTime.UtcNow };

        context.Applications.Add(app);
        context.UninstallSessions.Add(session);
        context.Artifacts.Add(artifact);
        
        await context.SaveChangesAsync();

        var loadedApp = await context.Applications.FirstOrDefaultAsync(a => a.Id == appId);
        Assert.NotNull(loadedApp);
        Assert.Equal("TestApp", loadedApp.Name);

        var loadedSession = await context.UninstallSessions.FirstOrDefaultAsync(s => s.ApplicationId == appId);
        Assert.NotNull(loadedSession);

        var loadedArtifact = await context.Artifacts.FirstOrDefaultAsync(a => a.SessionId == loadedSession.Id);
        Assert.NotNull(loadedArtifact);
        Assert.Equal(@"C:\Test", loadedArtifact.Path);
    }

    [Fact]
    public async Task CanSaveAndLoadCleanupPlan()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new AppDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();

        var appId = Guid.NewGuid();
        var app = new Application { Id = appId, Name = "TestApp", CreatedAt = DateTime.UtcNow };
        var session = new UninstallSession { Id = Guid.NewGuid(), ApplicationId = appId, Status = UninstallSessionStatus.Created, StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        
        var plan = new CleanupPlan
        {
            Id = Guid.NewGuid(),
            ApplicationId = appId,
            UninstallSessionId = session.Id,
            Status = CleanupPlanStatus.Generated,
            Summary = new CleanupPlanSummary { TotalArtifacts = 1, RecommendedItems = 1 }
        };

        var item = new CleanupPlanItem
        {
            Id = Guid.NewGuid(),
            CleanupPlanId = plan.Id,
            Path = @"C:\App",
            Classification = ArtifactClassification.ApplicationOwned,
            RiskLevel = RiskLevel.Low,
            Recommended = true,
            Reasons = new System.Collections.Generic.List<string> { "Exact Match" }
        };

        plan.Items.Add(item);

        context.Applications.Add(app);
        context.UninstallSessions.Add(session);
        context.CleanupPlans.Add(plan);
        await context.SaveChangesAsync();

        var loadedPlan = await context.CleanupPlans
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == plan.Id);

        Assert.NotNull(loadedPlan);
        Assert.Equal(CleanupPlanStatus.Generated, loadedPlan.Status);
        Assert.Equal(1, loadedPlan.Summary.TotalArtifacts);
        
        Assert.Single(loadedPlan.Items);
        Assert.Equal(@"C:\App", loadedPlan.Items[0].Path);
        Assert.Contains("Exact Match", loadedPlan.Items[0].Reasons);
    }
}
