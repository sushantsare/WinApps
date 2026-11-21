using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PipelineCheck.Core;

public partial class HomeViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int ClickCount { get; set; }

    public string CountText => ClickCount == 1 ? "Clicked 1 time" : $"Clicked {ClickCount} times";

    partial void OnClickCountChanged(int value) => OnPropertyChanged(nameof(CountText));

    [RelayCommand]
    private void Increment() => ClickCount++;
}
