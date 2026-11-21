using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AppStore.Pages;

/// <summary>
/// List + details layout: side by side when the page is wide; on narrow windows the details open over the
/// list with a Back button (Esc also goes back).
/// </summary>
internal sealed class ListDetail
{
    private const double NarrowBelow = 760;
    private const double WideFrom = 1300;
    private readonly FrameworkElement _list;
    private readonly Grid _detailsHost;
    private readonly FrameworkElement _details;
    private readonly Button _back;
    private bool _narrow;

    public ListDetail(FrameworkElement list, Grid detailsHost, FrameworkElement details, Button back)
    {
        _list = list;
        _detailsHost = detailsHost;
        _details = details;
        _back = back;
        _back.Click += (_, _) => Close();
        _detailsHost.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape && ShowingDetails)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    public bool ShowingDetails { get; private set; }

    public bool IsNarrow => _narrow;

    public void Update(double pageWidth)
    {
        _narrow = pageWidth < NarrowBelow;
        _details.Width = pageWidth >= WideFrom ? 420 : 340;
        if (!_narrow)
        {
            ShowingDetails = false;
        }

        Apply();
    }

    /// <summary>On narrow windows, show the selected item's details full width.</summary>
    public void Open()
    {
        if (_narrow)
        {
            ShowingDetails = true;
            Apply();
            _back.Focus(FocusState.Programmatic);
        }
    }

    public void Close()
    {
        ShowingDetails = false;
        Apply();
    }

    private void Apply()
    {
        if (!_narrow)
        {
            _list.Visibility = Visibility.Visible;
            _detailsHost.Visibility = Visibility.Visible;
            Grid.SetColumn(_detailsHost, 1);
            Grid.SetColumnSpan(_detailsHost, 1);
            _back.Visibility = Visibility.Collapsed;
            return;
        }

        _list.Visibility = ShowingDetails ? Visibility.Collapsed : Visibility.Visible;
        _detailsHost.Visibility = ShowingDetails ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(_detailsHost, 0);
        Grid.SetColumnSpan(_detailsHost, 2);
        if (ShowingDetails)
        {
            _details.Width = double.NaN;
        }

        _back.Visibility = ShowingDetails ? Visibility.Visible : Visibility.Collapsed;
    }
}
