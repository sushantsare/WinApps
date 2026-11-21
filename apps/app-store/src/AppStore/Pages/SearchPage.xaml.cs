using AppStore.Core.Models;
using AppStore.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AppStore.Pages;

public sealed partial class SearchPage : Page
{
    private readonly ListDetail _layout;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };

    public SearchPage()
    {
        InitializeComponent();
        _layout = new ListDetail(ListPane, DetailsHost, DetailsPane, DetailsBack);
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await ViewModel.SearchAsync().ConfigureAwait(true);
        };
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchViewModel.Selected) && List.SelectedItem != ViewModel.Selected)
            {
                List.SelectedItem = ViewModel.Selected;
            }
        };
        Loaded += (_, _) => SearchBox.Focus(FocusState.Programmatic);
    }

    public SearchViewModel ViewModel => App.Search;

    public void FocusSearch() => SearchBox.Focus(FocusState.Keyboard);

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => _layout.Update(e.NewSize.Width);

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _debounce.Stop();
            if (sender.Text.Trim().Length >= 2 || sender.Text.Length == 0)
            {
                _debounce.Start();
            }
        }
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _debounce.Stop();
        ViewModel.Query = args.QueryText;
        await ViewModel.SearchAsync().ConfigureAwait(true);
    }

    private async void Recent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string text)
        {
            await ViewModel.SearchRecentAsync(text).ConfigureAwait(true);
        }
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
        if (item is not null && e.Key == VirtualKey.Space && !item.IsInstalled && !item.InQueue)
        {
            item.IsChecked = !item.IsChecked;
            e.Handled = true;
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PackageItem p)
        {
            ViewModel.InstallOneCommand.Execute(p);
        }
    }

    private void InstallSelectedApp_Click(object sender, RoutedEventArgs e) => ViewModel.InstallOneCommand.Execute(ViewModel.Selected);

    private void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.ChosenVersion = VersionBox.SelectedItem as string ?? string.Empty;
}
