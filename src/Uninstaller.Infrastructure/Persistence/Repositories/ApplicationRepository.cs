using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Infrastructure.Persistence.Repositories;

public class ApplicationRepository : IApplicationRepository
{
    private readonly AppDbContext _dbContext;
    private readonly IApplicationDeduplicator _deduplicator;
    private readonly ILogger<ApplicationRepository> _logger;

    public ApplicationRepository(AppDbContext dbContext, IApplicationDeduplicator deduplicator, ILogger<ApplicationRepository> logger)
    {
        _dbContext = dbContext;
        _deduplicator = deduplicator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Application>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Applications.ToListAsync(cancellationToken);
    }

    public async Task<Application?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Applications.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task SaveAsync(Application application, CancellationToken cancellationToken)
    {
        if (_dbContext.Entry(application).State == EntityState.Detached)
        {
            _dbContext.Applications.Add(application);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Uninstaller.Core.Models.SyncResult> SyncAsync(IEnumerable<Application> discoveredApps, CancellationToken cancellationToken)
    {
        var result = new Uninstaller.Core.Models.SyncResult();
        var existingApps = await _dbContext.Applications.ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;

        // 1. Mark existing as not present
        foreach (var app in existingApps)
        {
            app.IsPresent = false;
        }

        // 2. Prep discovered apps
        var uniqueDiscovered = _deduplicator.Deduplicate(discoveredApps).ToList();
        foreach (var app in uniqueDiscovered)
        {
            app.IsPresent = true;
            app.LastSeen = now;
            app.UpdatedAt = now;
        }

        // 3. Deduplicate discovered apps INTO existing apps
        var combined = new List<Application>(existingApps);
        combined.AddRange(uniqueDiscovered);

        var finalApps = _deduplicator.Deduplicate(combined).ToList();

        // 4. Determine metrics and add new
        foreach (var app in finalApps)
        {
            if (!existingApps.Contains(app))
            {
                result.ApplicationsAdded++;
                _dbContext.Applications.Add(app);
            }
            else
            {
                // It was an existing app. Check if it was updated by looking at EF state or LastSeen
                if (app.LastSeen == now)
                {
                    result.ApplicationsUpdated++;
                }
                else
                {
                    result.ApplicationsUnchanged++;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }
}
