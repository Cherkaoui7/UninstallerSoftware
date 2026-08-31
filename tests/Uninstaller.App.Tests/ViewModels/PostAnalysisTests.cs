using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.App.ViewModels;
using Uninstaller.App.Services;
using Uninstaller.Core.Abstractions;
using Xunit;

namespace Uninstaller.App.Tests.ViewModels;

public class PostAnalysisTests
{
    [Fact]
    public void TestActivatorUtilities()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<INavigationService>().Object);
        services.AddSingleton(new Mock<IErrorBoundaryService>().Object);
        var provider = services.BuildServiceProvider();

        var plan = new CleanupPlan 
        { 
            Id = Guid.NewGuid(), 
            UninstallSessionId = Guid.NewGuid(), 
            ApplicationId = Guid.NewGuid(),
            Status = CleanupPlanStatus.Generated
        };
        var app = new Application { Id = Guid.NewGuid(), Name = "Test" };

        var vm = ActivatorUtilities.CreateInstance<CleanupPlanViewModel>(provider, plan, app);
        Assert.NotNull(vm);
    }
}
