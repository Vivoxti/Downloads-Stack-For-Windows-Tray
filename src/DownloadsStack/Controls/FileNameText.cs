using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DownloadsStack.Controls;

public sealed class FileNameText : TextBlock
{
    private static readonly DependencyPropertyKey BackdropWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(BackdropWidth), typeof(double), typeof(FileNameText), new PropertyMetadata(0d));
    public static readonly DependencyProperty BackdropWidthProperty = BackdropWidthPropertyKey.DependencyProperty;
    public double BackdropWidth => (double)GetValue(BackdropWidthProperty);
    public static readonly DependencyProperty FileNameProperty = DependencyProperty.Register(nameof(FileName), typeof(string), typeof(FileNameText), new PropertyMetadata("", (d, _) => ((FileNameText)d).UpdateText()));
    public string FileName { get => (string)GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }
    public FileNameText() { TextTrimming = TextTrimming.CharacterEllipsis; SizeChanged += (_, _) => UpdateText(); }
    private void UpdateText()
    {
        var name = FileName ?? "";
        if (ActualWidth <= 0) { Text = name; return; }
        double Measure(string text) => new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        void Display(string text)
        {
            Text = text;
            SetValue(BackdropWidthPropertyKey, Math.Min(ActualWidth, Measure(text)) + 16);
        }
        if (Measure(name) <= ActualWidth) { Display(name); return; }
        var extension = Path.GetExtension(name); var stem = name[..(name.Length - extension.Length)];
        var low = 0; var high = stem.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Measure(stem[..middle] + "…" + extension) <= ActualWidth) low = middle; else high = middle - 1;
        }
        if (low > 0 && char.IsHighSurrogate(stem[low - 1])) --low;
        Display(stem[..low] + "…" + extension);
    }
}
