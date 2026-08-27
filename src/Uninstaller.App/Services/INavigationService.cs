using CommunityToolkit.Mvvm.ComponentModel;

namespace Uninstaller.App.Services;

public interface INavigationService : System.ComponentModel.INotifyPropertyChanged
{
    ObservableObject? CurrentViewModel { get; }
    void NavigateTo<TViewModel>() where TViewModel : ObservableObject;
}
