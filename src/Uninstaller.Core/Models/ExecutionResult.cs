using System;

namespace Uninstaller.Core.Models;

public class ExecutionResult
{
    public int? ProcessId { get; set; }
    public int? ExitCode { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsSuccess => ExitCode == 0;
}
