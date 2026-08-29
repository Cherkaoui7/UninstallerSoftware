using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class RecoveryTransactionEngine : IRecoveryTransactionEngine
{
    private readonly ILogger<RecoveryTransactionEngine> _logger;
    private readonly IBackupService _backupService;
    private readonly IRecoveryExecutorResolver _executorResolver;
    private readonly IRecoveryItemExecutionTracker _executionTracker;
    private readonly ITransactionJournal _journal;

    public RecoveryTransactionEngine(
        ILogger<RecoveryTransactionEngine> logger,
        IBackupService backupService,
        IRecoveryExecutorResolver executorResolver,
        IRecoveryItemExecutionTracker executionTracker,
        ITransactionJournal journal)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _executorResolver = executorResolver ?? throw new ArgumentNullException(nameof(executorResolver));
        _executionTracker = executionTracker ?? throw new ArgumentNullException(nameof(executionTracker));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    public async Task<RecoverySessionResult> ExecuteAsync(
        RecoverySession session,
        Application application,
        CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (application == null) throw new ArgumentNullException(nameof(application));

        var result = new RecoverySessionResult
        {
            RecoverySessionId = session.Id,
            Status = RecoverySessionStatus.Pending,
            TotalItems = session.Items.Count
        };

        foreach (var item in session.Items)
        {
            var executionResult = new RecoveryResult { RecoveryItemId = item.Id };

            if (cancellationToken.IsCancellationRequested)
            {
                await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.Cancelled, CancellationToken.None);
                result.Status = RecoverySessionStatus.Cancelled;
                break;
            }

            // 1. Validating
            await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.Validating, cancellationToken);
            var backup = await _backupService.GetBackupAsync(item.BackupArtifactId, cancellationToken);
            if (backup == null)
            {
                _logger.LogError("Backup {BackupId} not found for recovery item {ItemId}", item.BackupArtifactId, item.Id);
                await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.Failed, cancellationToken);
                executionResult.Outcome = RecoveryOutcome.ValidationFailed;
                executionResult.FailureReason = "Backup artifact not found.";
                result.Results.Add(executionResult);
                result.FailureCount++;
                continue;
            }

            if (backup.ArtifactType != item.ArtifactType)
            {
                _logger.LogError("Backup artifact type mismatch for item {ItemId}", item.Id);
                await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.Failed, cancellationToken);
                executionResult.Outcome = RecoveryOutcome.ValidationFailed;
                executionResult.FailureReason = "Backup artifact type mismatch.";
                result.Results.Add(executionResult);
                result.FailureCount++;
                continue;
            }

            // 2. Verify Backup
            await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.VerifyingBackup, cancellationToken);
            var verificationResult = await _backupService.VerifyBackupAsync(backup, cancellationToken);
            if (!verificationResult.IsValid)
            {
                _logger.LogError("Backup {BackupId} failed verification immediately before restoration for item {ItemId}", item.BackupArtifactId, item.Id);
                await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.Failed, cancellationToken);
                executionResult.Outcome = RecoveryOutcome.BackupInvalid;
                executionResult.FailureReason = $"Backup verification failed: {verificationResult.FailureReason}";
                result.Results.Add(executionResult);
                result.FailureCount++;
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.Cancelled, CancellationToken.None);
                result.Status = RecoverySessionStatus.Cancelled;
                break;
            }

            // 3. Resolve Executor
            var executor = _executorResolver.Resolve(item.ArtifactType);
            if (executor == null)
            {
                _logger.LogError("No executor found for artifact type {ArtifactType}", item.ArtifactType);
                await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.Failed, cancellationToken);
                executionResult.Outcome = RecoveryOutcome.ValidationFailed;
                executionResult.FailureReason = $"No executor found for artifact type {item.ArtifactType}";
                result.Results.Add(executionResult);
                result.FailureCount++;
                continue;
            }

            // 4. Create Recovery Context
            var context = new RecoveryContext
            {
                RecoveryItemId = item.Id,
                BackupId = backup.Id,
                ArtifactType = backup.ArtifactType,
                OriginalCanonicalPath = backup.OriginalPath,
                BackupPath = backup.BackupPath,
                ExpectedHash = backup.Hash,
                ExpectedRegistryHive = backup.ExpectedRegistryHive,
                ExpectedRegistryKeyPath = backup.ExpectedRegistryKeyPath,
                ExpectedShortcutTarget = backup.ExpectedShortcutTarget,
                BackupVerificationResult = verificationResult,
                RecoveryAuthorization = DateTime.UtcNow
            };

            // 5. Restore
            await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.Restoring, cancellationToken);
            try
            {
                var execResult = await executor.ExecuteAsync(context, CancellationToken.None);
                result.Results.Add(execResult);

                if (execResult.Success)
                {
                    await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.Recovered, CancellationToken.None);
                    result.SuccessCount++;
                }
                else
                {
                    var failState = execResult.Outcome == RecoveryOutcome.RecoveryConflict
                        ? RecoveryItemExecutionState.Conflict
                        : RecoveryItemExecutionState.Failed;
                        
                    await UpdateStateAsync(session.Id, item, failState, CancellationToken.None);
                    result.FailureCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error recovering item {ItemId}", item.Id);
                await UpdateStateAsync(session.Id, item, RecoveryItemExecutionState.Failed, cancellationToken);
                executionResult.Outcome = RecoveryOutcome.Failed;
                executionResult.FailureReason = $"Exception during recovery: {ex.Message}";
                result.Results.Add(executionResult);
                result.FailureCount++;
            }
        }

        if (result.Status != RecoverySessionStatus.Cancelled)
        {
            if (result.FailureCount == 0 && result.SkippedCount == 0 && result.SuccessCount > 0)
            {
                result.Status = RecoverySessionStatus.Completed;
            }
            else if (result.FailureCount > 0 || result.SkippedCount > 0)
            {
                result.Status = RecoverySessionStatus.CompletedWithFailures;
            }
            else
            {
                result.Status = RecoverySessionStatus.Completed;
            }
        }

        result.CompletedAt = DateTime.UtcNow;
        return result;
    }

    private async Task UpdateStateAsync(Guid sessionId, RecoveryItem item, RecoveryItemExecutionState state, CancellationToken cancellationToken)
    {
        await _journal.RecordStateAsync(sessionId, item.BackupArtifactId, TransactionType.Recovery, state.ToString(), cancellationToken);
        await _executionTracker.UpdateStateAsync(item.Id, state);
    }
}
