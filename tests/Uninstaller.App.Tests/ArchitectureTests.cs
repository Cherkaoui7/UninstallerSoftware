using System;
using NetArchTest.Rules;
using Uninstaller.App.ViewModels;
using Xunit;

namespace Uninstaller.App.Tests;

public class ArchitectureTests
{
    [Fact]
    public void App_ShouldNotHaveDirectOSAccess()
    {
        var result = Types.InAssembly(typeof(MainViewModel).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.Win32") // No direct registry access
            .GetResult();

        Assert.True(result.IsSuccessful, "App layer must not have direct OS access dependencies.");
    }
}
