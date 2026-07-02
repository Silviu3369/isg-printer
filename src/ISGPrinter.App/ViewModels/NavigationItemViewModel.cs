using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ISGPrinter.App.ViewModels;

public sealed partial class NavigationItemViewModel : ObservableObject
{
    public NavigationItemViewModel(string title, string icon, string accentHex, object pageViewModel)
    {
        Title = title;
        Icon = icon;
        PageViewModel = pageViewModel;

        AccentColor = (Color)ColorConverter.ConvertFromString(accentHex);
        Accent = Frozen(AccentColor);
        // Pale fill for the selected pill body (kept subtle; text stays white).
        AccentTint = Frozen(Color.FromArgb(0x24, AccentColor.R, AccentColor.G, AccentColor.B));
    }

    [ObservableProperty]
    private bool isSelected;

    public string Title { get; }

    public string Icon { get; }

    public object PageViewModel { get; }

    public Color AccentColor { get; }

    public Brush Accent { get; }

    public Brush AccentTint { get; }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
