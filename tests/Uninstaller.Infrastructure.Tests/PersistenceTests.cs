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
        
        var artifact = new Artifact { Id = Guid.NewGuid(), SessionId = session.Id, Path = @"C:\Test", Type = ArtifactType.Directory, Classification = ArtifactClassification.SafeCandidate, DiscoveredAt = DateTime.UtcNow };

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
}
