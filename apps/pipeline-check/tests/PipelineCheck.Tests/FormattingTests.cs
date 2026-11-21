using PipelineCheck.Core;
using Xunit;

namespace PipelineCheck.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData("123456.5", "₹1,23,456.50")]
    [InlineData("0", "₹0.00")]
    [InlineData("-1800", "-₹1,800.00")]
    [InlineData("10000000", "₹1,00,00,000.00")]
    public void Rupees_uses_indian_grouping(string amount, string expected) =>
        Assert.Equal(expected, Formatting.Rupees(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData(6, "Good morning")]
    [InlineData(13, "Good afternoon")]
    [InlineData(21, "Good evening")]
    public void Greeting_by_hour(int hour, string expected) => Assert.Equal(expected, Formatting.Greeting(hour));

    [Fact]
    public void Greeting_rejects_bad_hour() => Assert.Throws<ArgumentOutOfRangeException>(() => Formatting.Greeting(24));

    [Fact]
    public void Increment_updates_count_text()
    {
        var vm = new HomeViewModel();
        vm.IncrementCommand.Execute(null);
        Assert.Equal("Clicked 1 time", vm.CountText);
        vm.IncrementCommand.Execute(null);
        Assert.Equal("Clicked 2 times", vm.CountText);
    }
}
