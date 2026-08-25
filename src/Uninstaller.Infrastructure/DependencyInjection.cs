using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Uninstaller.Infrastructure.Persistence;

namespace Uninstaller.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbDirectory = Path.Combine(localAppData, "Uninstaller", "Data");
        var logDirectory = Path.Combine(localAppData, "Uninstaller", "Logs");
        
        Directory.CreateDirectory(dbDirectory);
        Directory.CreateDirectory(logDirectory);

        var dbPath = Path.Combine(dbDirectory, "uninstaller.db");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Setup Serilog
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(logDirectory, "log-.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        services.AddLogging(loggingBuilder =>
            loggingBuilder.AddSerilog(dispose: true));

        return services;
    }
}
