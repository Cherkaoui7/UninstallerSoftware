using System;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class CommandParserTests
{
    private readonly Mock<IFileSystemService> _fileSystemMock;
    private readonly CommandParser _parser;

    public CommandParserTests()
    {
        _fileSystemMock = new Mock<IFileSystemService>();
        // Default to file exists so existing tests don't break
        _fileSystemMock.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _parser = new CommandParser(_fileSystemMock.Object, NullLogger<CommandParser>.Instance);
    }

    [Fact]
    public void Parse_MissingCommand_ReturnsMissingStrategy()
    {
        var app = new Application { UninstallCommand = null, QuietUninstallCommand = " " };
        var result = _parser.Parse(app);

        Assert.Equal(ExecutionType.Missing, result.ExecutionType);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_MsiUninstall_SetsMsiStrategy()
    {
        var app = new Application
        {
            UninstallCommand = "MsiExec.exe /I{GUID}",
            IsWindowsInstaller = false
        };

        var result = _parser.Parse(app);

        Assert.Equal(ExecutionType.Msi, result.ExecutionType);
        Assert.Equal("MsiExec.exe", result.ExecutablePath);
        Assert.Equal("/I{GUID}", result.Arguments);
    }

    [Fact]
    public void Parse_WindowsInstallerFlag_ForcesMsiStrategy()
    {
        var app = new Application
        {
            UninstallCommand = "\"C:\\Path\\unins000.exe\" /SILENT",
            IsWindowsInstaller = true
        };

        var result = _parser.Parse(app);

        Assert.Equal(ExecutionType.Msi, result.ExecutionType);
        Assert.Equal("C:\\Path\\unins000.exe", result.ExecutablePath);
        Assert.Equal("/SILENT", result.Arguments);
    }

    [Fact]
    public void Parse_QuietUninstall_PrefersQuietOverNormal()
    {
        var app = new Application
        {
            UninstallCommand = "\"C:\\App\\uninst.exe\"",
            QuietUninstallCommand = "\"C:\\App\\uninst.exe\" /S"
        };

        var result = _parser.Parse(app);

        Assert.Equal(ExecutionType.QuietExecutable, result.ExecutionType);
        Assert.Equal("C:\\App\\uninst.exe", result.ExecutablePath);
        Assert.Equal("/S", result.Arguments);
        Assert.Equal(app.QuietUninstallCommand, result.OriginalCommand);
    }

    [Fact]
    public void Parse_ExeUninstall_SetsExecutableStrategy()
    {
        var app = new Application
        {
            UninstallCommand = "C:\\Program Files\\App\\uninstall.exe"
        };

        var result = _parser.Parse(app);

        Assert.Equal(ExecutionType.Executable, result.ExecutionType);
        Assert.Equal("C:\\Program Files\\App\\uninstall.exe", result.ExecutablePath);
        Assert.Null(result.Arguments);
    }

    [Fact]
    public void Parse_ArgumentsContainingSpaces_ParsesCorrectly()
    {
        var app = new Application
        {
            UninstallCommand = "\"C:\\App\\uninst.exe\" /uninstall \"C:\\Program Files\\Target\""
        };

        var result = _parser.Parse(app);

        Assert.Equal("C:\\App\\uninst.exe", result.ExecutablePath);
        Assert.Equal("/uninstall \"C:\\Program Files\\Target\"", result.Arguments);
    }

    [Fact]
    public void Parse_UnquotedPathWithSpacesAndArguments_HeuristicParsesExe()
    {
        var app = new Application
        {
            UninstallCommand = "C:\\Program Files\\App Folder\\uninst.exe /S /v/qn"
        };

        var result = _parser.Parse(app);

        Assert.Equal("C:\\Program Files\\App Folder\\uninst.exe", result.ExecutablePath);
        Assert.Equal("/S /v/qn", result.Arguments);
    }

    [Fact]
    public void Parse_MalformedCommand_ExtractsExecutable()
    {
        var app = new Application
        {
            // Missing closing quote
            UninstallCommand = "\"C:\\App\\uninst.exe /S"
        };

        var result = _parser.Parse(app);

        // Should return invalid because of missing quote
        Assert.Equal(ExecutionType.Unknown, result.ExecutionType);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_BlockedExecutable_ReturnsUnknown()
    {
        var app = new Application { UninstallCommand = "cmd.exe /c del C:\\test.txt" };
        var result = _parser.Parse(app);

        Assert.Equal(ExecutionType.Unknown, result.ExecutionType);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_ShellInjection_ReturnsUnknown()
    {
        var app = new Application { UninstallCommand = "\"C:\\App\\uninst.exe\" & format C:" };
        var result = _parser.Parse(app);

        Assert.Equal(ExecutionType.Unknown, result.ExecutionType);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_RequiresElevation_Heuristics()
    {
        var hklmApp = new Application { UninstallCommand = "app.exe", RegistrySource = "HKLM" };
        var hkcuApp = new Application { UninstallCommand = "app.exe", RegistrySource = "CurrentUser" };
        var appDataApp = new Application { UninstallCommand = "app.exe", InstallLocation = "C:\\Users\\Bob\\AppData\\Local\\App" };

        Assert.True(_parser.Parse(hklmApp).RequiresElevation);
        Assert.False(_parser.Parse(hkcuApp).RequiresElevation);
        Assert.False(_parser.Parse(appDataApp).RequiresElevation);
    }

    [Fact]
    public void Parse_QuotedExecutablePath_NoArguments_ParsesCorrectly()
    {
        var app = new Application
        {
            UninstallCommand = "\"C:\\Program Files\\App\\AppUninstaller.exe\""
        };
        var result = _parser.Parse(app);
        Assert.Equal("C:\\Program Files\\App\\AppUninstaller.exe", result.ExecutablePath);
        Assert.Null(result.Arguments);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Parse_QuotedExecutablePath_WithArguments_ParsesCorrectly()
    {
        var app = new Application
        {
            UninstallCommand = "\"C:\\Program Files\\App\\AppUninstaller.exe\" /silent /cleanup"
        };
        var result = _parser.Parse(app);
        Assert.Equal("C:\\Program Files\\App\\AppUninstaller.exe", result.ExecutablePath);
        Assert.Equal("/silent /cleanup", result.Arguments);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Parse_MissingExecutable_ReturnsMissing()
    {
        _fileSystemMock.Setup(fs => fs.FileExists("C:\\App\\Missing.exe")).Returns(false);
        var app = new Application { UninstallCommand = "\"C:\\App\\Missing.exe\" /S" };
        var result = _parser.Parse(app);
        
        Assert.Equal(ExecutionType.Missing, result.ExecutionType);
        Assert.False(result.IsValid);
    }
}
