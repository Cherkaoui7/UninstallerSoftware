using System.Windows;
using System.Windows.Controls;
using Serilog;

namespace Uninstaller.App.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            Log.Information("[Navigation] HistoryView Loaded event. DataContext={DataContextType} (#{Hash})", 
                DataContext?.GetType().FullName, DataContext?.GetHashCode());
        };
        DataContextChanged += (s, e) =>
        {
            Log.Information("[Navigation] HistoryView DataContext assigned to {DataContextType} (#{Hash})", 
                e.NewValue?.GetType().FullName, e.NewValue?.GetHashCode());
        };
    }
}
