using NetArchTest.Rules;
using Uninstaller.Core.Abstractions;
using Xunit;

namespace Uninstaller.Core.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Core_ShouldNotHaveDependenciesOnInfrastructureOrWindowsOrApp()
    {
        var result = Types.InAssembly(typeof(IApplicationRepository).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Uninstaller.Infrastructure", "Uninstaller.Windows", "Uninstaller.App", "Microsoft.EntityFrameworkCore", "Serilog")
            .GetResult();

        Assert.True(result.IsSuccessful, "Core layer must not have dependencies on Infrastructure, Windows, App, or UI layers.");
    }
}
