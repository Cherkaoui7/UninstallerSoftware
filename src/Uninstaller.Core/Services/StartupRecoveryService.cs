using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class StartupRecoveryService : IStartupRecoveryService
{
    private readonly ITransactionJournal _journal;
    private readonly IReconciliationRepository _repository;
    private readonly IFileSystemService _fileSystem;
    private readonly IRegistryService _registry;
    private readonly IBackupService _backupService;
    private readonly ICanonicalPathResolver _pathResolver;
    private readonly ILogger<StartupRecoveryService> _logger;

    public StartupRecoveryService(
        ITransactionJournal journal,
        IReconciliationRepository repository,
        IFileSystemService fileSystem,
        IRegistryService registry,
        IBackupService backupService,
        ICanonicalPathResolver pathResolver,
        ILogger<StartupRecoveryService> logger)
    {
        _journal = journal;
        _repository = repository;
        _fileSystem = fileSystem;
        _registry = registry;
        _backupService = backupService;
        _pathResolver = pathResolver;
        _logger = logger;
    }

    public async Task<bool> ReconcileIncompleteTransactionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking for interrupted transactions...");
        
        var incompleteEntries = await _journal.GetIncompleteTransactionsAsync(cancellationToken);
        var entriesList = incompleteEntries.ToList();
        
        if (!entriesList.Any())
        {
            _logger.LogInformation("No interrupted transactions found.");
            return false;
        }

        _logger.LogWarning("Found {Count} interrupted transactions to reconcile.", entriesList.Count);

        foreach (var entry in entriesList)
        {
            try
            {
                // 1. Mark Interrupted
                await _journal.RecordStateAsync(entry.SessionId, entry.ItemId, entry.TransactionType, "Interrupted", cancellationToken);
                
                // 2. Mark Reconciling
                await _journal.RecordStateAsync(entry.SessionId, entry.ItemId, entry.TransactionType, "Reconciling", cancellationToken);

                // 3. Reconcile based on type
                if (entry.TransactionType == TransactionType.Cleanup)
                {
                    await ReconcileCleanupAsync(entry, cancellationToken);
                }
                else if (entry.TransactionType == TransactionType.Recovery)
                {
                    await ReconcileRecoveryAsync(entry, cancellationToken);
                }
                
                // 4. Mark Reconciled
                await _journal.RecordStateAsync(entry.SessionId, entry.ItemId, entry.TransactionType, "Reconciled", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconcile item {ItemId}", entry.ItemId);
            }
        }

        return true;
    }

    private async Task ReconcileCleanupAsync(TransactionJournalEntry entry, CancellationToken cancellationToken)
    {
        var item = await _repository.GetCleanupItemAsync(entry.ItemId, cancellationToken);
        if (item == null)
        {
            _logger.LogWarning("Cleanup item {ItemId} not found. Cannot reconcile.", entry.ItemId);
            return;
        }

        var exists = CheckArtifactExists(item.ArtifactType, item.Path);

        // For Cleanup:
        // If it doesn't exist, it was deleted successfully -> Succeeded.
        // If it still exists, it wasn't deleted -> Failed (interrupted before finish).
        var newState = exists ? CleanupItemExecutionState.Failed : CleanupItemExecutionState.Succeeded;
        
        _logger.LogInformation("Cleanup item {ItemId} of type {Type} at {Path} exists={Exists}. Marking as {State}.", 
            item.Id, item.ArtifactType, item.Path, exists, newState);

        await _journal.RecordStateAsync(entry.SessionId, entry.ItemId, TransactionType.Cleanup, newState.ToString(), cancellationToken);
    }

    private async Task ReconcileRecoveryAsync(TransactionJournalEntry entry, CancellationToken cancellationToken)
    {
        var backup = await _backupService.GetBackupAsync(entry.ItemId, cancellationToken);
        if (backup == null)
        {
            _logger.LogWarning("Recovery backup {BackupId} not found. Cannot reconcile.", entry.ItemId);
            await _journal.RecordStateAsync(entry.SessionId, entry.ItemId, TransactionType.Recovery, RecoveryItemExecutionState.Failed.ToString(), cancellationToken);
            return;
        }

        var exists = CheckArtifactExists(backup.ArtifactType, backup.OriginalPath);

        // For Recovery:
        // If it exists, it was recovered successfully (or was never deleted) -> Recovered (if hash matches, but for simple existence we assume Recovered)
        // If it doesn't exist, it wasn't recovered -> Failed
        var newState = exists ? RecoveryItemExecutionState.Recovered : RecoveryItemExecutionState.Failed;

        _logger.LogInformation("Recovery item backup {BackupId} of type {Type} at {Path} exists={Exists}. Marking as {State}.", 
            backup.Id, backup.ArtifactType, backup.OriginalPath, exists, newState);

        await _journal.RecordStateAsync(entry.SessionId, entry.ItemId, TransactionType.Recovery, newState.ToString(), cancellationToken);
    }

    private bool CheckArtifactExists(ArtifactType type, string path)
    {
        return type switch
        {
            ArtifactType.File or ArtifactType.Shortcut => _fileSystem.FileExists(path),
            ArtifactType.Directory => _fileSystem.DirectoryExists(path),
            ArtifactType.RegistryKey => _registry.KeyExists(ParseRoot(path), ParseSubKey(path)),
            ArtifactType.RegistryValue => CheckRegistryValueExists(path),
            _ => true
        };
    }

    private bool CheckRegistryValueExists(string path)
    {
        var root = ParseRoot(path);
        var remainder = ParseSubKey(path);
        
        if (remainder.Contains("::"))
        {
            var parts = remainder.Split("::", 2);
            return _registry.ValueExists(root, parts[0], parts[1]);
        }

        var lastSlash = remainder.LastIndexOf('\\');
        if (lastSlash > 0)
        {
            var keyPath = remainder.Substring(0, lastSlash);
            var valueName = remainder.Substring(lastSlash + 1);
            return _registry.ValueExists(root, keyPath, valueName);
        }

        return _registry.ValueExists(root, string.Empty, remainder);
    }

    private string ParseRoot(string path)
    {
        var sep = path.IndexOf('\\');
        return sep > 0 ? path.Substring(0, sep) : path;
    }

    private string ParseSubKey(string path)
    {
        var sep = path.IndexOf('\\');
        return sep > 0 && sep < path.Length - 1 ? path.Substring(sep + 1) : string.Empty;
    }
}
