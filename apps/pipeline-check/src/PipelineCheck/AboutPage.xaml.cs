using Microsoft.UI.Xaml.Controls;

namespace PipelineCheck;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        var v = typeof(App).Assembly.GetName().Version;
        VersionText.Text = $"Pipeline Check · version {v?.Major}.{v?.Minor}.{v?.Build}";
    }
}
