using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Infrastructure.Persistence;
using Uninstaller.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Uninstaller.Tests;

public class UninstallServiceVerificationTests
{
    private ServiceProvider BuildServiceProvider(Action<Mock<IRegistryService>> configureRegistry, Action<Mock<IProcessExecutor>> configureExecutor)
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IUninstallSessionRepository, UninstallSessionRepository>();
        services.AddScoped<IApplicationDeduplicator, ApplicationDeduplicator>();

        var registryMock = new Mock<IRegistryService>();
        configureRegistry(registryMock);
        services.AddSingleton(registryMock.Object);
        
        services.AddSingleton<IApplicationNormalizer, ApplicationNormalizer>();
        services.AddScoped<IDiscoveryService, DiscoveryService>();
        services.AddScoped<IUninstallService, UninstallService>();
        
        var parserMock = new Mock<ICommandParser>();
        parserMock.Setup(p => p.Parse(It.IsAny<Application>()))
            .Returns(new StructuredCommand { ExecutionType = ExecutionType.Executable, ExecutablePath = "test.exe" });
        services.AddSingleton(parserMock.Object);

        var executorMock = new Mock<IProcessExecutor>();
        configureExecutor(executorMock);
        services.AddSingleton(executorMock.Object);
        
        services.AddLogging();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Test1_FirstDiscoveryTrue_SecondDiscoveryFalse_Succeeds()
    {
        int discoveryCount = 0;
        var provider = BuildServiceProvider(registry => 
        {
            registry.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => 
                {
                    discoveryCount++;
                    if (discoveryCount == 1)
                        return new List<RawRegistryApplication> { new RawRegistryApplication { DisplayName = "7-Zip", WindowsInstaller = 1, RegistryKeyName = "7zip-guid", UninstallString = "test.exe" } };
                    return new List<RawRegistryApplication>();
                });
        }, executor => 
        {
            executor.Setup(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutionResult { ExitCode = 0 });
        });

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = new Application { Id = Guid.NewGuid(), Name = "7-Zip", IsPresent = true, IsWindowsInstaller = true, RegistryKeyName = "7zip-guid", UninstallCommand = "test.exe" };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var service = (UninstallService)scope.ServiceProvider.GetRequiredService<IUninstallService>();
        service.DelayTask = (ms, ct) => Task.CompletedTask; // Fast retry

        var session = await service.RunUninstallAsync(app, CancellationToken.None);

