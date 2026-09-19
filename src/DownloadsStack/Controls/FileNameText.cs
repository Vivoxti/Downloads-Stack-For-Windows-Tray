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
    // Fitting one name costs about eight measurements, and every row is fitted again whenever the list is
    // rebuilt. Building the typeface for each of them was most of that work.
    private Typeface? _typeface;
    private double _pixelsPerDip;
    /// <summary>
    /// Plate edge left of the first glyph and right of the last one. The leading side matches this text's
    /// own left margin, since the plate is drawn from the start of the column; the trailing side is wider
    /// on purpose — the last character sat almost on the edge, and the ellipsis of a trimmed name even more so.
    /// </summary>
    private const double LeadingPad = 8, TrailingPad = 14;
    public FileNameText() { TextTrimming = TextTrimming.CharacterEllipsis; SizeChanged += (_, _) => UpdateText(); }

    /// <summary>TextBlock seals property notifications, so the cached face is validated where it is used.</summary>
    private Typeface Face()
    {
        if (_typeface is null || !_typeface.FontFamily.Equals(FontFamily) || _typeface.Style != FontStyle ||
            _typeface.Weight != FontWeight || _typeface.Stretch != FontStretch)
            _typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        return _typeface;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _pixelsPerDip = newDpi.PixelsPerDip;
        UpdateText(); // Widths measured for the previous display no longer describe this one.
    }

    private void UpdateText()
    {
        var name = FileName ?? "";
        if (ActualWidth <= 0) { Text = name; return; }
        if (_pixelsPerDip <= 0) _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = Face();
        var scale = _pixelsPerDip;
        double Measure(string text) => new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection, face, FontSize, Foreground, scale).WidthIncludingTrailingWhitespace;
        void Display(string text)
        {
            Text = text;
            SetValue(BackdropWidthPropertyKey, Math.Min(ActualWidth, Measure(text)) + LeadingPad + TrailingPad);
        }
        if (Measure(name) <= ActualWidth) { Display(name); return; }
        var extension = Path.GetExtension(name); var stem = name[..(name.Length - extension.Length)];
        var low = 0; var high = stem.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Measure(string.Concat(stem.AsSpan(0, middle), "…", extension)) <= ActualWidth) low = middle; else high = middle - 1;
        }
        if (low > 0 && char.IsHighSurrogate(stem[low - 1])) --low;
        Display(string.Concat(stem.AsSpan(0, low), "…", extension));
    }
}
