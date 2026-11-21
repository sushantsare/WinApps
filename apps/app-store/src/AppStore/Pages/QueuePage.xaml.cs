using AppStore.Core.Services;
using AppStore.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AppStore.Pages;

public sealed partial class QueuePage : Page
{
    public QueuePage() => InitializeComponent();

    public QueueViewModel ViewModel => App.QueueVm;

    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel.Cancel((sender as FrameworkElement)?.Tag as OperationItem);

    private void Retry_Click(object sender, RoutedEventArgs e) => ViewModel.Retry((sender as FrameworkElement)?.Tag as OperationItem);

    private void Details_Click(object sender, RoutedEventArgs e) => ViewModel.ShowDetailsCommand.Execute((sender as FrameworkElement)?.Tag as OperationItem);

    private void GoUpdates_Click(object sender, RoutedEventArgs e) => App.Window?.SelectPage("updates");
}
