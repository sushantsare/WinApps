using Microsoft.UI.Xaml.Controls;
using PipelineCheck.Core;

namespace PipelineCheck;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; } = new();

    public HomePage()
    {
        InitializeComponent();
        Greeting.Text = $"{Formatting.Greeting(DateTime.Now.Hour)}, Sushant";
        Amount.Text = Formatting.Rupees(123456.5m);
    }
}
