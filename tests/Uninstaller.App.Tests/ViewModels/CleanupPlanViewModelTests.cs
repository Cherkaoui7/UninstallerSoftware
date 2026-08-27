using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Uninstaller.App.ViewModels;
using Uninstaller.App.Services;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.App.Tests.ViewModels
{
    public class CleanupPlanViewModelTests
    {
        private readonly Mock<INavigationService> _navMock;
        private readonly Mock<ICleanupTransactionEngine> _engineMock;
        private readonly Mock<IErrorBoundaryService> _errorMock;
        private readonly Application _app;

        public CleanupPlanViewModelTests()
        {
            _navMock = new Mock<INavigationService>();
            _engineMock = new Mock<ICleanupTransactionEngine>();
            _errorMock = new Mock<IErrorBoundaryService>();
            _app = new Application { Id = Guid.NewGuid(), Name = "TestApp", Version = "1.0", Publisher = "TestPub" };
        }

        private CleanupPlan CreatePlan(params CleanupPlanItem[] items)
        {
            return new CleanupPlan
            {
                Id = Guid.NewGuid(),
                ApplicationId = _app.Id,
                Items = items.ToList()
            };
        }

        [Fact]
        public void Constructor_InitializesSummaries_Correctly()
        {
            var item1 = new CleanupPlanItem { Id = Guid.NewGuid(), Recommended = true, Classification = ArtifactClassification.ApplicationOwned, RiskLevel = RiskLevel.Low };
            var item2 = new CleanupPlanItem { Id = Guid.NewGuid(), Recommended = false, IsProtected = true, Classification = ArtifactClassification.UserData, RiskLevel = RiskLevel.Blocked };
            
            var plan = CreatePlan(item1, item2);
            plan.Warnings.Add("Warning 1");
            var vm = new CleanupPlanViewModel(plan, _app, _navMock.Object, _engineMock.Object, _errorMock.Object);

            Assert.Equal(2, vm.TotalArtifacts);
            Assert.Equal(1, vm.RecommendedArtifacts);
            Assert.Equal(1, vm.ProtectedArtifacts);
            Assert.Equal(1, vm.UserDataArtifacts);
            Assert.Equal(1, vm.WarningsCount);
            Assert.Equal("Low", vm.OverallRisk);
            Assert.Equal("TestApp", vm.ApplicationName);
            Assert.True(vm.HasItems);
            Assert.True(vm.HasExecutableItems);
        }

        [Fact]
        public void RecommendedItem_IsInitiallySelected()
        {
            var item = new CleanupPlanItem { Recommended = true, Classification = ArtifactClassification.ApplicationOwned };
            var vm = new CleanupPlanViewModel(CreatePlan(item), _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            
            Assert.True(vm.Items[0].IsSelected);
            Assert.Equal(1, vm.SelectedArtifacts);
        }

        [Fact]
        public void NonRecommendedItem_IsInitiallyUnselected()
        {
            var item = new CleanupPlanItem { Recommended = false, Classification = ArtifactClassification.ApplicationOwned };
            var vm = new CleanupPlanViewModel(CreatePlan(item), _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            
            Assert.False(vm.Items[0].IsSelected);
            Assert.Equal(0, vm.SelectedArtifacts);
        }

        [Fact]
        public void ProtectedItem_CannotBeSelected()
        {
            var item = new CleanupPlanItem { Recommended = false, IsProtected = true, Classification = ArtifactClassification.ApplicationOwned };
            var vm = new CleanupPlanViewModel(CreatePlan(item), _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            
            vm.Items[0].IsSelected = true;
            Assert.False(vm.Items[0].IsSelected);
        }

        [Fact]
        public void UserData_CannotBeSelected()
        {
            var item = new CleanupPlanItem { Recommended = false, Classification = ArtifactClassification.UserData };
            var vm = new CleanupPlanViewModel(CreatePlan(item), _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            
            vm.Items[0].IsSelected = true;
            Assert.False(vm.Items[0].IsSelected);
        }

        [Fact]
        public void SharedDependency_CannotBeSelected()
        {
            var item = new CleanupPlanItem { Recommended = false, Classification = ArtifactClassification.SharedDependency };
            var vm = new CleanupPlanViewModel(CreatePlan(item), _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            
            vm.Items[0].IsSelected = true;
            Assert.False(vm.Items[0].IsSelected);
        }

        [Fact]
        public void BlockedItem_CannotBeSelected()
        {
            var item = new CleanupPlanItem { Recommended = false, Classification = ArtifactClassification.ApplicationOwned, RiskLevel = RiskLevel.Blocked };
            var vm = new CleanupPlanViewModel(CreatePlan(item), _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            
            vm.Items[0].IsSelected = true;
            Assert.False(vm.Items[0].IsSelected);
        }

        [Fact]
        public void DeselectRecommendedItem_UpdatesSummary()
        {
            var item = new CleanupPlanItem { Recommended = true, Classification = ArtifactClassification.ApplicationOwned };
            var vm = new CleanupPlanViewModel(CreatePlan(item), _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            
            Assert.Equal(1, vm.SelectedArtifacts);
            vm.Items[0].IsSelected = false;
            Assert.Equal(0, vm.SelectedArtifacts);
        }

        [Fact]
        public void ZeroSelection_ConfirmationBlocked()
        {
            var item = new CleanupPlanItem { Recommended = false, Classification = ArtifactClassification.ApplicationOwned };
            var vm = new CleanupPlanViewModel(CreatePlan(item), _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            
            vm.ReviewCleanup();
            Assert.False(vm.IsConfirmationVisible);
            Assert.Equal("No items selected for cleanup.", vm.StatusMessage);
        }

        [Fact]
        public async Task ExecuteCleanup_PassesExactSelectedIdsToEngine()
        {
            var item1 = new CleanupPlanItem { Id = Guid.NewGuid(), Recommended = true, Classification = ArtifactClassification.ApplicationOwned };
            var item2 = new CleanupPlanItem { Id = Guid.NewGuid(), Recommended = false, Classification = ArtifactClassification.ApplicationOwned };
            var plan = CreatePlan(item1, item2);
            var vm = new CleanupPlanViewModel(plan, _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            
            vm.ReviewCleanup();
            
            _engineMock.Setup(e => e.ExecuteAsync(plan, _app, It.Is<IEnumerable<Guid>>(l => l.Count() == 1 && l.First() == item1.Id), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CleanupSessionResult { Status = CleanupSessionStatus.Completed });

            await vm.ExecuteCleanupAsync();
            
            _engineMock.VerifyAll();
            Assert.Equal(Uninstaller.App.Enums.UIState.Success, vm.State);
        }

        [Fact]
        public async Task ExecuteCleanup_StalePlan_ShowsErrorSafely()
        {
            var item = new CleanupPlanItem { Id = Guid.NewGuid(), Recommended = true, Classification = ArtifactClassification.ApplicationOwned };
            var plan = CreatePlan(item);
            var vm = new CleanupPlanViewModel(plan, _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            
            _engineMock.Setup(e => e.ExecuteAsync(plan, _app, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CleanupSessionResult { Status = CleanupSessionStatus.Failed });

            await vm.ExecuteCleanupAsync();
            
            Assert.Equal(Uninstaller.App.Enums.UIState.Error, vm.State);
            Assert.Contains("Failed", vm.ErrorMessage);
        }

        [Fact]
        public void EmptyPlan_HasExecutableItems_IsFalse()
        {
            var vm = new CleanupPlanViewModel(CreatePlan(), _app, _navMock.Object, _engineMock.Object, _errorMock.Object);
            Assert.False(vm.HasItems);
            Assert.False(vm.HasExecutableItems);
        }
    }
}
