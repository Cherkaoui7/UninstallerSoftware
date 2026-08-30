using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Infrastructure.Persistence.Repositories;

public class UninstallSessionRepository : IUninstallSessionRepository
{
    private readonly AppDbContext _context;

    public UninstallSessionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<UninstallSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.UninstallSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<UninstallSession?> GetLatestByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        return await _context.UninstallSessions
            .Where(s => s.ApplicationId == applicationId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SaveAsync(UninstallSession session, CancellationToken cancellationToken)
    {
        var existing = await _context.UninstallSessions.FindAsync(new object[] { session.Id }, cancellationToken);
        if (existing == null)
        {
            await _context.UninstallSessions.AddAsync(session, cancellationToken);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(session);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
