using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class UninstallService : IUninstallService
{
    private readonly IUninstallSessionRepository _sessionRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ICommandParser _commandParser;
    private readonly IProcessExecutor _processExecutor;
    private readonly IDiscoveryService _discoveryService;
    private readonly ILogger<UninstallService> _logger;

    public UninstallService(
        IUninstallSessionRepository sessionRepository,
        IApplicationRepository applicationRepository,
        ICommandParser commandParser,
        IProcessExecutor processExecutor,
        IDiscoveryService discoveryService,
        ILogger<UninstallService> logger)
    {
        _sessionRepository = sessionRepository;
        _applicationRepository = applicationRepository;
        _commandParser = commandParser;
        _processExecutor = processExecutor;
        _discoveryService = discoveryService;
        _logger = logger;
    }

    public async Task<UninstallSession> RunUninstallAsync(Application application, CancellationToken cancellationToken = default)
    {
        var session = new UninstallSession
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            Status = UninstallSessionStatus.Created,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            VerificationResult = VerificationResult.Unknown
        };

        await TransitionStateAsync(session, UninstallSessionStatus.Created, cancellationToken);

        try
        {
            // Validating
            await TransitionStateAsync(session, UninstallSessionStatus.Validating, cancellationToken);
            var parsedCommand = _commandParser.Parse(application);

            if (!parsedCommand.IsValid)
            {
                return await FailSessionAsync(session, "Command validation failed. The uninstall command is invalid or missing.", cancellationToken);
            }

            session.Strategy = parsedCommand.ExecutionType.ToString();

            // ReadyToExecute
            await TransitionStateAsync(session, UninstallSessionStatus.ReadyToExecute, cancellationToken);
            
            if (cancellationToken.IsCancellationRequested)
            {
                return await CancelSessionAsync(session, "Cancelled before execution");
            }

            // Executing
            await TransitionStateAsync(session, UninstallSessionStatus.Executing, cancellationToken);
            var executionResult = await _processExecutor.ExecuteAsync(parsedCommand, cancellationToken);

            session.ProcessId = executionResult.ProcessId;
            session.ExitCode = executionResult.ExitCode;
            if (executionResult.StartTime.HasValue)
            {
                session.StartedAt = executionResult.StartTime.Value;
            }

            if (!executionResult.IsSuccess && !executionResult.ExitCode.HasValue && !cancellationToken.IsCancellationRequested)
            {
                // Execution failed (e.g. process startup failure)
                return await FailSessionAsync(session, executionResult.ErrorMessage ?? "Process failed to start.", cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested && !executionResult.IsSuccess)
            {
                return await CancelSessionAsync(session, "Cancelled during execution");
            }

            // ProcessCompleted
            await TransitionStateAsync(session, UninstallSessionStatus.ProcessCompleted, cancellationToken);

            // Verifying
            await TransitionStateAsync(session, UninstallSessionStatus.Verifying, cancellationToken);
            
            if (executionResult.ExitCode != 0)
            {
                session.VerificationResult = VerificationResult.VerificationFailed;
                return await FailSessionAsync(session, $"Verification failed: Process exited with non-zero code {executionResult.ExitCode}.", cancellationToken);
            }

            try
            {
                await _discoveryService.DiscoverApplicationsAsync(cancellationToken);
                
                var refreshedApp = await _applicationRepository.GetByIdAsync(application.Id, cancellationToken);
                var isStillInstalled = refreshedApp != null && refreshedApp.IsPresent;

                if (isStillInstalled)
                {
                    session.VerificationResult = VerificationResult.StillInstalled;
                    return await FailSessionAsync(session, "Application is still installed after uninstallation.", cancellationToken);
                }

                session.VerificationResult = VerificationResult.VerifiedRemoved;
            }
            catch (Exception ex)
            {
                session.VerificationResult = VerificationResult.VerificationFailed;
                _logger.LogWarning(ex, "Failed to run discovery during verification for session {SessionId}", session.Id);
                return await FailSessionAsync(session, $"Discovery verification failed: {ex.Message}", cancellationToken);
            }

            // Completed
            session.Status = UninstallSessionStatus.Completed;
            session.CompletedAt = DateTime.UtcNow;
            await SaveSessionAsync(session, cancellationToken);
            _logger.LogInformation("Uninstall session {SessionId} completed successfully.", session.Id);
        }
        catch (OperationCanceledException)
        {
            await CancelSessionAsync(session, "Operation cancelled");
        }
        catch (Exception ex)
        {
            await FailSessionAsync(session, ex.Message, default); // Use default token to ensure fail state saves
        }

        return session;
    }

    private async Task<UninstallSession> FailSessionAsync(UninstallSession session, string reason, CancellationToken cancellationToken)
    {
        // Safe check for valid transition
        ValidateTransition(session.Status, UninstallSessionStatus.Failed);
        session.Status = UninstallSessionStatus.Failed;
        session.FailureReason = reason;
        session.CompletedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveSessionAsync(session, cancellationToken);
        _logger.LogError("Uninstall session {SessionId} failed: {Reason}", session.Id, reason);
        return session;
    }

    private async Task<UninstallSession> CancelSessionAsync(UninstallSession session, string reason)
    {
        ValidateTransition(session.Status, UninstallSessionStatus.Cancelled);
        session.Status = UninstallSessionStatus.Cancelled;
        session.FailureReason = reason;
        session.CompletedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveSessionAsync(session, default);
        _logger.LogWarning("Uninstall session {SessionId} cancelled: {Reason}", session.Id, reason);
        return session;
    }

    private async Task TransitionStateAsync(UninstallSession session, UninstallSessionStatus newStatus, CancellationToken cancellationToken)
    {
        if (session.Status != UninstallSessionStatus.Created || newStatus != UninstallSessionStatus.Created) 
        {
            ValidateTransition(session.Status, newStatus);
        }
        
        session.Status = newStatus;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveSessionAsync(session, cancellationToken);
        _logger.LogInformation("Uninstall session {SessionId} transitioned to {Status}", session.Id, newStatus);
    }

    private async Task SaveSessionAsync(UninstallSession session, CancellationToken cancellationToken)
    {
        await _sessionRepository.SaveAsync(session, cancellationToken);
    }

    private void ValidateTransition(UninstallSessionStatus current, UninstallSessionStatus next)
    {
        if (current == next) return;

        bool isValid = (current, next) switch
        {
            (_, UninstallSessionStatus.Created) => true,
            (UninstallSessionStatus.Created, UninstallSessionStatus.Validating) => true,
            (UninstallSessionStatus.Validating, UninstallSessionStatus.ReadyToExecute) => true,
            (UninstallSessionStatus.Validating, UninstallSessionStatus.Failed) => true,
            (UninstallSessionStatus.ReadyToExecute, UninstallSessionStatus.Executing) => true,
            (UninstallSessionStatus.ReadyToExecute, UninstallSessionStatus.Cancelled) => true,
            (UninstallSessionStatus.ReadyToExecute, UninstallSessionStatus.Failed) => true,
            (UninstallSessionStatus.Executing, UninstallSessionStatus.ProcessCompleted) => true,
            (UninstallSessionStatus.Executing, UninstallSessionStatus.Failed) => true,
            (UninstallSessionStatus.Executing, UninstallSessionStatus.Cancelled) => true,
            (UninstallSessionStatus.ProcessCompleted, UninstallSessionStatus.Verifying) => true,
            (UninstallSessionStatus.ProcessCompleted, UninstallSessionStatus.Failed) => true,
            (UninstallSessionStatus.Verifying, UninstallSessionStatus.Completed) => true,
            (UninstallSessionStatus.Verifying, UninstallSessionStatus.Failed) => true,
            _ => false
        };

        if (!isValid)
        {
            throw new InvalidOperationException($"Invalid state transition from {current} to {next}");
        }
    }
}
