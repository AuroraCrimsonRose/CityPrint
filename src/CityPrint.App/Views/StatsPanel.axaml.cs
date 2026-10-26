using Avalonia.Controls;
using CityPrint.App.ViewModels;

namespace CityPrint.App.Views;

public partial class StatsPanel : UserControl
{
    public StatsPanel()
    {
        InitializeComponent();
        DataContext = AppState.Current;
    }
}
