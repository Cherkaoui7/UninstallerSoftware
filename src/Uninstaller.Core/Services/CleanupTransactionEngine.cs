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
    private readonly ILogger<CleanupTransactionEngine> _logger;

    public CleanupTransactionEngine(
        ICleanupPreflightValidator preflightValidator,
        IBackupService backupService,
        IExecutorResolver executorResolver,
        IItemExecutionTracker executionTracker,
        ILogger<CleanupTransactionEngine> logger)
    {
        _preflightValidator = preflightValidator;
        _backupService = backupService;
        _executorResolver = executorResolver;
        _executionTracker = executionTracker;
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
                await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Cancelled);
                result.Status = CleanupSessionStatus.Cancelled;
                break;
            }

            var executionResult = new CleanupExecutionResult { ItemId = item.Id };
            await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Pending);

            // 1. Fresh preflight validation
            _logger.LogInformation("Starting preflight validation for item {ItemId}", item.Id);
            await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Validating);
            var preflightResult = await _preflightValidator.ValidateAsync(item, application, cancellationToken);
            if (preflightResult.Outcome != PreflightValidationOutcome.Authorized)
            {
                _logger.LogWarning("Preflight unauthorized for item {ItemId}: {Outcome}", item.Id, preflightResult.Outcome);
                await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Skipped);
                executionResult.Outcome = CleanupOutcome.ValidationFailed;
                executionResult.FailureReason = $"Fresh preflight validation failed: {preflightResult.Outcome}";
                result.Results.Add(executionResult);
                result.SkippedCount++;
                continue;
            }
            
            
            await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.PreflightAuthorized);
            
            if (cancellationToken.IsCancellationRequested)
            {
                await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Cancelled);
                result.Status = CleanupSessionStatus.Cancelled;
                break;
            }

            // 2. Backup
            _logger.LogInformation("Starting backup for item {ItemId}", item.Id);
            await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.BackingUp);
            var backupResult = await _backupService.BackupArtifactAsync(item, plan.UninstallSessionId, cancellationToken);

            // 3. Verify backup
            var verificationResult = await _backupService.VerifyBackupAsync(backupResult, cancellationToken);
            if (!verificationResult.IsValid)
            {
                _logger.LogError("Backup failed or not verified for item {ItemId}. Status: {Status}", item.Id, backupResult.VerificationStatus);
                await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Failed);
                executionResult.Outcome = CleanupOutcome.ValidationFailed;
                executionResult.FailureReason = $"Backup was not verified: {verificationResult.FailureReason}";
                result.Results.Add(executionResult);
                result.FailureCount++;
                continue;
            }

            
            await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.BackupVerified);

            if (cancellationToken.IsCancellationRequested)
            {
                await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Cancelled);
                result.Status = CleanupSessionStatus.Cancelled;
                break;
            }

            // 4. Final pre-execution validation
            _logger.LogInformation("Starting final validation for item {ItemId}", item.Id);
            await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.FinalValidating);
            var finalValidationResult = await _preflightValidator.ValidateAsync(item, application, cancellationToken);
            if (finalValidationResult.Outcome != PreflightValidationOutcome.Authorized)
            {
                _logger.LogError("Final validation unauthorized for item {ItemId}: {Outcome}", item.Id, finalValidationResult.Outcome);
                await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Failed);
                executionResult.Outcome = CleanupOutcome.ValidationFailed;
                executionResult.FailureReason = $"Final validation failed (stale state): {finalValidationResult.Outcome}";
                result.Results.Add(executionResult);
                result.FailureCount++;
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Cancelled);
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
                await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Failed);
                executionResult.Outcome = CleanupOutcome.ValidationFailed;
                executionResult.FailureReason = $"Unsupported artifact type: {item.ArtifactType}";
                result.Results.Add(executionResult);
                result.FailureCount++;
                continue;
            }

            // 7. Execute
            _logger.LogInformation("Executing item {ItemId} using {ExecutorType}", item.Id, executor.GetType().Name);
            await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Executing);
            try
            {
                // CRITICAL: We pass CancellationToken.None here.
                // A cancellation arriving during an executor operation must not cause the engine
                // to report Cancelled before recording the actual mutation result.
                // The current operation must finish/verify, then the engine will stop starting additional items.
                var execResult = await executor.ExecuteAsync(context, CancellationToken.None);
                result.Results.Add(execResult);
                
                await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Verifying);

                if (execResult.Success)
                {
                    _logger.LogInformation("Item {ItemId} executed successfully", item.Id);
                    await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Succeeded);
                    result.SuccessCount++;
                }
                else
                {
                    _logger.LogError("Item {ItemId} execution failed: {Outcome} - {Reason}", item.Id, execResult.Outcome, execResult.FailureReason);
                    await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Failed);
                    result.FailureCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error executing item {ItemId}", item.Id);
                await _executionTracker.UpdateStateAsync(item.Id, CleanupItemExecutionState.Failed);
                executionResult.Outcome = CleanupOutcome.DeleteFailed;
                executionResult.FailureReason = $"Exception during execution: {ex.Message}";
                result.Results.Add(executionResult);
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
                result.Status = CleanupSessionStatus.Completed; // Edge case (all skipped/failed covered)
            }
        }

        result.CompletedAt = DateTime.UtcNow;
        return result;
    }
}
