using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;
using System.Linq;
using System;

namespace Uninstaller.App.Tests;

public class Phase5ITests
{
    private readonly string _solutionDir;

    public Phase5ITests()
    {
        // Go up from bin/Release/net10.0-windows/ to the root Uninstaller dir
        _solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void Manifest_Exists_And_Requires_AsInvoker()
    {
        var manifestPath = Path.Combine(_solutionDir, "src", "Uninstaller.App", "app.manifest");
        File.Exists(manifestPath).Should().BeTrue("app.manifest should exist to explicitly enforce permissions");

        var doc = XDocument.Load(manifestPath);
        var ns = XNamespace.Get("urn:schemas-microsoft-com:asm.v3");
        
        var execLevel = doc.Descendants(ns + "requestedExecutionLevel").FirstOrDefault();
        execLevel.Should().NotBeNull("manifest should contain requestedExecutionLevel");
        
        execLevel!.Attribute("level")?.Value.Should().Be("asInvoker", "The application must NOT requireAdministrator by default");
        execLevel!.Attribute("uiAccess")?.Value.Should().Be("false");
    }

    [Fact]
    public void CI_Workflow_Contains_No_Hardcoded_Secrets_And_Prepares_Signing()
    {
        var ciPath = Path.Combine(_solutionDir, ".github", "workflows", "ci.yml");
        if (!File.Exists(ciPath))
            return; // Skip if run outside the full repo context

        var content = File.ReadAllText(ciPath);
        
        content.Should().NotContain("password:");
        content.Should().NotContain("secret:");
        content.Should().NotContain("api_key:");
        content.Should().Contain("secrets.CODE_SIGN_CERT", "Signing must use GitHub secrets, not hardcoded files");
    }

    [Fact]
    public void Publish_Directory_Does_Not_Contain_Test_Assemblies_Or_Source_Code()
    {
        var publishDir = Path.Combine(_solutionDir, "Publish");
        if (!Directory.Exists(publishDir))
            return; // Only validate if the publish script has actually been run

        var allFiles = Directory.GetFiles(publishDir, "*.*", SearchOption.AllDirectories);
        
        // No tests
        allFiles.Should().NotContain(f => f.EndsWith("Tests.dll", StringComparison.OrdinalIgnoreCase));
        allFiles.Should().NotContain(f => f.Contains("xunit", StringComparison.OrdinalIgnoreCase));
        allFiles.Should().NotContain(f => f.Contains("Moq", StringComparison.OrdinalIgnoreCase));

        // No source code
        allFiles.Should().NotContain(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
    }
}
