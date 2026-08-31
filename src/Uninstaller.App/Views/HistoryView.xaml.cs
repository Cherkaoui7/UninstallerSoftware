using System.Windows.Controls;
using Serilog;

namespace Uninstaller.App.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        DataContextChanged += (s, e) =>
        {
            Log.Information("[Navigation] HistoryView DataContext assigned to {DataContextType}", e.NewValue?.GetType().Name);
        };
    }
}
