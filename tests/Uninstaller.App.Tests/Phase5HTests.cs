using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Uninstaller.App.Tests;

public class Phase5HTests
{
    [Fact]
    public void Versioning_Metadata_Is_Consistent()
    {
        var assembly = typeof(Uninstaller.App.App).Assembly;
        
        var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
        var assemblyVersion = assembly.GetName().Version?.ToString();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        assemblyVersion.Should().NotBeNullOrEmpty();
        fileVersionInfo.FileVersion.Should().NotBeNullOrEmpty();
        informationalVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Directory_Build_Props_Exists_And_Contains_Version_Info()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var buildPropsPath = Path.Combine(projectRoot, "Directory.Build.props");
        
        File.Exists(buildPropsPath).Should().BeTrue("Directory.Build.props should be centralized in the project root.");
        
        var xml = XDocument.Load(buildPropsPath);
        var propertyGroup = xml.Root?.Element("PropertyGroup");
        
        propertyGroup.Should().NotBeNull();
        propertyGroup?.Element("Version").Should().NotBeNull();
        propertyGroup?.Element("AssemblyVersion").Should().NotBeNull();
        propertyGroup?.Element("FileVersion").Should().NotBeNull();
        propertyGroup?.Element("InformationalVersion").Should().NotBeNull();
    }
    
    [Fact]
    public void Publish_Script_Exists_And_Contains_Safe_Configuration()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var publishScriptPath = Path.Combine(projectRoot, "scripts", "Publish.ps1");
        
        File.Exists(publishScriptPath).Should().BeTrue("Publish.ps1 must exist.");
        
        var scriptContent = File.ReadAllText(publishScriptPath);
        scriptContent.Should().Contain("--self-contained true");
        scriptContent.Should().Contain("--runtime $RuntimeIdentifier");
        scriptContent.Should().Contain("--configuration $Configuration");
    }

    [Fact]
    public void Installer_Script_Exists_And_Has_No_Destructive_Commands()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var installerScriptPath = Path.Combine(projectRoot, "installer", "Uninstaller.iss");
        
        File.Exists(installerScriptPath).Should().BeTrue("Uninstaller.iss must exist.");
        
        var scriptContent = File.ReadAllText(installerScriptPath);
        scriptContent.Should().NotContain("CleanupTransactionEngine", "Installer should not invoke cleanup logic");
        scriptContent.Should().NotContain("RecoveryTransactionEngine", "Installer should not invoke recovery logic");
        scriptContent.Should().Contain("PrivilegesRequired=admin", "Installer must write to Program Files");
        scriptContent.Should().Contain("{autopf}", "Installer must default to Program Files");
    }
}
