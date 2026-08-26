using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Uninstaller.Domain.Entities;

namespace Uninstaller.App.ViewModels;

public partial class ApplicationViewModel : ObservableObject
{
    private readonly Application _application;

    public ApplicationViewModel(Application application)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public Guid Id => _application.Id;
    
    public string Name => _application.Name;
    
    public string? Publisher => _application.Publisher;
    
    public string? Version => _application.Version;
    
    public string? InstallLocation => _application.InstallLocation;
    
    public string RegistrySource => _application.RegistrySource;
    
    public bool IsPresent => _application.IsPresent;
    
    [ObservableProperty]
    private bool _isUninstalling;

    [ObservableProperty]
    private string _uninstallStatus = string.Empty;
    
    public DateTime LastSeen => _application.LastSeen;
    
    public string? Architecture => _application.Architecture;
}
