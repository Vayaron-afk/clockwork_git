using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace ClockworkGit.UI;

/// <summary>Old/new image comparison: side by side, onion skin, pixel difference; zoom with synced scrolling.</summary>
public partial class ImageCompareView : UserControl
{
    static readonly double[] Zooms = [0, 0.25, 0.5, 1, 2, 4, 8]; // 0 = fit

    int _gen;
    bool _syncing;
    int _maxWidth;

    public ImageCompareView()
    {
        InitializeComponent();
        var checker = ImageTools.CheckerBrush();
        OldScroll.Background = checker;
        NewScroll.Background = checker;
        OverlayScroll.Background = checker;

        ModeSide.IsCheckedChanged += (_, _) => UpdateMode();
        ModeOnion.IsCheckedChanged += (_, _) => UpdateMode();
        ModeDiff.IsCheckedChanged += (_, _) => UpdateMode();
        ZoomBox.SelectionChanged += (_, _) => UpdateMode();
        OldScroll.ScrollChanged += (_, _) => Sync(OldScroll, NewScroll);
        NewScroll.ScrollChanged += (_, _) => Sync(NewScroll, OldScroll);
    }

    void Sync(ScrollViewer from, ScrollViewer to)
    {
        if (_syncing) return;
        _syncing = true;
        to.Offset = from.Offset;
        _syncing = false;
    }

    public void Clear()
    {
        _gen++;
        Info.Text = "";
        SetImages(null, null, null);
    }

    /// <summary>Decodes both versions off the UI thread; a newer call cancels an older one.</summary>
    public async Task ShowAsync(byte[]? oldBytes, byte[]? newBytes, string path)
    {
        var gen = ++_gen;
        Info.Text = "Loading…";
        SetImages(null, null, null);

        var (oldSk, newSk, diffSk, changed) = await Task.Run(() =>
        {
            var o = ImageTools.Decode(oldBytes, path);
            var n = ImageTools.Decode(newBytes, path);
            SKBitmap? d = null;
            double pct = -1;
            if (o != null && n != null) (d, pct) = ImageTools.Diff(o, n);
            return (o, n, d, pct);
        });

        try
        {
            if (gen != _gen) return;
            SetImages(
                oldSk == null ? null : ImageTools.ToAvalonia(oldSk),
                newSk == null ? null : ImageTools.ToAvalonia(newSk),
                diffSk == null ? null : ImageTools.ToAvalonia(diffSk));

            static string Describe(SKBitmap? b, byte[]? bytes) =>
                bytes == null ? "—"
                : b == null ? $"unsupported format, {LfsActions.FormatSize(bytes.Length)}"
                : $"{b.Width}×{b.Height}, {LfsActions.FormatSize(bytes.Length)}";

            var info = $"Old: {Describe(oldSk, oldBytes)}   New: {Describe(newSk, newBytes)}";
            if (changed >= 0) info += changed == 0 ? "   · pixels identical" : $"   · changed {changed:0.###}% of pixels";
            if (oldSk != null && newSk != null && (oldSk.Width != newSk.Width || oldSk.Height != newSk.Height)) info += "   · size differs";
            Info.Text = info;
            _maxWidth = Math.Max(oldSk?.Width ?? 0, newSk?.Width ?? 0);
            UpdateMode();
        }
        finally
        {
            oldSk?.Dispose();
            newSk?.Dispose();
            diffSk?.Dispose();
        }
    }

    void SetImages(Bitmap? oldImage, Bitmap? newImage, Bitmap? diffImage)
    {
        var previous = new[] { OldImage.Source, NewImage.Source, DiffImage.Source };
        OldImage.Source = oldImage;
        OnionOld.Source = oldImage;
        NewImage.Source = newImage;
        OnionNew.Source = newImage;
        DiffImage.Source = diffImage;
        foreach (var p in previous) (p as IDisposable)?.Dispose();
    }

    void UpdateMode()
    {
        var onion = ModeOnion.IsChecked == true;
        SideGrid.IsVisible = ModeSide.IsChecked == true;
        OverlayScroll.IsVisible = onion;
        DiffScroll.IsVisible = ModeDiff.IsChecked == true;
        OnionSlider.IsVisible = onion;

        var zoom = Zooms[Math.Clamp(ZoomBox.SelectedIndex, 0, Zooms.Length - 1)];
        var fit = zoom == 0;
        var bars = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        foreach (var sv in new[] { OldScroll, NewScroll, OverlayScroll, DiffScroll })
        {
            sv.HorizontalScrollBarVisibility = bars;
            sv.VerticalScrollBarVisibility = bars;
        }
        var transform = fit ? null : new ScaleTransform(zoom, zoom);
        foreach (var z in new[] { OldZoom, NewZoom, OverlayZoom, DiffZoom }) z.LayoutTransform = transform;

        // Pixel art and zoomed-in views look right only without smoothing.
        var sharp = zoom >= 2 || (fit && _maxWidth is > 0 and <= 256);
        foreach (var img in new[] { OldImage, NewImage, OnionOld, OnionNew, DiffImage })
        {
            img.Stretch = fit ? Stretch.Uniform : Stretch.None;
            RenderOptions.SetBitmapInterpolationMode(img, sharp ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        }
    }
}
