using System;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Uninstaller.Core.Models;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Services;

public class ApplicationNormalizer : IApplicationNormalizer
{
    private readonly ILogger<ApplicationNormalizer> _logger;

    public ApplicationNormalizer(ILogger<ApplicationNormalizer> logger)
    {
        _logger = logger;
    }

    public Application? Normalize(RawRegistryApplication rawApp)
    {
        var name = NormalizeString(rawApp.DisplayName);
        if (string.IsNullOrEmpty(name))
        {
            _logger.LogInformation("Skipping application with missing or empty DisplayName. Key: {Key}", rawApp.RegistryKeyName);
            return null;
        }

        var app = new Application
        {
            Id = Guid.NewGuid(),
            Name = name,
            Version = NormalizeString(rawApp.DisplayVersion),
            Publisher = NormalizeString(rawApp.Publisher),
            InstallLocation = NormalizePath(rawApp.InstallLocation),
            UninstallCommand = NormalizeString(rawApp.UninstallString, preserveCaseAndWhitespace: true),
            QuietUninstallCommand = NormalizeString(rawApp.QuietUninstallString, preserveCaseAndWhitespace: true),
            InstallDate = ParseDate(rawApp.InstallDate),
            EstimatedSize = rawApp.EstimatedSize,
            IsSystemComponent = rawApp.SystemComponent == 1,
            IsWindowsInstaller = rawApp.WindowsInstaller == 1,
            RegistrySource = rawApp.RegistrySource ?? string.Empty,
            RegistryKeyName = rawApp.RegistryKeyName ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };
        
        return app;
    }

    private string? NormalizeString(string? input, bool preserveCaseAndWhitespace = false)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return preserveCaseAndWhitespace ? trimmed : trimmed;
    }

    private string? NormalizePath(string? input)
    {
        var path = NormalizeString(input);
        if (path == null) return null;
        
        path = path.TrimEnd('\\', '/', '"', '\'');
        path = path.TrimStart('"', '\'');
        return path;
    }

    private DateTime? ParseDate(string? input)
    {
        var str = NormalizeString(input);
        if (str == null) return null;

        if (str.Length == 8 && DateTime.TryParseExact(str, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date1))
        {
            return date1;
        }

        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date2))
        {
            return date2;
        }

        return null;
    }
}
