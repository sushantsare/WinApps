using System.Globalization;

namespace PipelineCheck.Core;

/// <summary>Indian formats used across Sushant's apps.</summary>
public static class Formatting
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>₹ with Indian digit grouping, e.g. 123456.5 → "₹1,23,456.50", -1800 → "-₹1,800.00".</summary>
    public static string Rupees(decimal amount)
    {
        var digits = Math.Abs(amount).ToString("#,##0.00", India);
        return amount < 0 ? $"-₹{digits}" : $"₹{digits}";
    }

    /// <summary>Greeting for the home page by hour of day (0–23).</summary>
    public static string Greeting(int hour) => hour switch
    {
        < 0 or > 23 => throw new ArgumentOutOfRangeException(nameof(hour)),
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening",
    };
}
