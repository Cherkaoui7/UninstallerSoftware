using System;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.Core.Models;
using Uninstaller.Core.Services;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class ApplicationNormalizerTests
{
    private readonly ApplicationNormalizer _normalizer;

    public ApplicationNormalizerTests()
    {
        _normalizer = new ApplicationNormalizer(new NullLogger<ApplicationNormalizer>());
    }

    [Fact]
    public void Normalize_SkipsWhenDisplayNameIsNull()
    {
        var raw = new RawRegistryApplication { DisplayName = null };
        var result = _normalizer.Normalize(raw);
        Assert.Null(result);
    }

    [Fact]
    public void Normalize_SkipsWhenDisplayNameIsWhitespace()
    {
        var raw = new RawRegistryApplication { DisplayName = "   " };
        var result = _normalizer.Normalize(raw);
        Assert.Null(result);
    }

    [Fact]
    public void Normalize_NormalizesWhitespaceAndNulls()
    {
        var raw = new RawRegistryApplication 
        { 
            DisplayName = "  Test App  ",
            Publisher = "   ",
            DisplayVersion = ""
        };

        var result = _normalizer.Normalize(raw);

        Assert.NotNull(result);
        Assert.Equal("Test App", result.Name);
        Assert.Null(result.Publisher);
        Assert.Null(result.Version);
    }

    [Fact]
    public void Normalize_NormalizesInstallLocation()
    {
        var raw = new RawRegistryApplication 
        { 
            DisplayName = "App",
            InstallLocation = "\"C:\\Program Files\\App\\\"" 
        };

        var result = _normalizer.Normalize(raw);

        Assert.NotNull(result);
        Assert.Equal("C:\\Program Files\\App", result.InstallLocation);
    }

    [Fact]
    public void Normalize_PreservesUninstallCommand()
    {
        var cmd = "\"C:\\Program Files\\App\\uninstall.exe\" /quiet /norestart";
        var raw = new RawRegistryApplication 
        { 
            DisplayName = "App",
            UninstallString = "  " + cmd + "  " 
        };

        var result = _normalizer.Normalize(raw);

        Assert.NotNull(result);
        Assert.Equal(cmd, result.UninstallCommand);
    }

    [Fact]
    public void Normalize_ParsesInstallDate()
    {
        var raw = new RawRegistryApplication 
        { 
            DisplayName = "App",
            InstallDate = "20230514" 
        };

        var result = _normalizer.Normalize(raw);

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 5, 14), result.InstallDate);
    }

    [Fact]
    public void Normalize_HandlesInvalidInstallDateSafely()
    {
        var raw = new RawRegistryApplication 
        { 
            DisplayName = "App",
            InstallDate = "NotADate" 
        };

        var result = _normalizer.Normalize(raw);

        Assert.NotNull(result);
        Assert.Null(result.InstallDate);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(null, false)]
    [InlineData(2, false)]
    public void Normalize_InterpretsSystemComponentConservatively(int? sysComp, bool expected)
    {
        var raw = new RawRegistryApplication 
        { 
            DisplayName = "App",
            SystemComponent = sysComp
        };

        var result = _normalizer.Normalize(raw);

        Assert.NotNull(result);
        Assert.Equal(expected, result.IsSystemComponent);
    }

    [Fact]
    public void Normalize_HandlesMalformedPaths()
    {
        var raw = new RawRegistryApplication 
        { 
            DisplayName = "App",
            InstallLocation = "  \"\"C:\\Malformed\\Path\"  " 
        };

        var result = _normalizer.Normalize(raw);

        Assert.NotNull(result);
        // It trims quotes and whitespace
        Assert.Equal("C:\\Malformed\\Path", result.InstallLocation);
    }

    [Fact]
    public void Normalize_HandlesInvalidSize()
    {
        // The normalizer expects EstimatedSize to be an int (passed down from RawRegistryApplication)
        // If it's missing or null in RawRegistryApplication, it should map to null
        var raw = new RawRegistryApplication 
        { 
            DisplayName = "App",
            EstimatedSize = null
        };

        var result = _normalizer.Normalize(raw);

        Assert.NotNull(result);
        Assert.Null(result.EstimatedSize);
    }
}
