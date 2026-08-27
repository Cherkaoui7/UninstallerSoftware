using System;
using Serilog;

namespace Uninstaller.App.Services;

public class ErrorBoundaryService : IErrorBoundaryService
{
    public string HandleException(Exception ex, string context = "")
    {
        Log.Error(ex, "UI Exception Boundary caught error in context: {Context}", context);
        
        if (ex is OperationCanceledException)
        {
            return "The operation was cancelled.";
        }
        
        // Convert technical exceptions to user-safe messages
        return $"An unexpected error occurred during {context.ToLowerInvariant()}. Please check the logs for details.";
    }
}
