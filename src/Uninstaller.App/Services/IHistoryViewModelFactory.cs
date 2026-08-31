using System;
using Uninstaller.App.ViewModels;

namespace Uninstaller.App.Services;

public interface IHistoryViewModelFactory
{
    CleanupSessionHistoryViewModel CreateCleanupSessionHistoryViewModel(Guid sessionId);
    RecoverySessionHistoryViewModel CreateRecoverySessionHistoryViewModel(Guid sessionId);
    ApplicationHistoryViewModel CreateApplicationHistoryViewModel(Guid applicationId, string applicationName);
}