        Assert.Equal(UninstallSessionStatus.Completed, session.Status);
        Assert.Equal(VerificationResult.VerifiedRemoved, session.VerificationResult);
        Assert.Equal(2, discoveryCount);
    }

    [Fact]
    public async Task Test2_AllDiscoveryAttemptsTrue_FailsAfterTimeout()
    {
        int discoveryCount = 0;
        var provider = BuildServiceProvider(registry => 
        {
            registry.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => 
                {
                    discoveryCount++;
                    return new List<RawRegistryApplication> { new RawRegistryApplication { DisplayName = "7-Zip", WindowsInstaller = 1, RegistryKeyName = "7zip-guid", UninstallString = "test.exe" } };
                });
        }, executor => 
        {
            executor.Setup(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutionResult { ExitCode = 0 });
        });

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = new Application { Id = Guid.NewGuid(), Name = "7-Zip", IsPresent = true, IsWindowsInstaller = true, RegistryKeyName = "7zip-guid", UninstallCommand = "test.exe" };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var service = (UninstallService)scope.ServiceProvider.GetRequiredService<IUninstallService>();
        service.DelayTask = (ms, ct) => Task.CompletedTask;

        var session = await service.RunUninstallAsync(app, CancellationToken.None);

        using var scope3 = provider.CreateScope();
        var db3 = scope3.ServiceProvider.GetRequiredService<AppDbContext>();
        var finalApp = await db3.Applications.FindAsync(app.Id);



        Assert.Equal(UninstallSessionStatus.Failed, session.Status);
        Assert.Equal(VerificationResult.StillInstalled, session.VerificationResult);
        Assert.Equal(20, discoveryCount); // Max retries
    }

    [Fact]
    public async Task Test3_ExitCodeNotZero_ImmediateFailure()
    {
        int discoveryCount = 0;
        var provider = BuildServiceProvider(registry => 
        {
            registry.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { discoveryCount++; return new List<RawRegistryApplication>(); });
        }, executor => 
        {
            executor.Setup(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutionResult { ExitCode = 1 });
        });

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = new Application { Id = Guid.NewGuid(), Name = "7-Zip", IsPresent = true, IsWindowsInstaller = true, RegistryKeyName = "7zip-guid", UninstallCommand = "test.exe" };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var service = (UninstallService)scope.ServiceProvider.GetRequiredService<IUninstallService>();
        
        var session = await service.RunUninstallAsync(app, CancellationToken.None);

        Assert.Equal(UninstallSessionStatus.Failed, session.Status);
        Assert.Equal(VerificationResult.VerificationFailed, session.VerificationResult);
        Assert.Equal(0, discoveryCount); // Should not even attempt discovery
    }

    [Fact]
    public async Task Test4_CancellationTokenCancelledDuringRetry_CancelsSafely()
    {
        var provider = BuildServiceProvider(registry => 
        {
            registry.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RawRegistryApplication> { new RawRegistryApplication { DisplayName = "7-Zip", WindowsInstaller = 1, RegistryKeyName = "7zip-guid", UninstallString = "test.exe" } });
        }, executor => 
        {
            executor.Setup(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutionResult { ExitCode = 0 });
        });

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = new Application { Id = Guid.NewGuid(), Name = "7-Zip", IsPresent = true, IsWindowsInstaller = true, RegistryKeyName = "7zip-guid", UninstallCommand = "test.exe" };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        var service = (UninstallService)scope.ServiceProvider.GetRequiredService<IUninstallService>();
        
        service.DelayTask = (ms, ct) => 
        { 
            cts.Cancel(); // Cancel on first delay
            return Task.Delay(1, ct); 
        };

        var session = await service.RunUninstallAsync(app, cts.Token);

        Assert.Equal(UninstallSessionStatus.Cancelled, session.Status);
        Assert.Equal(VerificationResult.Unknown, session.VerificationResult); // Cancellation aborts verification
    }

    [Fact]
    public async Task Test5_AlreadyAbsent_SucceedsImmediately()
    {
        int discoveryCount = 0;
        var provider = BuildServiceProvider(registry => 
        {
            registry.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => 
                {
                    discoveryCount++;
                    return new List<RawRegistryApplication>(); // Empty
                });
        }, executor => 
        {
            executor.Setup(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutionResult { ExitCode = 0 });
        });

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = new Application { Id = Guid.NewGuid(), Name = "7-Zip", IsPresent = true, IsWindowsInstaller = true, RegistryKeyName = "7zip-guid", UninstallCommand = "test.exe" };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var service = (UninstallService)scope.ServiceProvider.GetRequiredService<IUninstallService>();
        service.DelayTask = (ms, ct) => Task.CompletedTask;

        var session = await service.RunUninstallAsync(app, CancellationToken.None);

        Assert.Equal(UninstallSessionStatus.Completed, session.Status);
        Assert.Equal(VerificationResult.VerifiedRemoved, session.VerificationResult);
        Assert.Equal(1, discoveryCount);
    }

    [Fact]
    public async Task Test6_VerifyPersistedApplicationRemainsInSQLite_IsPresentFalse()
    {
        var provider = BuildServiceProvider(registry => 
        {
            registry.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RawRegistryApplication>());
        }, executor => 
        {
            executor.Setup(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutionResult { ExitCode = 0 });
        });

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = new Application { Id = Guid.NewGuid(), Name = "7-Zip", IsPresent = true, IsWindowsInstaller = true, RegistryKeyName = "7zip-guid", UninstallCommand = "test.exe" };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var service = (UninstallService)scope.ServiceProvider.GetRequiredService<IUninstallService>();
        await service.RunUninstallAsync(app, CancellationToken.None);

        // Verify fresh context
        using var scope2 = provider.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedApp = await db2.Applications.FindAsync(app.Id);
        
        Assert.NotNull(persistedApp);
        Assert.False(persistedApp.IsPresent);
    }

    [Fact]
    public async Task Test7_VerifyUninstallSessionBecomesCompleted_AndVerifiedRemoved()
    {
        var provider = BuildServiceProvider(registry => 
        {
            registry.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RawRegistryApplication>());
        }, executor => 
        {
            executor.Setup(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutionResult { ExitCode = 0 });
        });

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = new Application { Id = Guid.NewGuid(), Name = "7-Zip", IsPresent = true, IsWindowsInstaller = true, RegistryKeyName = "7zip-guid", UninstallCommand = "test.exe" };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var service = (UninstallService)scope.ServiceProvider.GetRequiredService<IUninstallService>();
        var session = await service.RunUninstallAsync(app, CancellationToken.None);

        using var scope2 = provider.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedSession = await db2.UninstallSessions.FindAsync(session.Id);
        
        Assert.NotNull(persistedSession);
        Assert.Equal(UninstallSessionStatus.Completed, persistedSession.Status);
        Assert.Equal(VerificationResult.VerifiedRemoved, persistedSession.VerificationResult);
    }
}
