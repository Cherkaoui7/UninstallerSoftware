using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models.History;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Infrastructure.Persistence.Repositories;

public class HistoryRepository : IHistoryRepository
{
    private readonly AppDbContext _context;

    public HistoryRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<HistoryActivity>> GetRecentActivitiesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var activities = new List<HistoryActivity>();

        // 1. Discoveries (from Applications)
        var apps = await _context.Applications.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        activities.AddRange(apps.Select(a => new HistoryActivity
        {
            SessionId = a.Id, // Using App Id for lack of SessionId
            ApplicationId = a.Id,
            ApplicationName = a.Name,
            ActivityType = ActivityType.Discovery,
            Status = "Completed",
            Timestamp = a.CreatedAt,
            TotalItems = 1,
            SuccessfulItems = 1
        }));

        // 2. Official Uninstalls
        var sessions = await _context.UninstallSessions.AsNoTracking()
            .Include(s => _context.Applications.FirstOrDefault(a => a.Id == s.ApplicationId))
            .OrderByDescending(s => s.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        // Fetch application names manually due to EF limitations on left joins if Nav property missing
        var appIds = sessions.Select(s => s.ApplicationId).Distinct().ToList();
        var appsDict = await _context.Applications.AsNoTracking()
            .Where(a => appIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        activities.AddRange(sessions.Select(s => new HistoryActivity
        {
            SessionId = s.Id,
            ApplicationId = s.ApplicationId,
            ApplicationName = appsDict.TryGetValue(s.ApplicationId, out var name) ? name : "Unknown",
            ActivityType = ActivityType.OfficialUninstall,
            Status = s.Status.ToString(),
            Timestamp = s.StartedAt,
            TotalItems = 1,
            SuccessfulItems = s.Status == UninstallSessionStatus.Completed ? 1 : 0,
            FailedItems = s.Status == UninstallSessionStatus.Failed ? 1 : 0
        }));

        // 3. Cleanup Plans (Cleanup Activity)
        var plans = await _context.CleanupPlans.AsNoTracking()
            .Include(p => p.Summary)
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var planAppIds = plans.Select(p => p.ApplicationId).Distinct().ToList();
        var planAppsDict = await _context.Applications.AsNoTracking()
            .Where(a => planAppIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        activities.AddRange(plans.Select(p => new HistoryActivity
        {
            SessionId = p.UninstallSessionId, // Plan links to UninstallSession
            ApplicationId = p.ApplicationId,
            ApplicationName = planAppsDict.TryGetValue(p.ApplicationId, out var name) ? name : "Unknown",
            ActivityType = ActivityType.Cleanup,
            Status = p.Status.ToString(),
            Timestamp = p.CreatedAt,
            TotalItems = p.Summary.TotalArtifacts,
            SuccessfulItems = 0, // We don't have success counts in DB easily without querying Operations
            FailedItems = 0 // Approximate for dashboard
        }));

        // 4. Recovery (derive from Operations with Restore types)
        var recoveryOps = await _context.Operations.AsNoTracking()
            .Where(o => o.OperationType == OperationType.RestoreFile || o.OperationType == OperationType.RestoreRegistryKey)
            .GroupBy(o => o.SessionId)
            .Select(g => new { SessionId = g.Key, Timestamp = g.Min(o => o.StartedAt), Total = g.Count() })
            .OrderByDescending(g => g.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        // Map recovery sessions back to applications (Operations belong to UninstallSessions)
        var recoverySessionIds = recoveryOps.Select(r => r.SessionId).ToList();
        var recoverySessions = await _context.UninstallSessions.AsNoTracking()
            .Where(s => recoverySessionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.ApplicationId, cancellationToken);
            
        var recoveryAppIds = recoverySessions.Values.Distinct().ToList();
        var recoveryAppsDict = await _context.Applications.AsNoTracking()
            .Where(a => recoveryAppIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        activities.AddRange(recoveryOps.Select(r =>
        {
            var appId = recoverySessions.TryGetValue(r.SessionId, out var aId) ? aId : Guid.Empty;
            return new HistoryActivity
            {
                SessionId = r.SessionId,
                ApplicationId = appId,
                ApplicationName = recoveryAppsDict.TryGetValue(appId, out var name) ? name : "Unknown",
                ActivityType = ActivityType.Recovery,
                Status = "Completed", // Assumed if we have records
                Timestamp = r.Timestamp,
                TotalItems = r.Total
            };
        }));

        return activities.OrderByDescending(a => a.Timestamp ?? DateTime.MinValue).Take(limit).ToList();
    }

    public async Task<IReadOnlyList<TimelineEvent>> GetApplicationTimelineAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var events = new List<TimelineEvent>();

        // 1. Discovery
        var app = await _context.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (app != null)
        {
            events.Add(new TimelineEvent
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                ActivityType = ActivityType.Discovery,
                Timestamp = app.CreatedAt,
                Status = "Completed",
                Title = "Application Discovered",
                Description = $"Found {app.Name} on the system."
            });
        }

        // 2. Uninstall Sessions
        var sessions = await _context.UninstallSessions.AsNoTracking()
            .Where(s => s.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);

        foreach (var s in sessions)
        {
            events.Add(new TimelineEvent
            {
                Id = s.Id,
                ApplicationId = applicationId,
                ActivityType = ActivityType.OfficialUninstall,
                Timestamp = s.StartedAt,
                Status = s.Status.ToString(),
                Title = "Official Uninstall",
                Description = $"Session {s.Id}",
                RelatedSessionId = s.Id
            });

            // Residual Analysis (Artifacts discovered)
            var artifacts = await _context.Artifacts.AsNoTracking()
                .Where(a => a.SessionId == s.Id)
                .ToListAsync(cancellationToken);

            if (artifacts.Any())
            {
                events.Add(new TimelineEvent
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = applicationId,
                    ActivityType = ActivityType.ResidualAnalysis,
                    Timestamp = artifacts.Min(a => a.DiscoveredAt),
                    Status = "Completed",
                    Title = "Residual Analysis",
                    Description = $"Found {artifacts.Count} artifacts.",
                    RelatedSessionId = s.Id
                });
            }

            // Cleanup Plans
            var plan = await _context.CleanupPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UninstallSessionId == s.Id, cancellationToken);

            if (plan != null)
            {
                events.Add(new TimelineEvent
                {
                    Id = plan.Id,
                    ApplicationId = applicationId,
                    ActivityType = ActivityType.Cleanup,
                    Timestamp = plan.CreatedAt,
                    Status = plan.Status.ToString(),
                    Title = "Cleanup Plan",
                    Description = $"Plan generated for session.",
                    RelatedSessionId = s.Id
                });
            }
            
            // Recovery Operations
            var recoveryOpsCount = await _context.Operations.AsNoTracking()
                .CountAsync(o => o.SessionId == s.Id && (o.OperationType == OperationType.RestoreFile || o.OperationType == OperationType.RestoreRegistryKey), cancellationToken);

            if (recoveryOpsCount > 0)
            {
                events.Add(new TimelineEvent
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = applicationId,
                    ActivityType = ActivityType.Recovery,
                    Timestamp = DateTime.UtcNow, // Approximation, fetch min StartedAt if needed
                    Status = "Completed",
                    Title = "Recovery Executed",
                    Description = $"Recovered {recoveryOpsCount} items.",
                    RelatedSessionId = s.Id
                });
            }
        }

