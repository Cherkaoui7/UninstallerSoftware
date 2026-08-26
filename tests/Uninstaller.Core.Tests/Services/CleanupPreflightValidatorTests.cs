using System;
using System.Threading.Tasks;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class CleanupPreflightValidatorTests
{
    private readonly Mock<ICanonicalPathResolver> _pathResolverMock = new();
    private readonly Mock<IFileSystemService> _fileSystemMock = new();
    private readonly Mock<IRegistryService> _registryMock = new();
    private readonly Mock<IShortcutService> _shortcutMock = new();
    private readonly CleanupPreflightValidator _validator;

    public CleanupPreflightValidatorTests()
    {
        _validator = new CleanupPreflightValidator(
            _pathResolverMock.Object,
            _fileSystemMock.Object,
            _registryMock.Object,
            _shortcutMock.Object
        );
    }

    private CleanupPlanItem CreateValidPlanItem(ArtifactType type = ArtifactType.File, string path = @"C:\App\test.txt")
    {
        return new CleanupPlanItem
        {
            Id = Guid.NewGuid(),
            Recommended = true,
            RiskLevel = RiskLevel.Low,
            ArtifactType = type,
            Path = path,
            Classification = ArtifactClassification.ApplicationOwned,
            IsProtected = false
        };
    }

    private Application CreateApp(string installLoc = @"C:\App")
    {
        return new Application
        {
            Id = Guid.NewGuid(),
            Name = "Test App",
            InstallLocation = installLoc
        };
    }

    [Fact]
    public async Task ValidateAsync_ValidFile_ReturnsAuthorized()
    {
        var item = CreateValidPlanItem();
        var app = CreateApp();
        
        _pathResolverMock.Setup(r => r.IsPathContainedWithin(item.Path, app.InstallLocation)).Returns(true);
        _pathResolverMock.Setup(r => r.ResolveAndVerify(item.Path, app.InstallLocation, default))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = item.Path, IsWithinExpectedRoot = true });
        _pathResolverMock.Setup(r => r.ResolveAndVerify(app.InstallLocation, null, default))
            .Returns(new PathSafetyResult { CanonicalPath = app.InstallLocation });
        _fileSystemMock.Setup(f => f.FileExists(item.Path)).Returns(true);

        var result = await _validator.ValidateAsync(item, app);

        Assert.True(result.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.Authorized, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_ValidDirectory_ReturnsAuthorized()
    {
        var item = CreateValidPlanItem(ArtifactType.Directory, @"C:\App\Data");
        var app = CreateApp();

        _pathResolverMock.Setup(r => r.IsPathContainedWithin(item.Path, app.InstallLocation)).Returns(true);
        _pathResolverMock.Setup(r => r.ResolveAndVerify(item.Path, app.InstallLocation, default))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = item.Path, IsWithinExpectedRoot = true });
        _pathResolverMock.Setup(r => r.ResolveAndVerify(app.InstallLocation, null, default))
            .Returns(new PathSafetyResult { CanonicalPath = app.InstallLocation });
        _fileSystemMock.Setup(f => f.DirectoryExists(item.Path)).Returns(true);

        var result = await _validator.ValidateAsync(item, app);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task ValidateAsync_MissingFile_ReturnsMissing()
    {
        var item = CreateValidPlanItem();
        var app = CreateApp();

        _pathResolverMock.Setup(r => r.IsPathContainedWithin(item.Path, app.InstallLocation)).Returns(true);
        _pathResolverMock.Setup(r => r.ResolveAndVerify(item.Path, app.InstallLocation, default))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = item.Path });
        _fileSystemMock.Setup(f => f.FileExists(item.Path)).Returns(false);

        var result = await _validator.ValidateAsync(item, app);

        Assert.False(result.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.Missing, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_RootDirectory_ReturnsOutsideExpectedRoot()
    {
        var item = CreateValidPlanItem(ArtifactType.Directory, @"C:\App");
        var app = CreateApp();

        _pathResolverMock.Setup(r => r.IsPathContainedWithin(item.Path, app.InstallLocation)).Returns(true);
        _pathResolverMock.Setup(r => r.ResolveAndVerify(item.Path, app.InstallLocation, default))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = @"C:\App", IsWithinExpectedRoot = true });
        _pathResolverMock.Setup(r => r.ResolveAndVerify(app.InstallLocation, null, default))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = @"C:\App" });

        var result = await _validator.ValidateAsync(item, app);

        Assert.False(result.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.OutsideExpectedRoot, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_ReparsePoint_ReturnsReparseBlocked()
    {
        var item = CreateValidPlanItem();
        var app = CreateApp();

        _pathResolverMock.Setup(r => r.IsPathContainedWithin(item.Path, app.InstallLocation)).Returns(true);
        _pathResolverMock.Setup(r => r.ResolveAndVerify(item.Path, app.InstallLocation, default))
            .Returns(new PathSafetyResult { IsValid = true, IsReparsePoint = true });

        var result = await _validator.ValidateAsync(item, app);

        Assert.False(result.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.ReparseBlocked, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_ProtectedPath_ReturnsProtected()
    {
        var item = CreateValidPlanItem();
        var app = CreateApp();

        _pathResolverMock.Setup(r => r.IsPathContainedWithin(item.Path, app.InstallLocation)).Returns(true);
        _pathResolverMock.Setup(r => r.ResolveAndVerify(item.Path, app.InstallLocation, default))
            .Returns(new PathSafetyResult { IsValid = true, IsProtected = true });

        var result = await _validator.ValidateAsync(item, app);

        Assert.False(result.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.Protected, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_ClassificationChanged_ReturnsValidationError()
    {
        var item = CreateValidPlanItem();
        item.Classification = ArtifactClassification.UserData;
        var app = CreateApp();

        var result = await _validator.ValidateAsync(item, app);

        Assert.False(result.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.ValidationError, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_RiskChanged_ReturnsValidationError()
    {
        var item = CreateValidPlanItem();
        item.RiskLevel = RiskLevel.High;
        var app = CreateApp();

        var result = await _validator.ValidateAsync(item, app);

        Assert.False(result.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.ValidationError, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_RecommendationChanged_ReturnsValidationError()
    {
        var item = CreateValidPlanItem();
        item.Recommended = false;
        var app = CreateApp();

        var result = await _validator.ValidateAsync(item, app);

        Assert.False(result.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.ValidationError, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_ValidRegistryKey_ReturnsAuthorized()
    {
        var item = CreateValidPlanItem(ArtifactType.RegistryKey, @"HKCU\Software\MyApp");
        var app = CreateApp();

        _registryMock.Setup(r => r.KeyExists("HKCU", @"Software\MyApp")).Returns(true);

        var result = await _validator.ValidateAsync(item, app);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task ValidateAsync_MissingRegistryKey_ReturnsMissing()
    {
        var item = CreateValidPlanItem(ArtifactType.RegistryKey, @"HKCU\Software\MyApp");
        var app = CreateApp();

        _registryMock.Setup(r => r.KeyExists("HKCU", @"Software\MyApp")).Returns(false);

        var result = await _validator.ValidateAsync(item, app);

        Assert.False(result.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.Missing, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_ValidShortcut_ReturnsAuthorized()
    {
        var item = CreateValidPlanItem(ArtifactType.Shortcut, @"C:\Desktop\App.lnk");
        var app = CreateApp();

        _pathResolverMock.Setup(r => r.ResolveAndVerify(item.Path, null, default))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = item.Path });
            
        _shortcutMock.Setup(s => s.ShortcutExists(item.Path)).Returns(true);
        _shortcutMock.Setup(s => s.GetShortcutTarget(item.Path)).Returns(@"C:\App\App.exe");

        _pathResolverMock.Setup(r => r.ResolveAndVerify(@"C:\App\App.exe", null, default))
            .Returns(new PathSafetyResult { IsValid = true });

        _pathResolverMock.Setup(r => r.IsPathContainedWithin(@"C:\App\App.exe", app.InstallLocation)).Returns(true);

        var result = await _validator.ValidateAsync(item, app);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task ValidateAsync_ShortcutIdentityMismatch_ReturnsIdentityMismatch()
    {
        var item = CreateValidPlanItem(ArtifactType.Shortcut, @"C:\Desktop\App.lnk");
        var app = CreateApp(); // InstallLocation = C:\App

        _pathResolverMock.Setup(r => r.ResolveAndVerify(item.Path, null, default))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = item.Path });
            
        _shortcutMock.Setup(s => s.ShortcutExists(item.Path)).Returns(true);
        _shortcutMock.Setup(s => s.GetShortcutTarget(item.Path)).Returns(@"C:\AnotherApp\App.exe");

        _pathResolverMock.Setup(r => r.ResolveAndVerify(@"C:\AnotherApp\App.exe", null, default))
            .Returns(new PathSafetyResult { IsValid = true });

        _pathResolverMock.Setup(r => r.IsPathContainedWithin(@"C:\AnotherApp\App.exe", app.InstallLocation)).Returns(false);

        var result = await _validator.ValidateAsync(item, app);

        Assert.False(result.IsAuthorized);
        Assert.Equal(PreflightValidationOutcome.IdentityMismatch, result.Outcome);
    }
}
