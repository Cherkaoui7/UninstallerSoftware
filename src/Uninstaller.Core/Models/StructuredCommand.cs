namespace Uninstaller.Core.Models;

public class StructuredCommand
{
    public ExecutionType ExecutionType { get; set; } = ExecutionType.Missing;
    public string? ExecutablePath { get; set; }
    public string? Arguments { get; set; }
    public bool RequiresElevation { get; set; }
    public string? OriginalCommand { get; set; }
    public bool IsValid => ExecutionType != ExecutionType.Missing && ExecutionType != ExecutionType.Unknown && !string.IsNullOrWhiteSpace(ExecutablePath);
}
