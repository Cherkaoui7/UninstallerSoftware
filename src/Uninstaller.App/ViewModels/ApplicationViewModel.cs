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
    
    public bool IsPresent
    {
        get => _application.IsPresent;
        set
        {
            if (_application.IsPresent != value)
            {
                _application.IsPresent = value;
                OnPropertyChanged(nameof(IsPresent));
                OnPropertyChanged(nameof(UninstallStatus));
            }
        }
    }
    
    [ObservableProperty]
    private bool _isUninstalling;

    public string UninstallStatus
    {
        get
        {
            if (!string.IsNullOrEmpty(_uninstallStatus)) return _uninstallStatus;
            return IsPresent ? "Installed" : "Uninstalled";
        }
        set
        {
            SetProperty(ref _uninstallStatus, value);
        }
    }
    private string _uninstallStatus = string.Empty;
    
    public DateTime LastSeen => _application.LastSeen;
    
    public string? Architecture => _application.Architecture;
}
