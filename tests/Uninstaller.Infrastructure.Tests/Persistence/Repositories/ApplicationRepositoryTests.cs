using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Infrastructure.Persistence;
using Uninstaller.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Uninstaller.Infrastructure.Tests.Persistence.Repositories;

public class ApplicationRepositoryTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly ApplicationRepository _repository;

    public ApplicationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new AppDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        var deduplicator = new ApplicationDeduplicator();
        _repository = new ApplicationRepository(_dbContext, deduplicator, new NullLogger<ApplicationRepository>());
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task SyncAsync_EmptyDatabase_InsertsNewApp()
    {
        var apps = new[] { new Application { Id = Guid.NewGuid(), Name = "App 1", Publisher = "Pub 1" } };
        
        await _repository.SyncAsync(apps, CancellationToken.None);

        var saved = await _dbContext.Applications.ToListAsync();
        Assert.Single(saved);
        Assert.Equal("App 1", saved[0].Name);
        Assert.True(saved[0].IsPresent);
    }

    [Fact]
    public async Task SyncAsync_RepeatedDiscovery_DoesNotCreateDuplicates()
    {
        var app1 = new Application { Id = Guid.NewGuid(), Name = "App 1", Publisher = "Pub 1", UninstallCommand = "cmd1" };
        
        await _repository.SyncAsync(new[] { app1 }, CancellationToken.None);
        
        // Discover exactly the same app but new instance
        var app2 = new Application { Id = Guid.NewGuid(), Name = "App 1", Publisher = "Pub 1", UninstallCommand = "cmd1" };
        
        await _repository.SyncAsync(new[] { app2 }, CancellationToken.None);

        var saved = await _dbContext.Applications.ToListAsync();
        Assert.Single(saved); // Should merge!
        Assert.Equal("App 1", saved[0].Name);
    }

    [Fact]
    public async Task SyncAsync_AppNoLongerDiscovered_SetsIsPresentFalse()
    {
        var app1 = new Application { Id = Guid.NewGuid(), Name = "App 1", UninstallCommand = "cmd1" };
        
        await _repository.SyncAsync(new[] { app1 }, CancellationToken.None);
        
        // Sync with empty list (app uninstalled/disappeared)
        await _repository.SyncAsync(Array.Empty<Application>(), CancellationToken.None);

        var saved = await _dbContext.Applications.ToListAsync();
        Assert.Single(saved);
        Assert.False(saved[0].IsPresent);
    }

    [Fact]
    public async Task SyncAsync_MetadataUpdate_MergesNewData()
    {
        var app1 = new Application { Id = Guid.NewGuid(), Name = "App 1", Publisher = "Old Pub", UninstallCommand = "cmd1" };
        await _repository.SyncAsync(new[] { app1 }, CancellationToken.None);
        
        // Later discovery adds InstallLocation
        var app2 = new Application { Id = Guid.NewGuid(), Name = "App 1", Publisher = "Old Pub", UninstallCommand = "cmd1", InstallLocation = "C:\\Path" };
        await _repository.SyncAsync(new[] { app2 }, CancellationToken.None);

        var saved = await _dbContext.Applications.ToListAsync();
        Assert.Single(saved);
        Assert.Equal("C:\\Path", saved[0].InstallLocation);
        Assert.True(saved[0].IsPresent);
    }

    [Fact]
    public async Task SyncAsync_MultipleDiscoverySources_RepresentingOneApplication_SquashesThem()
    {
        // 3 apps in one discovery batch that are actually the same app
        var app32 = new Application { Id = Guid.NewGuid(), Name = "App", UninstallCommand = "cmd", RegistrySource = "HKLM32" };
        var app64 = new Application { Id = Guid.NewGuid(), Name = "App", UninstallCommand = "cmd", RegistrySource = "HKLM64" };
        
        await _repository.SyncAsync(new[] { app32, app64 }, CancellationToken.None);

        var saved = await _dbContext.Applications.ToListAsync();
        Assert.Single(saved);
        Assert.Contains("HKLM32", saved[0].RegistrySource);
        Assert.Contains("HKLM64", saved[0].RegistrySource);
    }

    [Fact]
    public async Task SyncAsync_TransactionFailure_ThrowsException()
    {
        var app1 = new Application { Id = Guid.NewGuid(), Name = "App 1" };
        
        // Force a failure by dropping the table so SaveChanges throws a DbUpdateException
        await _dbContext.Database.ExecuteSqlRawAsync("DROP TABLE Applications");

        await Assert.ThrowsAnyAsync<Exception>(() => 
            _repository.SyncAsync(new[] { app1 }, CancellationToken.None));
    }
}
