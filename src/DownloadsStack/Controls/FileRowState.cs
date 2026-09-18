using System.Windows;

namespace DownloadsStack.Controls;

public static class FileRowState
{
    public static readonly DependencyProperty IsContextTargetProperty = DependencyProperty.RegisterAttached(
        "IsContextTarget", typeof(bool), typeof(FileRowState), new PropertyMetadata(false));

    public static bool GetIsContextTarget(DependencyObject row) => (bool)row.GetValue(IsContextTargetProperty);
    public static void SetIsContextTarget(DependencyObject row, bool value) => row.SetValue(IsContextTargetProperty, value);
}
