using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models;

namespace Uninstaller.Windows.Processes;

public class WindowsProcessExecutor : IProcessExecutor
{
    private readonly ILogger<WindowsProcessExecutor> _logger;

    public WindowsProcessExecutor(ILogger<WindowsProcessExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync(StructuredCommand command, CancellationToken cancellationToken = default)
    {
        if (command == null || !command.IsValid || string.IsNullOrWhiteSpace(command.ExecutablePath))
        {
            throw new ArgumentException("Command is invalid or missing an executable path.", nameof(command));
        }

        var result = new ExecutionResult();

        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            UseShellExecute = command.RequiresElevation, // ShellExecute is required for runas (UAC)
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Normal // Uninstallers usually have UI, even if just a progress bar
        };

        if (command.RequiresElevation)
        {
            startInfo.Verb = "runas";
        }

        if (!string.IsNullOrWhiteSpace(command.Arguments))
        {
            startInfo.Arguments = command.Arguments;
        }

        _logger.LogInformation("Starting execution for {Executable} with arguments {Arguments}", startInfo.FileName, startInfo.Arguments);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        
        try
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.Exited += (sender, args) =>
            {
                tcs.TrySetResult(true);
            };

            // Register cancellation token to set result but not kill the process yet, per rules.
            // "Do not terminate the process automatically yet."
            using var reg = cancellationToken.Register(() => 
            {
                _logger.LogWarning("Execution cancelled for {Executable}", startInfo.FileName);
                tcs.TrySetCanceled(cancellationToken);
            });

            if (!process.Start())
            {
                _logger.LogError("Failed to start process {Executable}.", startInfo.FileName);
                result.ErrorMessage = "Process failed to start.";
                return result;
            }

            try
            {
                result.ProcessId = process.Id;
                result.StartTime = process.StartTime;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not capture process start info.");
                result.StartTime = DateTime.UtcNow;
            }

            await tcs.Task; // Wait for exit or cancellation

            try
            {
                result.ExitCode = process.ExitCode;
                result.EndTime = process.ExitTime;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not capture process exit info.");
                result.EndTime = DateTime.UtcNow;
            }

            _logger.LogInformation("Process {PID} exited with code {ExitCode}.", result.ProcessId, result.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // 1223: The operation was canceled by the user (UAC prompt denied)
            _logger.LogWarning("UAC prompt was canceled by the user.");
            result.ErrorMessage = "Operation canceled by the user (UAC).";
        }
        catch (TaskCanceledException)
        {
            result.ErrorMessage = "Task was canceled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while executing process {Executable}.", startInfo.FileName);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }
}
