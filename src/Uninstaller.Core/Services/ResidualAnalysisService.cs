using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class ResidualAnalysisService : IResidualAnalysisService
{
    private readonly IEnumerable<IResidualScanner> _scanners;
    private readonly IEvidenceEngine _evidenceEngine;
    private readonly ICleanupPlanGenerator _planGenerator;
    private readonly ILogger<ResidualAnalysisService> _logger;

    public ResidualAnalysisService(
        IEnumerable<IResidualScanner> scanners,
        IEvidenceEngine evidenceEngine,
        ICleanupPlanGenerator planGenerator,
        ILogger<ResidualAnalysisService> logger)
    {
        _scanners = scanners;
        _evidenceEngine = evidenceEngine;
        _planGenerator = planGenerator;
        _logger = logger;
    }

    public async Task<ResidualAnalysisSession> RunAnalysisAsync(UninstallSession uninstallSession, Application application, CancellationToken cancellationToken = default)
    {
        var session = new ResidualAnalysisSession
        {
            Id = Guid.NewGuid(),
            UninstallSessionId = uninstallSession.Id,
            Status = ResidualAnalysisStatus.Created,
        };

        try
        {
            if (uninstallSession.Status != UninstallSessionStatus.Completed)
            {
                session.Status = ResidualAnalysisStatus.Failed;
                session.FailureReason = "Cannot run residual analysis on an incomplete uninstall session.";
                return session;
            }

            session.Status = ResidualAnalysisStatus.Scanning;
            session.StartedAt = DateTime.UtcNow;
            _logger.LogInformation("Starting residual analysis for application {AppName} (Session: {SessionId})", application.Name, session.Id);

            var discoveredCandidates = new List<ResidualArtifactCandidate>();

            foreach (var scanner in _scanners)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    _logger.LogInformation("Running scanner {ScannerName}...", scanner.Name);
                    var candidates = await scanner.ScanAsync(application, cancellationToken);
                    discoveredCandidates.AddRange(candidates);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    session.ErrorCount++;
                    _logger.LogError(ex, "Scanner {ScannerName} failed.", scanner.Name);
                }
            }

            var analysisResults = new List<ArtifactAnalysisResult>();
            foreach (var candidate in discoveredCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = _evidenceEngine.Analyze(candidate);
                analysisResults.Add(result);
            }

            session.Plan = _planGenerator.Generate(uninstallSession.Id, application.Id, analysisResults);
            session.ArtifactCount = session.Plan.Items.Count;
            session.Status = ResidualAnalysisStatus.Completed;
            session.CompletedAt = DateTime.UtcNow;
            
            _logger.LogInformation("Completed residual analysis. Found {Count} artifacts with {Errors} errors.", session.ArtifactCount, session.ErrorCount);
        }
        catch (OperationCanceledException)
        {
            session.Status = ResidualAnalysisStatus.Cancelled;
            session.CompletedAt = DateTime.UtcNow;
            session.FailureReason = "Analysis was cancelled by the user.";
            _logger.LogWarning("Residual analysis cancelled.");
        }
        catch (Exception ex)
        {
            session.Status = ResidualAnalysisStatus.Failed;
            session.CompletedAt = DateTime.UtcNow;
            session.FailureReason = ex.Message;
            _logger.LogError(ex, "Residual analysis failed unexpectedly.");
        }

        return session;
    }
}
