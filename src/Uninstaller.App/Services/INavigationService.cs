using CommunityToolkit.Mvvm.ComponentModel;

namespace Uninstaller.App.Services;

public interface INavigationService : System.ComponentModel.INotifyPropertyChanged
{
    ObservableObject? CurrentViewModel { get; }
    TViewModel NavigateTo<TViewModel>() where TViewModel : ObservableObject;
    void NavigateTo(ObservableObject viewModel);
}
