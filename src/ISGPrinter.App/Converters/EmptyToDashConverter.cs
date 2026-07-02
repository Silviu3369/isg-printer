using System.Globalization;
using System.Windows.Data;

namespace ISGPrinter.App.Converters;

/// <summary>
/// Renders a muted em dash for null/blank values so detail fields never
/// look empty or broken. One-way only.
/// </summary>
public sealed class EmptyToDashConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null || (value is string text && string.IsNullOrWhiteSpace(text)) ? "—" : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
