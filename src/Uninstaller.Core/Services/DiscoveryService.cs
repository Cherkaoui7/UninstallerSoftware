using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models;

namespace Uninstaller.Core.Services;

public class DiscoveryService : IDiscoveryService
{
    private readonly IRegistryService _registryService;
    private readonly IApplicationNormalizer _normalizer;
    private readonly IApplicationRepository _repository;
    private readonly ILogger<DiscoveryService> _logger;

    public DiscoveryService(
        IRegistryService registryService,
        IApplicationNormalizer normalizer,
        IApplicationRepository repository,
        ILogger<DiscoveryService> logger)
    {
        _registryService = registryService;
        _normalizer = normalizer;
        _repository = repository;
        _logger = logger;
    }

    public async Task<DiscoveryResult> DiscoverApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new DiscoveryResult
        {
            DiscoveryStartedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Starting application discovery session.");

        try
        {
            // 1. Fetch raw registry entries
            _logger.LogInformation("Invoking registry discovery.");
            var rawEntries = await _registryService.GetUninstallEntriesAsync(cancellationToken);
            
            result.EntriesInspected = rawEntries.Count;

            // 2. Normalize
            _logger.LogInformation("Normalizing discovered entries.");
            var normalizedApps = new List<Uninstaller.Domain.Entities.Application>();
            
            foreach (var raw in rawEntries)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    break;
                }

                try
                {
                    var app = _normalizer.Normalize(raw);
                    if (app != null)
                    {
                        normalizedApps.Add(app);
                    }
                    else
                    {
                        result.EntriesSkipped++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    _logger.LogWarning(ex, "Failed to normalize application entry: {Entry}", raw.DisplayName ?? "Unknown");
                }
            }

            result.ApplicationsDiscovered = normalizedApps.Count;

            if (result.Cancelled)
            {
                _logger.LogWarning("Discovery session was cancelled during normalization.");
                return result;
            }

            // 3. Persist and Deduplicate
            _logger.LogInformation("Syncing {Count} applications to persistence.", normalizedApps.Count);
            
            var syncResult = await _repository.SyncAsync(normalizedApps, cancellationToken);
            
            result.ApplicationsAdded = syncResult.ApplicationsAdded;
            result.ApplicationsUpdated = syncResult.ApplicationsUpdated;
            result.ApplicationsUnchanged = syncResult.ApplicationsUnchanged;

            _logger.LogInformation("Discovery sync complete. Added: {Added}, Updated: {Updated}, Unchanged: {Unchanged}", 
                syncResult.ApplicationsAdded, syncResult.ApplicationsUpdated, syncResult.ApplicationsUnchanged);
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
            _logger.LogWarning("Discovery session was cancelled.");
        }
        catch (Exception ex)
        {
            result.Errors++;
            _logger.LogError(ex, "An unexpected error occurred during the discovery session.");
        }
        finally
        {
            CompleteResult(result);
        }

        return result;
    }

    private DiscoveryResult CompleteResult(DiscoveryResult result)
    {
        result.DiscoveryCompletedAt = DateTime.UtcNow;
        return result;
    }
}
