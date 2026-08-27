using System;

namespace Uninstaller.App.Services;

public interface IErrorBoundaryService
{
    string HandleException(Exception ex, string context = "");
}
