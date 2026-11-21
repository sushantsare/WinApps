using AppStore.Core.Models;
using AppStore.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AppStore.Pages;

public sealed partial class InstalledPage : Page
{
    private readonly ListDetail _layout;

    public InstalledPage()
    {
        InitializeComponent();
        _layout = new ListDetail(ListPane, DetailsHost, DetailsPane, DetailsBack);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InstalledViewModel.Selected) && List.SelectedItem != ViewModel.Selected)
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

    public InstalledViewModel ViewModel => App.Installed;

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _layout.Update(e.NewSize.Width);
        ExportText.Visibility = e.NewSize.Width < 600 ? Visibility.Collapsed : Visibility.Visible;
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

    private void List_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var item = (e.OriginalSource as ListViewItem)?.Content as PackageItem ?? ViewModel.Selected;
        if (item is null || item.InQueue)
        {
            return;
        }

        if (e.Key == VirtualKey.Space)
        {
            item.IsChecked = !item.IsChecked;
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Delete)
        {
            ViewModel.UninstallOneCommand.Execute(item);
            e.Handled = true;
        }
    }
}
