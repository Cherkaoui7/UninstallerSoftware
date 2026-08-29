using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Uninstaller.Core.Abstractions;
using Uninstaller.Infrastructure.Persistence;
using Uninstaller.Infrastructure.Persistence.Repositories;

namespace Uninstaller.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IUninstallSessionRepository, UninstallSessionRepository>();
        services.AddScoped<IHistoryRepository, HistoryRepository>();
        services.AddScoped<ITransactionJournal, TransactionJournal>();
        services.AddScoped<IReconciliationRepository, ReconciliationRepository>();

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
