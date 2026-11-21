using AppStore.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AppStore.Pages;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Refresh();
    }

    public HistoryViewModel ViewModel => App.History;
}
