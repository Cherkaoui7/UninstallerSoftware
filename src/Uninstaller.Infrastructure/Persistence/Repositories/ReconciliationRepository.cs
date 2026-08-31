using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Infrastructure.Persistence.Repositories;

public class ReconciliationRepository : IReconciliationRepository
{
    private readonly AppDbContext _context;

    public ReconciliationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CleanupPlanItem?> GetCleanupItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        return await _context.CleanupPlanItems
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
    }

    public async Task<Backup?> GetBackupAsync(Guid backupId, CancellationToken cancellationToken = default)
    {
        return await _context.Backups
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);
    }

    public async Task SaveBackupAsync(Backup backup, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Backups.FirstOrDefaultAsync(b => b.Id == backup.Id, cancellationToken);
        if (existing == null)
        {
            await _context.Backups.AddAsync(backup, cancellationToken);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(backup);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<RecoveryItem?> GetRecoveryItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        // RecoveryItem is transient and not in the DbContext. We will return null or skip it.
        return null;
    }
}
