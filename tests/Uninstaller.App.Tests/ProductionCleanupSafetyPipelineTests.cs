using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Uninstaller.App.Services;
using Uninstaller.App.ViewModels;
using Uninstaller.Core;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Infrastructure;
using Uninstaller.Infrastructure.Persistence;
using Uninstaller.Windows;
using Uninstaller.Windows.Filesystem;
using Xunit;

namespace Uninstaller.App.Tests;

public class ProductionCleanupSafetyPipelineTests
{
    private IServiceProvider CreateProductionServiceProvider()
    {
        var services = new ServiceCollection();

        // 1. Core, Infrastructure, Windows production registrations
        services.AddCore();
        services.AddInfrastructure();
        services.AddWindows();

        services.AddSingleton<ObservableItemExecutionTracker>();
        services.AddSingleton<IObservableItemExecutionTracker>(sp => sp.GetRequiredService<ObservableItemExecutionTracker>());
        services.AddSingleton<IItemExecutionTracker>(sp => sp.GetRequiredService<ObservableItemExecutionTracker>());

        services.AddSingleton<ObservableRecoveryItemExecutionTracker>();
        services.AddSingleton<IObservableRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<ObservableRecoveryItemExecutionTracker>());
        services.AddSingleton<IRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<ObservableRecoveryItemExecutionTracker>());

        services.AddSingleton<IErrorBoundaryService, ErrorBoundaryService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ICleanupViewModelFactory, CleanupViewModelFactory>();
        services.AddSingleton<IHistoryViewModelFactory, HistoryViewModelFactory>();

        // Logging
        services.AddLogging(builder => builder.AddDebug());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    [Fact]
    public async Task Req01_ApplicationOwned_AppDataRoamingArtifact_PassesPreflight()
    {
        var provider = CreateProductionServiceProvider();
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICleanupPreflightValidator>();

        var testBase = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "UninstallerSafetyTests");
        var tempAppDir = Path.Combine(testBase, "Telegram Desktop");
        Directory.CreateDirectory(tempAppDir);

        try
        {
            var app = new Application
            {
                Id = Guid.NewGuid(),
                Name = "Telegram Desktop",
                InstallLocation = tempAppDir
            };

            var planItem = new CleanupPlanItem
            {
                Id = Guid.NewGuid(),
                Path = tempAppDir,
                ArtifactType = ArtifactType.Directory,
                Classification = ArtifactClassification.ApplicationOwned,
                RiskLevel = RiskLevel.Low,
                Recommended = true,
                IsProtected = false
            };

            var preflight = await validator.ValidateAsync(planItem, app);

            Assert.True(preflight.IsAuthorized, $"Expected preflight authorized, but got: {preflight.Outcome}, reason: {preflight.FailureReason}");
            Assert.False(preflight.IsProtected);
            Assert.Equal(PreflightValidationOutcome.Authorized, preflight.Outcome);
        }
        finally
        {
            if (Directory.Exists(tempAppDir))
            {
                try { Directory.Delete(tempAppDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Req02_ProtectedWindowsPath_FailsPreflight()
    {
        var provider = CreateProductionServiceProvider();
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICleanupPreflightValidator>();

        var app = new Application { Id = Guid.NewGuid(), Name = "SystemApp" };
        var planItem = new CleanupPlanItem
        {
            Id = Guid.NewGuid(),
            Path = @"C:\Windows\System32\drivers\etc\hosts",
            ArtifactType = ArtifactType.File,
            Classification = ArtifactClassification.ApplicationOwned,
            RiskLevel = RiskLevel.Low,
            Recommended = true,
            IsProtected = false
        };

        var preflight = await validator.ValidateAsync(planItem, app);

        Assert.False(preflight.IsAuthorized);
        Assert.True(preflight.IsProtected);
        Assert.Equal(PreflightValidationOutcome.Protected, preflight.Outcome);
    }

    [Fact]
    public async Task Req03_ProtectedUserDocumentPath_FailsPreflight()
    {
        var provider = CreateProductionServiceProvider();
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICleanupPreflightValidator>();

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var docPath = Path.Combine(userProfile, "Documents", "ImportantDocument.docx");

        var app = new Application { Id = Guid.NewGuid(), Name = "DocApp" };
        var planItem = new CleanupPlanItem
        {
            Id = Guid.NewGuid(),
            Path = docPath,
            ArtifactType = ArtifactType.File,
            Classification = ArtifactClassification.ApplicationOwned,
            RiskLevel = RiskLevel.Low,
            Recommended = true,
            IsProtected = false
        };

        var preflight = await validator.ValidateAsync(planItem, app);

        Assert.False(preflight.IsAuthorized);
        Assert.True(preflight.IsProtected);
        Assert.Equal(PreflightValidationOutcome.Protected, preflight.Outcome);
    }

    [Fact]
    public async Task Req04_HighRiskOrNonApplicationOwned_RegistryArtifact_IsRejected()
    {
        var provider = CreateProductionServiceProvider();
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICleanupPreflightValidator>();

        var app = new Application { Id = Guid.NewGuid(), Name = "RegApp" };
        var planItemHighRisk = new CleanupPlanItem
        {
            Id = Guid.NewGuid(),
            Path = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run",
            ArtifactType = ArtifactType.RegistryKey,
            Classification = ArtifactClassification.SharedDependency,
            RiskLevel = RiskLevel.High,
            Recommended = false,
            IsProtected = false
        };

        var preflight = await validator.ValidateAsync(planItemHighRisk, app);

        Assert.False(preflight.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.ValidationError, preflight.Outcome);
    }

    [Fact]
    public void Req05_CanonicalizedEquivalentPath_ProducesIdenticalProtectionDecision()
    {
        var provider = CreateProductionServiceProvider();
        var resolver = provider.GetRequiredService<ICanonicalPathResolver>();

        var path1 = @"C:\Users\test\AppData\Roaming\Telegram Desktop\..\Telegram Desktop";
        var path2 = @"C:\Users\test\AppData\Roaming\Telegram Desktop\";
        var path3 = @"c:\users\test\appdata\roaming\telegram desktop";

        var res1 = resolver.ResolveAndVerify(path1);
        var res2 = resolver.ResolveAndVerify(path2);
        var res3 = resolver.ResolveAndVerify(path3);

        Assert.Equal(res1.IsProtected, res2.IsProtected);
        Assert.Equal(res2.IsProtected, res3.IsProtected);
        Assert.False(res1.IsProtected);
    }

    [Fact]
    public void Req06_CleanupPlanAndCleanupPreflight_UseConsistentSafetyMetadata()
    {
        var provider = CreateProductionServiceProvider();
        using var scope = provider.CreateScope();
        var evidenceEngine = scope.ServiceProvider.GetRequiredService<IEvidenceEngine>();
        var pathResolver = scope.ServiceProvider.GetRequiredService<ICanonicalPathResolver>();

        var appDataCandidate = new ResidualArtifactCandidate(
            new Artifact { Id = Guid.NewGuid(), Path = @"C:\Users\test\AppData\Roaming\Telegram Desktop", Type = ArtifactType.Directory },
            new List<Evidence> { new Evidence(EvidenceType.ExactInstallLocation, "Exact match", "Scanner") },
            "Scanner"
        );

        var analysisResult = evidenceEngine.Analyze(appDataCandidate);
        var pathSafety = pathResolver.ResolveAndVerify(appDataCandidate.Artifact.Path!);

        Assert.Equal(analysisResult.IsProtected, pathSafety.IsProtected);
        Assert.False(analysisResult.IsProtected);
        Assert.Equal(ArtifactClassification.ApplicationOwned, analysisResult.Classification);
    }

    [Fact]
    public async Task Req07_To_13_EndToEndProductionComposition_FullExecutionPipeline()
    {
        var testBase = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "UninstallerSafetyTests");
        var tempAppDir = Path.Combine(testBase, "E2EApp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempAppDir);
        var artifactFile = Path.Combine(tempAppDir, "app_data.bin");
        File.WriteAllText(artifactFile, "important persistent state");

        var unselectedFile = Path.Combine(tempAppDir, "unselected_data.bin");
        File.WriteAllText(unselectedFile, "should remain untouched");

        try
        {
            var provider = CreateProductionServiceProvider();
            var cleanupFactory = provider.GetRequiredService<ICleanupViewModelFactory>();

            var app = new Application
            {
                Id = Guid.NewGuid(),
                Name = "E2E Pipeline Test App",
                InstallLocation = tempAppDir
            };

            var session = new UninstallSession
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                Status = UninstallSessionStatus.Completed,
                CreatedAt = DateTime.UtcNow
            };

            using (var initScope = provider.CreateScope())
            {
                var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.Applications.Add(app);
                db.UninstallSessions.Add(session);
                await db.SaveChangesAsync();
            }

            var item1 = new CleanupPlanItem
            {
                Id = Guid.NewGuid(),
                Path = artifactFile,
                ArtifactType = ArtifactType.File,
                Classification = ArtifactClassification.ApplicationOwned,
                RiskLevel = RiskLevel.Low,
                Recommended = true,
                IsProtected = false
            };

            var itemUnselected = new CleanupPlanItem
            {
                Id = Guid.NewGuid(),
                Path = unselectedFile,
                ArtifactType = ArtifactType.File,
                Classification = ArtifactClassification.ApplicationOwned,
                RiskLevel = RiskLevel.Low,
                Recommended = true,
                IsProtected = false
            };

            var plan = new CleanupPlan
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                UninstallSessionId = session.Id,
                CreatedAt = DateTime.UtcNow,
                Items = new List<CleanupPlanItem> { item1, itemUnselected }
            };

            // User selects ONLY item1 (itemUnselected is not in selectedItemIds)
            var selectedItemIds = new[] { item1.Id };

            // 1. Create execution VM via strongly-typed factory with dedicated scope
            using var execVm = cleanupFactory.CreateExecutionViewModel(plan, app, selectedItemIds);

            // 2. Start Async Execution
            await execVm.StartExecutionAsync();

            // 3. Assert execution results
            Assert.Equal(1, execVm.SuccessCount);
            Assert.Equal(0, execVm.FailedCount);
            Assert.Equal(CleanupItemExecutionState.Succeeded, execVm.Items.First(i => i.Id == item1.Id).State);

            // 4. Assert filesystem state: selected artifact deleted, unselected untouched
            Assert.False(File.Exists(artifactFile), "Selected artifact must be deleted.");
            Assert.True(File.Exists(unselectedFile), "Unselected artifact must NOT be deleted.");

            // 5. Assert Transaction Journal persisted exact ItemId correlation
            using (var verifyScope = provider.CreateScope())
            {
                var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var journalEntries = await db.TransactionJournalEntries.Where(j => j.SessionId == session.Id).ToListAsync();
                Assert.NotEmpty(journalEntries);
                Assert.Contains(journalEntries, j => j.ItemId == item1.Id);
                Assert.DoesNotContain(journalEntries, j => j.ItemId == itemUnselected.Id);

                var backup = await db.Backups.FirstOrDefaultAsync(b => b.SessionId == session.Id && b.OriginalPath == artifactFile);
                Assert.NotNull(backup);
                Assert.Equal(BackupVerificationStatus.Verified, backup.VerificationStatus);
            }
        }
        finally
        {
            if (Directory.Exists(tempAppDir))
            {
                try { Directory.Delete(tempAppDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Req14_SchemaIntegrity_MissingParentSession_ThrowsForeignKeyException()
    {
        var provider = CreateProductionServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var missingSessionId = Guid.NewGuid();
        var backup = new Backup
        {
            Id = Guid.NewGuid(),
            SessionId = missingSessionId, // Missing parent in UninstallSessions
            ArtifactId = Guid.NewGuid(),
            ArtifactType = ArtifactType.File,
            OriginalPath = @"C:\Test\file.bin",
            BackupPath = @"C:\Test\backup.bin",
            Status = BackupStatus.Committed,
            VerificationStatus = BackupVerificationStatus.Verified
        };

        db.Backups.Add(backup);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);
        Assert.Contains("FOREIGN KEY", ex.InnerException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Req15_SchemaIntegrity_MissingApplicationParent_ThrowsForeignKeyException()
    {
        var provider = CreateProductionServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var missingAppId = Guid.NewGuid();
        var session = new UninstallSession
        {
            Id = Guid.NewGuid(),
            ApplicationId = missingAppId, // Missing parent in Applications
            Status = UninstallSessionStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };

        db.UninstallSessions.Add(session);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);
        Assert.Contains("FOREIGN KEY", ex.InnerException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Req16_SchemaIntegrity_MissingCleanupPlanParent_ThrowsForeignKeyException()
    {
        var provider = CreateProductionServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var missingPlanId = Guid.NewGuid();
        var planItem = new CleanupPlanItem
        {
            Id = Guid.NewGuid(),
            CleanupPlanId = missingPlanId, // Missing parent in CleanupPlans
            ArtifactId = Guid.NewGuid(),
            ArtifactType = ArtifactType.File,
            Path = @"C:\Test\file.bin",
            Classification = ArtifactClassification.ApplicationOwned,
            RiskLevel = RiskLevel.Low,
            Recommended = true
        };

        db.CleanupPlanItems.Add(planItem);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);
        Assert.Contains("FOREIGN KEY", ex.InnerException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Req17_ProductionIdentityLifecycle_ResidualAnalysisToBackup_PreservesAllForeignKeys()
    {
        var testBase = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "UninstallerSafetyTests");
        var tempAppDir = Path.Combine(testBase, "Telegram Desktop");
        Directory.CreateDirectory(tempAppDir);
        var artifactFile = Path.Combine(tempAppDir, "telegram.exe");
        File.WriteAllText(artifactFile, "binary content");

        try
        {
            var provider = CreateProductionServiceProvider();
            
            var app = new Application
            {
                Id = Guid.NewGuid(),
                Name = "Telegram Desktop",
                InstallLocation = tempAppDir
            };

            var session = new UninstallSession
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                Status = UninstallSessionStatus.Completed,
                CreatedAt = DateTime.UtcNow
            };

            using (var initScope = provider.CreateScope())
            {
                var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.Applications.Add(app);
                db.UninstallSessions.Add(session);
                await db.SaveChangesAsync();
            }

            // 1. Run Residual Analysis
            using var analysisScope = provider.CreateScope();
            var analysisService = analysisScope.ServiceProvider.GetRequiredService<IResidualAnalysisService>();
            var analysisResult = await analysisService.RunAnalysisAsync(session, app);

            Assert.NotNull(analysisResult.Plan);
            // Critical Identity Assertion: Plan.UninstallSessionId MUST equal the persisted UninstallSession.Id
            Assert.Equal(session.Id, analysisResult.Plan.UninstallSessionId);

            // 2. Persist CleanupPlan
            using (var planScope = provider.CreateScope())
            {
                var db = planScope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.CleanupPlans.Add(analysisResult.Plan);
                await db.SaveChangesAsync(); // Must not throw FK violation!
            }

            // 3. Execute Cleanup with Backup
            var cleanupFactory = provider.GetRequiredService<ICleanupViewModelFactory>();
            var selectedIds = analysisResult.Plan.Items.Select(i => i.Id).ToList();

            using var execVm = cleanupFactory.CreateExecutionViewModel(analysisResult.Plan, app, selectedIds);
            await execVm.StartExecutionAsync();

            // 4. Verify All Entities and FK relationships in Database
            using (var verifyScope = provider.CreateScope())
            {
                var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                // Verify Backups
                var backups = await db.Backups.Where(b => b.SessionId == session.Id).ToListAsync();
                Assert.NotEmpty(backups);
                foreach (var b in backups)
                {
                    Assert.Equal(session.Id, b.SessionId);
                    Assert.Equal(BackupVerificationStatus.Verified, b.VerificationStatus);
                    
                    // Verify parent session exists
                    var parentSession = await db.UninstallSessions.FindAsync(b.SessionId);
                    Assert.NotNull(parentSession);
                }

                // Verify Journal Entries
                var journalEntries = await db.TransactionJournalEntries.Where(j => j.SessionId == session.Id).ToListAsync();
                Assert.NotEmpty(journalEntries);
            }
        }
        finally
        {
            if (Directory.Exists(tempAppDir))
            {
                try { Directory.Delete(tempAppDir, true); } catch { }
            }
        }
    }
}
