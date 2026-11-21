using AppStore.Core.Models;
using AppStore.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AppStore.Pages;

public sealed partial class UpdatesPage : Page
{
    private readonly ListDetail _layout;

    public UpdatesPage()
    {
        InitializeComponent();
        _layout = new ListDetail(ListPane, DetailsHost, DetailsPane, DetailsBack);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdatesViewModel.Selected) && List.SelectedItem != ViewModel.Selected)
            {
                List.SelectedItem = ViewModel.Selected;
            }
        };
        Loaded += async (_, _) =>
        {
            List.SelectedItem = ViewModel.Selected;
            await ViewModel.LoadIfNeededAsync().ConfigureAwait(true);
        };
    }

    public UpdatesViewModel ViewModel => App.Updates;

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _layout.Update(e.NewSize.Width);
        CheckAgainText.Visibility = e.NewSize.Width < 560 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is PackageItem p)
        {
            ViewModel.Selected = p;
        }
    }

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PackageItem p)
        {
            ViewModel.Selected = p;
            _layout.Open();
        }
    }

    private void DetailsBack_Click(object sender, RoutedEventArgs e) => List.Focus(FocusState.Programmatic);

    private void List_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var item = (e.OriginalSource as ListViewItem)?.Content as PackageItem ?? ViewModel.Selected;
        if (item is null)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Space when !item.InQueue:
                item.IsChecked = !item.IsChecked;
                e.Handled = true;
                break;
            case VirtualKey.Delete:
                ViewModel.SkipVersionCommand.Execute(item);
                e.Handled = true;
                break;
            case VirtualKey.A when IsCtrlDown():
                ViewModel.ToggleAllCommand.Execute(null);
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    internal static bool IsCtrlDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void SelectAll_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleAll();

    private void InstallHeld_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PackageItem p)
        {
            ViewModel.InstallHeldCommand.Execute(p);
        }
    }

    private void ShowSkipped_Click(object sender, RoutedEventArgs e) => App.Window?.SelectPage("settings");

    private void FindApps_Click(object sender, RoutedEventArgs e) => App.Window?.SelectPage("search");
}
