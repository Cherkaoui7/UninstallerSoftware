using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class DiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverApplicationsAsync_HappyPath_ReturnsExpectedStats()
    {
        // Arrange
        var fakeRegistry = new FakeRegistryService(new List<RawRegistryApplication>
        {
            new RawRegistryApplication { DisplayName = "Valid App" },
            new RawRegistryApplication { DisplayName = null } // Will be skipped
        });

        var normalizer = new ApplicationNormalizer(new NullLogger<ApplicationNormalizer>());
        var fakeRepo = new FakeApplicationRepository(1, 0, 0); // Pretend 1 added

        var service = new DiscoveryService(
            fakeRegistry, 
            normalizer, 
            fakeRepo, 
            new NullLogger<DiscoveryService>());

        // Act
        var result = await service.DiscoverApplicationsAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(default, result.DiscoveryStartedAt);
        Assert.NotNull(result.DiscoveryCompletedAt);
        Assert.Equal(2, result.EntriesInspected);
        Assert.Equal(1, result.EntriesSkipped); // The one with no DisplayName
        Assert.Equal(1, result.ApplicationsDiscovered); // The normalized one
        Assert.Equal(1, result.ApplicationsAdded);
        Assert.Equal(0, result.Errors);
        Assert.False(result.Cancelled);
        Assert.True(fakeRepo.SyncCalled);
    }

    [Fact]
    public async Task DiscoverApplicationsAsync_RegistryThrows_ReturnsErrorAndHandlesGracefully()
    {
        // Arrange
        var fakeRegistry = new FakeRegistryService(new Exception("Registry access denied"));
        var normalizer = new ApplicationNormalizer(new NullLogger<ApplicationNormalizer>());
        var fakeRepo = new FakeApplicationRepository(0, 0, 0);

        var service = new DiscoveryService(
            fakeRegistry, 
            normalizer, 
            fakeRepo, 
            new NullLogger<DiscoveryService>());

        // Act
        var result = await service.DiscoverApplicationsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, result.Errors);
        Assert.Equal(0, result.EntriesInspected);
        Assert.False(fakeRepo.SyncCalled);
    }

    [Fact]
    public async Task DiscoverApplicationsAsync_CancelledToken_SetsCancelledFlag()
    {
        // Arrange
        var fakeRegistry = new FakeRegistryService(new List<RawRegistryApplication> { new RawRegistryApplication { DisplayName = "App" } });
        var normalizer = new ApplicationNormalizer(new NullLogger<ApplicationNormalizer>());
        var fakeRepo = new FakeApplicationRepository(0, 0, 0);

        var service = new DiscoveryService(fakeRegistry, normalizer, fakeRepo, new NullLogger<DiscoveryService>());
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var result = await service.DiscoverApplicationsAsync(cts.Token);

        // Assert
        Assert.True(result.Cancelled);
    }

    [Fact]
    public async Task DiscoverApplicationsAsync_PersistenceFailure_PropagatesException()
    {
        // Arrange
        var fakeRegistry = new FakeRegistryService(new List<RawRegistryApplication> { new RawRegistryApplication { DisplayName = "App" } });
        var normalizer = new ApplicationNormalizer(new NullLogger<ApplicationNormalizer>());
        var fakeRepo = new FakeApplicationRepository(new Exception("DB Failure"));

        var service = new DiscoveryService(fakeRegistry, normalizer, fakeRepo, new NullLogger<DiscoveryService>());

        // Act & Assert
        // The DiscoveryService traps top level exceptions and returns an Error state, it doesn't propagate the exception to the caller.
        var result = await service.DiscoverApplicationsAsync(CancellationToken.None);

        Assert.Equal(1, result.Errors);
        Assert.Equal(0, result.ApplicationsAdded); // Because it failed syncing
    }

    [Fact]
    public async Task DiscoverApplicationsAsync_PartialDiscoveryFailure_NormalizerThrows()
    {
        // Arrange
        var fakeRegistry = new FakeRegistryService(new List<RawRegistryApplication> 
        { 
            new RawRegistryApplication { DisplayName = "Good App" },
            new RawRegistryApplication { DisplayName = "Bad App" }
        });
        
        var badNormalizer = new MockNormalizer(); // A fake normalizer that throws on "Bad App"
        var fakeRepo = new FakeApplicationRepository(1, 0, 0);

        var service = new DiscoveryService(fakeRegistry, badNormalizer, fakeRepo, new NullLogger<DiscoveryService>());

        // Act
        var result = await service.DiscoverApplicationsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, result.Errors);
        Assert.Equal(1, result.ApplicationsDiscovered); // Good App was discovered
    }
    
    private class MockNormalizer : IApplicationNormalizer
    {
        public Application? Normalize(RawRegistryApplication rawApplication)
        {
            if (rawApplication.DisplayName == "Bad App")
                throw new InvalidOperationException("Partial failure test");
            return new Application { Name = "Good App" };
        }
    }

    private class FakeRegistryService : IRegistryService
    {
        private readonly List<RawRegistryApplication>? _entries;
        private readonly Exception? _exceptionToThrow;

        public bool KeyExists(string root, string path) => true;
        public bool ValueExists(string root, string path, string valueName) => true;

        public FakeRegistryService(List<RawRegistryApplication> entries) => _entries = entries;
        public FakeRegistryService(Exception ex) => _exceptionToThrow = ex;

        public Task<IReadOnlyList<RawRegistryApplication>> GetUninstallEntriesAsync(CancellationToken cancellationToken)
        {
            if (_exceptionToThrow != null) throw _exceptionToThrow;
            
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<RawRegistryApplication>>(_entries ?? new List<RawRegistryApplication>());
        }
    }

    private class FakeApplicationRepository : IApplicationRepository
    {
        private readonly int _added;
        private readonly int _updated;
        private readonly int _unchanged;
        private readonly Exception? _exceptionToThrow;
        private class FakeFileSystemService : IFileSystemService
        {
            public bool FileExists(string path) => true;
            public bool DirectoryExists(string path) => true;

            public IEnumerable<string> FindDirectories(string rootPath, string searchTerm)
            {
                if (rootPath == @"C:\Program Files")
                    return new List<string> { @"C:\Program Files\Test App" };
                return Enumerable.Empty<string>();
            }

            public IEnumerable<string> FindFiles(string rootPath, string searchTerm)
            {
                return Enumerable.Empty<string>();
            }
        }
        public bool SyncCalled { get; private set; }

        public FakeApplicationRepository(int added, int updated, int unchanged)
        {
            _added = added;
            _updated = updated;
            _unchanged = unchanged;
        }

        public FakeApplicationRepository(Exception ex)
        {
            _exceptionToThrow = ex;
        }

        public Task<IReadOnlyList<Application>> GetAllAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Application?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SaveAsync(Application application, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<SyncResult> SyncAsync(IEnumerable<Application> discoveredApps, CancellationToken cancellationToken)
        {
            if (_exceptionToThrow != null) throw _exceptionToThrow;
            
            SyncCalled = true;
            return Task.FromResult(new SyncResult
            {
                ApplicationsAdded = _added,
                ApplicationsUpdated = _updated,
                ApplicationsUnchanged = _unchanged
            });
        }
    }
}
