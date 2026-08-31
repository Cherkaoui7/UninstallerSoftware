using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class CleanupTransactionEngine : ICleanupTransactionEngine
{
    private readonly ICleanupPreflightValidator _preflightValidator;
    private readonly IBackupService _backupService;
    private readonly IExecutorResolver _executorResolver;
    private readonly IItemExecutionTracker _executionTracker;
    private readonly ITransactionJournal _journal;
    private readonly ILogger<CleanupTransactionEngine> _logger;

    public CleanupTransactionEngine(
        ICleanupPreflightValidator preflightValidator,
        IBackupService backupService,
        IExecutorResolver executorResolver,
        IItemExecutionTracker executionTracker,
        ITransactionJournal journal,
        ILogger<CleanupTransactionEngine> logger)
    {
        _preflightValidator = preflightValidator;
        _backupService = backupService;
        _executorResolver = executorResolver;
        _executionTracker = executionTracker;
        _journal = journal;
        _logger = logger;
    }

    public async Task<CleanupSessionResult> ExecuteAsync(
        CleanupPlan plan,
        Application application,
        IEnumerable<Guid> selectedItemIds,
        CancellationToken cancellationToken = default)
    {
        var result = new CleanupSessionResult
        {
            StartedAt = DateTime.UtcNow
        };

        var selectedItems = plan.Items.Where(i => selectedItemIds.Contains(i.Id)).ToList();
        result.ProcessedCount = selectedItems.Count;

        if (selectedItems.Count == 0)
        {
            result.Status = CleanupSessionStatus.Completed;
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }

        foreach (var item in selectedItems)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Cancellation requested before starting item {ItemId}. Stopping session.", item.Id);
                await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Cancelled, CancellationToken.None);
                result.Status = CleanupSessionStatus.Cancelled;
                break;
            }

            var executionResult = new CleanupExecutionResult { ItemId = item.Id };
            await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Pending, cancellationToken);
            
            _logger.LogInformation("Starting preflight validation for item {ItemId}", item.Id);
            await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Validating, cancellationToken);
            var preflightResult = await _preflightValidator.ValidateAsync(item, application, cancellationToken);
            if (preflightResult.Outcome != PreflightValidationOutcome.Authorized)
            {
                _logger.LogWarning("Preflight unauthorized for item {ItemId}: {Outcome}", item.Id, preflightResult.Outcome);
                await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Skipped, cancellationToken);
                executionResult.Outcome = CleanupOutcome.ValidationFailed;
                executionResult.FailureReason = $"Fresh preflight validation failed: {preflightResult.Outcome}";
                result.Results.Add(executionResult);
                result.SkippedCount++;
                continue;
            }
            
            
            await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.PreflightAuthorized, cancellationToken);
            
            if (cancellationToken.IsCancellationRequested)
            {
                await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Cancelled, CancellationToken.None);
                result.Status = CleanupSessionStatus.Cancelled;
                break;
            }

            // 2. Backup
            _logger.LogInformation("Starting backup for item {ItemId}", item.Id);
            await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.BackingUp, cancellationToken);
            var backupResult = await _backupService.BackupArtifactAsync(item, plan.UninstallSessionId, cancellationToken);

            // 3. Verify backup
            var verificationResult = await _backupService.VerifyBackupAsync(backupResult, cancellationToken);
            if (!verificationResult.IsValid)
            {
                _logger.LogError("Backup failed or not verified for item {ItemId}. Status: {Status}", item.Id, backupResult.VerificationStatus);
                await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Failed, cancellationToken);
                executionResult.Outcome = CleanupOutcome.ValidationFailed;
                executionResult.FailureReason = $"Backup was not verified: {verificationResult.FailureReason}";
                result.Results.Add(executionResult);
                result.FailureCount++;
                continue;
            }

            
            await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.BackupVerified, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Cancelled, CancellationToken.None);
                result.Status = CleanupSessionStatus.Cancelled;
                break;
            }

            // 4. Final pre-execution validation
            _logger.LogInformation("Starting final validation for item {ItemId}", item.Id);
            await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.FinalValidating, cancellationToken);
            var finalValidationResult = await _preflightValidator.ValidateAsync(item, application, cancellationToken);
            if (finalValidationResult.Outcome != PreflightValidationOutcome.Authorized)
            {
                _logger.LogError("Final validation unauthorized for item {ItemId}: {Outcome}", item.Id, finalValidationResult.Outcome);
                await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Failed, cancellationToken);
                executionResult.Outcome = CleanupOutcome.ValidationFailed;
                executionResult.FailureReason = $"Final validation failed (stale state): {finalValidationResult.Outcome}";
                result.Results.Add(executionResult);
                result.FailureCount++;
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Cancelled, CancellationToken.None);
                result.Status = CleanupSessionStatus.Cancelled;
                break;
            }

            // 5. Authorized Execution Context
            var context = new AuthorizedExecutionContext
            {
                CleanupPlanItemId = item.Id,
                ApplicationId = plan.ApplicationId,
                CanonicalPath = finalValidationResult.CanonicalPath,
                ArtifactType = item.ArtifactType,
                PreflightOutcomeAuthorized = true,
                BackupId = backupResult.Id,
                BackupVerificationStatus = backupResult.VerificationStatus,
                ExpectedShortcutTarget = finalValidationResult.ExpectedShortcutTarget,
                ExpectedRegistryHive = finalValidationResult.ExpectedRegistryHive,
                ExpectedRegistryKeyPath = finalValidationResult.ExpectedRegistryKeyPath,
                ExpectedRootPath = finalValidationResult.ExpectedRootPath,
                CreatedAt = DateTime.UtcNow
            };

            // 6. Resolve Executor
            var executor = _executorResolver.Resolve(item.ArtifactType);
            if (executor == null)
            {
                _logger.LogError("Missing executor for artifact type {ArtifactType} on item {ItemId}", item.ArtifactType, item.Id);
                await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Failed, cancellationToken);
                executionResult.Outcome = CleanupOutcome.ValidationFailed;
                executionResult.FailureReason = $"Unsupported artifact type: {item.ArtifactType}";
                result.Results.Add(executionResult);
                result.FailureCount++;
                continue;
            }

            // 7. Execute
            _logger.LogInformation("Executing item {ItemId} using {ExecutorType}", item.Id, executor.GetType().Name);
            await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Executing, cancellationToken);
            try
            {
                // CRITICAL: We pass CancellationToken.None here.
                // A cancellation arriving during an executor operation must not cause the engine
                // to report Cancelled before recording the actual mutation result.
                // The current operation must finish/verify, then the engine will stop starting additional items.
                var execResult = await executor.ExecuteAsync(context, CancellationToken.None);
                result.Results.Add(execResult);
                
                await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Verifying, CancellationToken.None);

                if (execResult.Success)
                {
                    _logger.LogInformation("Item {ItemId} executed successfully", item.Id);
                    await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Succeeded, CancellationToken.None);
                    result.SuccessCount++;
                }
                else
                {
                    _logger.LogError("Item {ItemId} execution failed: {Outcome} - {Reason}", item.Id, execResult.Outcome, execResult.FailureReason);
                    await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Failed, cancellationToken);
                    result.FailureCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error executing item {ItemId}", item.Id);
                await UpdateStateAsync(plan.UninstallSessionId, item.Id, CleanupItemExecutionState.Failed, cancellationToken);
                executionResult.Outcome = CleanupOutcome.DeleteFailed;
                executionResult.FailureReason = $"Exception during execution: {ex.Message}";
                
                var existingIdx = result.Results.FindIndex(r => r.ItemId == item.Id);
                if (existingIdx >= 0)
                {
                    result.Results[existingIdx] = executionResult;
                }
                else
                {
                    result.Results.Add(executionResult);
                }
                result.FailureCount++;
            }
        }

        if (result.Status != CleanupSessionStatus.Cancelled)
        {
            if (result.FailureCount == 0 && result.SkippedCount == 0 && result.SuccessCount > 0)
            {
                result.Status = CleanupSessionStatus.Completed;
            }
            else if (result.FailureCount > 0 || result.SkippedCount > 0)
            {
                result.Status = CleanupSessionStatus.CompletedWithFailures;
            }
            else
            {
                result.Status = result.SuccessCount > 0 ? CleanupSessionStatus.Completed : CleanupSessionStatus.CompletedWithFailures;
            }
        }

        result.CompletedAt = DateTime.UtcNow;
        return result;
    }

    private async Task UpdateStateAsync(Guid sessionId, Guid itemId, CleanupItemExecutionState state, CancellationToken cancellationToken)
    {
        await _journal.RecordStateAsync(sessionId, itemId, TransactionType.Cleanup, state.ToString(), cancellationToken);
        await _executionTracker.UpdateStateAsync(itemId, state);
    }
}
