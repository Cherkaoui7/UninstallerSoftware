using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.Core.Models;
using Uninstaller.Windows.Processes;
using Xunit;

namespace Uninstaller.Windows.Tests.Processes;

public class WindowsProcessExecutorTests
{
    private readonly WindowsProcessExecutor _executor;

    public WindowsProcessExecutorTests()
    {
        _executor = new WindowsProcessExecutor(NullLogger<WindowsProcessExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCommand_ThrowsArgumentException()
    {
        var command = new StructuredCommand { ExecutionType = ExecutionType.Missing };
        await Assert.ThrowsAsync<ArgumentException>(() => _executor.ExecuteAsync(command));
    }

    [Fact]
    public async Task ExecuteAsync_ValidSafeCommand_ReturnsSuccess()
    {
        // Safe process to execute for tests: ping.exe (sends 1 ping and exits quickly)
        var command = new StructuredCommand
        {
            ExecutionType = ExecutionType.Executable,
            ExecutablePath = "ping.exe",
            Arguments = "127.0.0.1 -n 1",
            RequiresElevation = false
        };

        var result = await _executor.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.ProcessId);
        Assert.NotNull(result.StartTime);
        Assert.NotNull(result.EndTime);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_NonExistentExecutable_ReturnsError()
    {
        var command = new StructuredCommand
        {
            ExecutionType = ExecutionType.Executable,
            ExecutablePath = "this_process_should_not_exist_12345.exe",
            RequiresElevation = false
        };

        var result = await _executor.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.Null(result.ProcessId);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationToken_ReturnsCancelledResultWithoutThrowing()
    {
        var command = new StructuredCommand
        {
            ExecutionType = ExecutionType.Executable,
            ExecutablePath = "ping.exe",
            Arguments = "127.0.0.1 -n 10", // Runs for about 10 seconds
            RequiresElevation = false
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)); // Cancel after 500ms

        var result = await _executor.ExecuteAsync(command, cts.Token);

        // Process continues running in background per Phase 2C rules, but Task completes
        Assert.False(result.IsSuccess);
        Assert.Equal("Task was canceled.", result.ErrorMessage);
    }
}
