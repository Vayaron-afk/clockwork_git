using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Controls;

/// <summary>Отрисовывает линии графа для одной строки коммита; модель линий описана в <see cref="GraphBuilder"/>.</summary>
public sealed class GraphCell : Control
{
    public const double LaneWidth = 14;

    public static readonly StyledProperty<GraphRow?> RowProperty =
        AvaloniaProperty.Register<GraphCell, GraphRow?>(nameof(Row));

    static readonly IBrush[] Palette = new[]
    {
        "#4fc1ff", "#f5a623", "#7ed321", "#e25d9c", "#bd93f9", "#50e3c2", "#ff6b6b", "#f8e71c", "#9aa5b1",
    }.Select(c => (IBrush)new ImmutableSolidColorBrush(Color.Parse(c))).ToArray();

    static readonly IPen[] Pens = Palette.Select(b => (IPen)new ImmutablePen((IImmutableBrush)b, 2)).ToArray();
    static readonly IBrush Background = new ImmutableSolidColorBrush(Color.Parse("#1e1f22"));
    static readonly IPen NodeOutline = new ImmutablePen((IImmutableBrush)Background, 1.5);

    static GraphCell()
    {
        AffectsRender<GraphCell>(RowProperty);
        AffectsMeasure<GraphCell>(RowProperty);
    }

    public GraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>Вычисляет ширину по наибольшему номеру линии в этой строке графа.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var r = Row;
        if (r == null) return default;
        var lanes = Math.Max(Math.Max(r.Before.Length, r.After.Length), r.Node + 1);
        return new Size(lanes * LaneWidth + 6, 0);
    }

    /// <summary>Преобразует индекс линии в горизонтальную координату её центра.</summary>
    static double X(int lane) => lane * LaneWidth + LaneWidth / 2 + 2;

    /// <summary>Отрисовывает входящие линии, соединения с родительскими коммитами и узел текущего коммита.</summary>
    public override void Render(DrawingContext ctx)
    {
        var r = Row;
        if (r == null) return;
        var h = Bounds.Height;
        var mid = h / 2;

        for (var j = 0; j < r.Before.Length; j++)
        {
            var hash = r.Before[j];
            if (hash == null) continue;
            if (hash == r.Hash) Curve(ctx, Pens[j % Pens.Length], new Point(X(j), 0), new Point(X(r.Node), mid));
            else ctx.DrawLine(Pens[j % Pens.Length], new Point(X(j), 0), new Point(X(j), h));
        }

        foreach (var k in r.Down)
            Curve(ctx, Pens[k % Pens.Length], new Point(X(r.Node), mid), new Point(X(k), h));

        var center = new Point(X(r.Node), mid);
        if (r.IsWorkingTree)
            ctx.DrawEllipse(Background, Pens[r.Node % Pens.Length], center, 4.5, 4.5); // hollow: not a real commit
        else
            ctx.DrawEllipse(Palette[r.Node % Palette.Length], NodeOutline, center, 4.5, 4.5);
    }

    /// <summary>Отрисовывает вертикальную линию или плавную кривую между точками соединения линий.</summary>
    static void Curve(DrawingContext ctx, IPen pen, Point a, Point b)
    {
        if (Math.Abs(a.X - b.X) < 0.5)
        {
            ctx.DrawLine(pen, a, b);
            return;
        }
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var my = (a.Y + b.Y) / 2;
            g.BeginFigure(a, false);
            g.CubicBezierTo(new Point(a.X, my), new Point(b.X, my), b);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geometry);
    }
}
