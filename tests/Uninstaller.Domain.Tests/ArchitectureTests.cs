using NetArchTest.Rules;
using Uninstaller.Domain.Entities;
using Xunit;

namespace Uninstaller.Domain.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_ShouldNotHaveDependenciesOnOtherLayers()
    {
        var result = Types.InAssembly(typeof(Application).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Uninstaller.Core", "Uninstaller.Infrastructure", "Uninstaller.Windows", "Uninstaller.App", "Microsoft.EntityFrameworkCore", "Serilog")
            .GetResult();

        Assert.True(result.IsSuccessful, "Domain layer must not have dependencies on other layers or infrastructure frameworks.");
    }
}
