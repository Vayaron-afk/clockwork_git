using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace ClockworkGit.Controls;

/// <summary>Сравнение старого и нового изображений: бок о бок, наложение и попиксельная разница; масштабирование с синхронной прокруткой.</summary>
public partial class ImageCompareView : UserControl
{
    static readonly double[] Zooms = [0, 0.25, 0.5, 1, 2, 4, 8]; // 0 = fit

    int _gen;
    bool _syncing;
    int _maxWidth;
    // Decoded sources (unpremultiplied) stay alive so switching channels does not decode again.
    SKBitmap? _old, _new;
    long _oldSize = -1, _newSize = -1;
    Task _rendering = Task.CompletedTask;

    /// <summary>Текущий выбранный канал изображения, ограниченный допустимым диапазоном каналов.</summary>
    ImageChannel Channel => (ImageChannel)Math.Clamp(ChannelBox.SelectedIndex, 0, 5);

    /// <summary>Настраивает области изображений, режимы отображения, выбор масштаба и каналов, а также синхронизацию прокрутки.</summary>
    public ImageCompareView()
    {
        InitializeComponent();
        var checker = AvaloniaBitmaps.CheckerBrush();
        OldScroll.Background = checker;
        NewScroll.Background = checker;
        OverlayScroll.Background = checker;

        ModeSide.IsCheckedChanged += (_, _) => UpdateMode();
        ModeOnion.IsCheckedChanged += (_, _) => UpdateMode();
        ModeDiff.IsCheckedChanged += (_, _) => UpdateMode();
        ZoomBox.SelectionChanged += (_, _) => UpdateMode();
        ChannelBox.SelectionChanged += async (_, _) =>
        {
            if (_old == null && _new == null) return;
            _rendering = Render(_gen);
            await _rendering;
        };
        DetachedFromVisualTree += async (_, _) => await ReleaseSources();
        OldScroll.ScrollChanged += (_, _) => Sync(OldScroll, NewScroll);
        NewScroll.ScrollChanged += (_, _) => Sync(NewScroll, OldScroll);
    }

    /// <summary>Передаёт смещение прокрутки парному просмотру изображения, не запуская обратную синхронизацию.</summary>
    void Sync(ScrollViewer from, ScrollViewer to)
    {
        if (_syncing) return;
        _syncing = true;
        to.Offset = from.Offset;
        _syncing = false;
    }

    /// <summary>Очищает сравнение, делает результаты текущей загрузки устаревшими и освобождает кэшированные источники изображений.</summary>
    public async void Clear()
    {
        _gen++;
        Info.Text = "";
        SetImages(null, null, null);
        await ReleaseSources();
    }

    /// <summary>Ожидает завершения выполняемой отрисовки перед освобождением используемых ею растров.</summary>
    async Task ReleaseSources()
    {
        var oldSrc = _old;
        var newSrc = _new;
        _old = _new = null;
        try { await _rendering; } catch (Exception) { /* the render's own problem; sources go anyway */ }
        oldSrc?.Dispose();
        newSrc?.Dispose();
    }

    /// <summary>Декодирует обе версии вне UI-потока; результат предыдущего вызова отбрасывается без остановки его декодирования.</summary>
    public async Task ShowAsync(byte[]? oldBytes, byte[]? newBytes, string path)
    {
        var gen = ++_gen;
        Info.Text = "Loading…";
        SetImages(null, null, null);
        await ReleaseSources();

        var (o, n) = await Task.Run(() => (ImageDecoder.Decode(oldBytes, path), ImageDecoder.Decode(newBytes, path)));
        if (gen != _gen)
        {
            o?.Dispose();
            n?.Dispose();
            return;
        }
        _old = o;
        _new = n;
        _oldSize = oldBytes?.Length ?? -1;
        _newSize = newBytes?.Length ?? -1;
        _maxWidth = Math.Max(o?.Width ?? 0, n?.Width ?? 0);
        _rendering = Render(gen);
        await _rendering;
    }

    /// <summary>Создаёт отображаемые растры и изображение различий для выбранного канала.</summary>
    async Task Render(int gen)
    {
        var (o, n, channel) = (_old, _new, Channel);
        var result = await Task.Run(() =>
        {
            var oldImage = o == null ? null : AvaloniaBitmaps.Render(o, channel);
            var newImage = n == null ? null : AvaloniaBitmaps.Render(n, channel);
            RenderedImage? diffImage = null;
            double changed = -1;
            if (o != null && n != null)
            {
                var (diff, pct) = PixelDiff.Compute(o, n, channel);
                diffImage = AvaloniaBitmaps.Render(diff);
                diff.Dispose();
                changed = pct;
            }
            var alpha = n != null ? ImageDecoder.UsesAlpha(n) : o != null && ImageDecoder.UsesAlpha(o);
            return (oldImage, newImage, diffImage, changed, alpha);
        });
        if (gen != _gen) return;

        SetImages(
            result.oldImage == null ? null : AvaloniaBitmaps.ToAvalonia(result.oldImage),
            result.newImage == null ? null : AvaloniaBitmaps.ToAvalonia(result.newImage),
            result.diffImage == null ? null : AvaloniaBitmaps.ToAvalonia(result.diffImage));

        string Describe(SKBitmap? b, long size) =>
            size < 0 ? "—"
            : b == null ? $"unsupported format, {Format.Size(size)}"
            : $"{b.Width}×{b.Height}, {Format.Size(size)}";

        var info = $"Old: {Describe(o, _oldSize)}   New: {Describe(n, _newSize)}";
        var scope = channel == ImageChannel.RGBA ? "" : $" in {channel}";
        if (result.changed >= 0)
            info += result.changed == 0 ? $"   · pixels identical{scope}" : $"   · changed {result.changed:0.###}% of pixels{scope}";
        if (o != null && n != null && (o.Width != n.Width || o.Height != n.Height)) info += "   · size differs";
        if (o != null || n != null) info += result.alpha ? "   · alpha used" : "   · alpha unused (fully opaque)";
        Info.Text = info;
        UpdateMode();
    }

    /// <summary>Назначает растры представлениям и освобождает растры, назначенные ранее.</summary>
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

    /// <summary>Применяет выбранный режим сравнения, масштаб, параметры полос прокрутки и качество интерполяции.</summary>
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