        return events.OrderBy(e => e.Timestamp ?? DateTime.MinValue).ToList();
    }

    public async Task<HistoryActivity?> GetCleanupSessionDetailsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var plan = await _context.CleanupPlans.AsNoTracking()
            .Include(p => p.Summary)
            .FirstOrDefaultAsync(p => p.UninstallSessionId == sessionId, cancellationToken);
            
        if (plan == null) return null;

        var app = await _context.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == plan.ApplicationId, cancellationToken);

        var ops = await _context.Operations.AsNoTracking()
            .Where(o => o.SessionId == sessionId && (o.OperationType == OperationType.DeleteFile || o.OperationType == OperationType.DeleteDirectory || o.OperationType == OperationType.DeleteRegistryKey))
            .ToListAsync(cancellationToken);

        return new HistoryActivity
        {
            SessionId = sessionId,
            ApplicationId = plan.ApplicationId,
            ApplicationName = app?.Name ?? "Unknown",
            ActivityType = ActivityType.Cleanup,
            Status = plan.Status.ToString(),
            Timestamp = plan.CreatedAt,
            TotalItems = plan.Summary?.TotalArtifacts ?? 0,
            SuccessfulItems = ops.Count(o => o.Status == "Success" || o.Status == "Completed"),
            FailedItems = ops.Count(o => o.Status == "Failed" || o.Status == "Error"),
            WarningCount = plan.Warnings?.Count ?? 0
        };
    }

    public async Task<HistoryActivity?> GetRecoverySessionDetailsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var ops = await _context.Operations.AsNoTracking()
            .Where(o => o.SessionId == sessionId && (o.OperationType == OperationType.RestoreFile || o.OperationType == OperationType.RestoreRegistryKey))
            .ToListAsync(cancellationToken);

        if (!ops.Any()) return null;

        var session = await _context.UninstallSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        var app = session != null ? await _context.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == session.ApplicationId, cancellationToken) : null;

        return new HistoryActivity
        {
            SessionId = sessionId,
            ApplicationId = session?.ApplicationId ?? Guid.Empty,
            ApplicationName = app?.Name ?? "Unknown",
            ActivityType = ActivityType.Recovery,
            Status = "Completed",
            Timestamp = ops.Min(o => o.StartedAt),
            TotalItems = ops.Count,
            SuccessfulItems = ops.Count(o => o.Status == "Success" || o.Status == "Completed"),
            FailedItems = ops.Count(o => o.Status == "Failed" || o.Status == "Error")
        };
    }

    public async Task<IReadOnlyList<HistoryItemDetail>> GetSessionItemDetailsAsync(Guid sessionId, ActivityType type, CancellationToken cancellationToken = default)
    {
        var details = new List<HistoryItemDetail>();

        if (type == ActivityType.Cleanup)
        {
            var plan = await _context.CleanupPlans.AsNoTracking()
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.UninstallSessionId == sessionId, cancellationToken);

            if (plan == null) return details;

            var ops = await _context.Operations.AsNoTracking()
                .Where(o => o.SessionId == sessionId)
                .ToDictionaryAsync(o => o.ArtifactId, cancellationToken);
                
            var backups = await _context.Backups.AsNoTracking()
                .Where(b => b.SessionId == sessionId)
                .ToDictionaryAsync(b => b.ArtifactId, cancellationToken);

            foreach (var item in plan.Items)
            {
                ops.TryGetValue(item.ArtifactId, out var op);
                backups.TryGetValue(item.ArtifactId, out var backup);

                details.Add(new HistoryItemDetail
                {
                    ItemId = item.Id,
                    ArtifactId = item.ArtifactId,
                    ArtifactType = item.ArtifactType.ToString(),
                    Path = item.Path,
                    Classification = item.Classification.ToString(),
                    ConfidenceScore = item.ConfidenceScore,
                    RiskLevel = item.RiskLevel.ToString(),
                    ExecutionState = op?.Status ?? "Pending",
                    Outcome = op?.Status ?? "None",
                    FailureReason = op?.ErrorMessage ?? "",
                    BackupStatus = backup?.Status.ToString() ?? "None",
                    VerificationStatus = backup?.VerificationStatus.ToString() ?? "None",
                    Timestamp = op?.StartedAt
                });
            }
        }
        else if (type == ActivityType.Recovery)
        {
            var ops = await _context.Operations.AsNoTracking()
                .Where(o => o.SessionId == sessionId && (o.OperationType == OperationType.RestoreFile || o.OperationType == OperationType.RestoreRegistryKey))
                .ToListAsync(cancellationToken);

            var artifactIds = ops.Select(o => o.ArtifactId).ToList();
            var artifacts = await _context.Artifacts.AsNoTracking()
                .Where(a => artifactIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, cancellationToken);

            foreach (var op in ops)
            {
                artifacts.TryGetValue(op.ArtifactId, out var art);
                
                details.Add(new HistoryItemDetail
                {
                    ItemId = op.Id,
                    ArtifactId = op.ArtifactId,
                    ArtifactType = art?.Type.ToString() ?? "Unknown",
                    Path = art?.Path ?? op.SourcePath,
                    Classification = art?.Classification.ToString() ?? "Unknown",
                    ConfidenceScore = art?.ConfidenceScore ?? 0,
                    ExecutionState = op.Status,
                    Outcome = op.Status,
                    FailureReason = op.ErrorMessage ?? "",
                    Timestamp = op.StartedAt
                });
            }
        }

        return details;
    }
}
