using System.Windows;
using System.Windows.Controls;

namespace ISGPrinter.App.Helpers;

/// <summary>
/// Lets a <see cref="PasswordBox"/> two-way bind its password to a view-model
/// string. PasswordBox.Password is not a DependencyProperty, so this attached
/// property bridges it (guarding against the update feedback loop).
/// </summary>
public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(string.Empty, OnBoundPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxHelper));

    public static string GetBoundPassword(DependencyObject element) =>
        (string)element.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject element, string value) =>
        element.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }

        box.PasswordChanged -= HandlePasswordChanged;

        if (!(bool)box.GetValue(IsUpdatingProperty))
        {
            box.Password = e.NewValue as string ?? string.Empty;
        }

        box.PasswordChanged += HandlePasswordChanged;
    }

    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
