using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Controls;

/// <summary>Полоса обзора всего файла с маркерами изменений и видимым диапазоном; щелчок перемещает к выбранному месту.</summary>
public sealed class OverviewRuler : Control
{
    public static readonly StyledProperty<IReadOnlyList<DiffRow>?> RowsProperty =
        AvaloniaProperty.Register<OverviewRuler, IReadOnlyList<DiffRow>?>(nameof(Rows));
    public static readonly StyledProperty<double> ViewportStartProperty =
        AvaloniaProperty.Register<OverviewRuler, double>(nameof(ViewportStart));
    public static readonly StyledProperty<double> ViewportCountProperty =
        AvaloniaProperty.Register<OverviewRuler, double>(nameof(ViewportCount));

    static readonly IBrush Background = new ImmutableSolidColorBrush(Color.Parse("#232427"));
    static readonly IBrush Modified = new ImmutableSolidColorBrush(Color.Parse("#c9a227"));
    static readonly IBrush Removed = new ImmutableSolidColorBrush(Color.Parse("#e06060"));
    static readonly IBrush Added = new ImmutableSolidColorBrush(Color.Parse("#5fbf5f"));
    static readonly IBrush Viewport = new ImmutableSolidColorBrush(Color.FromArgb(50, 255, 255, 255));

    static OverviewRuler()
    {
        AffectsRender<OverviewRuler>(RowsProperty, ViewportStartProperty, ViewportCountProperty);
    }

    public IReadOnlyList<DiffRow>? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public double ViewportStart { get => GetValue(ViewportStartProperty); set => SetValue(ViewportStartProperty, value); }
    public double ViewportCount { get => GetValue(ViewportCountProperty); set => SetValue(ViewportCountProperty, value); }

    /// <summary>Вызывается с индексом строки diff, выбранной щелчком по шкале.</summary>
    public event Action<int>? RowRequested;

    /// <summary>Отрисовывает блоки изменений и индикатор области просмотра для текущего diff.</summary>
    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(Background, bounds);
        var rows = Rows;
        if (rows == null || rows.Count == 0) return;
        var h = bounds.Height;
        double total = rows.Count;

        for (var i = 0; i < rows.Count;)
        {
            if (rows[i].Kind != DiffRowKind.Change) { i++; continue; }
            var start = i;
            bool left = false, right = false;
            while (i < rows.Count && rows[i].Kind == DiffRowKind.Change)
            {
                left |= rows[i].HasLeft;
                right |= rows[i].HasRight;
                i++;
            }
            var brush = left && right ? Modified : left ? Removed : Added;
            var y = start / total * h;
            var height = Math.Max(2, (i - start) / total * h);
            ctx.FillRectangle(brush, new Rect(2, y, bounds.Width - 4, height));
        }

        if (ViewportCount > 0 && ViewportCount < total)
            ctx.FillRectangle(Viewport, new Rect(0, ViewportStart / total * h, bounds.Width, Math.Max(4, ViewportCount / total * h)));
    }

    /// <summary>Преобразует положение указателя в строку diff и запрашивает прокрутку к ней.</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var rows = Rows;
        if (rows == null || rows.Count == 0 || Bounds.Height <= 0) return;
        var index = (int)(e.GetPosition(this).Y / Bounds.Height * rows.Count);
        RowRequested?.Invoke(Math.Clamp(index, 0, rows.Count - 1));
        e.Handled = true;
    }
}
