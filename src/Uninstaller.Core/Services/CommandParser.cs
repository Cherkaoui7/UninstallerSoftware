using System;
using System.Linq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Services;

public class CommandParser : ICommandParser
{
    public StructuredCommand Parse(Application application)
    {
        if (application == null) throw new ArgumentNullException(nameof(application));

        if (string.IsNullOrWhiteSpace(application.UninstallCommand) && string.IsNullOrWhiteSpace(application.QuietUninstallCommand))
        {
            return new StructuredCommand { ExecutionType = ExecutionType.Missing };
        }

        var isQuiet = !string.IsNullOrWhiteSpace(application.QuietUninstallCommand);
        var rawCommand = isQuiet ? application.QuietUninstallCommand! : application.UninstallCommand!;

        var parsed = ParseRawString(rawCommand);
        parsed.OriginalCommand = rawCommand;
        
        if (string.IsNullOrWhiteSpace(parsed.ExecutablePath))
        {
            parsed.ExecutionType = ExecutionType.Unknown;
            return parsed;
        }

        // Security check: Reject powershell and cmd
        if (IsBlockedExecutable(parsed.ExecutablePath))
        {
            parsed.ExecutionType = ExecutionType.Unknown;
            return parsed;
        }

        // Determine ExecutionType
        if (application.IsWindowsInstaller || parsed.ExecutablePath.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            parsed.ExecutionType = ExecutionType.Msi;
        }
        else
        {
            parsed.ExecutionType = isQuiet ? ExecutionType.QuietExecutable : ExecutionType.Executable;
        }

        // Elevation Heuristic
        parsed.RequiresElevation = true;
        if (application.RegistrySource.Contains("CurrentUser", StringComparison.OrdinalIgnoreCase))
        {
            parsed.RequiresElevation = false;
        }
        else if (!string.IsNullOrWhiteSpace(application.InstallLocation) && 
                 application.InstallLocation.Contains("AppData", StringComparison.OrdinalIgnoreCase))
        {
            parsed.RequiresElevation = false;
        }

        return parsed;
    }

    private bool IsBlockedExecutable(string executablePath)
    {
        var exe = executablePath.Trim().ToLowerInvariant();
        if (exe.EndsWith("cmd.exe") || exe.EndsWith("powershell.exe") || exe.EndsWith("pwsh.exe") || exe.EndsWith("wscript.exe") || exe.EndsWith("cscript.exe"))
        {
            return true;
        }
        return false;
    }

    private StructuredCommand ParseRawString(string command)
    {
        var result = new StructuredCommand();
        command = command.Trim();

        // Check for shell injection characters in the raw command
        if (command.Contains("&") || command.Contains("|") || command.Contains("<") || command.Contains(">"))
        {
            return result; // Return empty (invalid) if we detect obvious shell injection tokens
        }

        if (command.StartsWith("\""))
        {
            var endQuoteIndex = command.IndexOf("\"", 1);
            if (endQuoteIndex > 0)
            {
                result.ExecutablePath = command.Substring(1, endQuoteIndex - 1);
                if (endQuoteIndex + 1 < command.Length)
                {
                    result.Arguments = command.Substring(endQuoteIndex + 1).Trim();
                }
            }
            else
            {
                // Malformed quoted string, return invalid
                result.ExecutablePath = null;
            }
        }
        else
        {
            // Heuristic to handle unquoted paths with spaces (e.g. C:\Program Files\App\uninstall.exe /S)
            var exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            var msiIndex = command.IndexOf(".msi", StringComparison.OrdinalIgnoreCase);
            
            int extensionIndex = -1;
            int extensionLength = 4;
            
            if (exeIndex > 0)
            {
                extensionIndex = exeIndex;
            }
            else if (msiIndex > 0)
            {
                extensionIndex = msiIndex;
            }

            if (extensionIndex > 0)
            {
                result.ExecutablePath = command.Substring(0, extensionIndex + extensionLength).Trim();
                if (extensionIndex + extensionLength < command.Length)
                {
                    result.Arguments = command.Substring(extensionIndex + extensionLength).Trim();
                }
            }
            else
            {
                // Fallback to first space
                var spaceIndex = command.IndexOf(" ");
                if (spaceIndex > 0)
                {
                    result.ExecutablePath = command.Substring(0, spaceIndex).Trim();
                    result.Arguments = command.Substring(spaceIndex + 1).Trim();
                }
                else
                {
                    result.ExecutablePath = command;
                }
            }
        }

        return result;
    }
}
