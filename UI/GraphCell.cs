using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ClockworkGit.Git;

namespace ClockworkGit.UI;

/// <summary>Draws the graph lanes of one commit row.</summary>
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
    static readonly IPen NodeOutline = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1e1f22")), 1.5);

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

    protected override Size MeasureOverride(Size availableSize)
    {
        var r = Row;
        if (r == null) return default;
        var lanes = Math.Max(Math.Max(r.Before.Length, r.After.Length), r.Node + 1);
        return new Size(lanes * LaneWidth + 6, 0);
    }

    static double X(int lane) => lane * LaneWidth + LaneWidth / 2 + 2;

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
            ctx.DrawEllipse(new ImmutableSolidColorBrush(Color.Parse("#1e1f22")), new ImmutablePen((IImmutableBrush)Palette[r.Node % Palette.Length], 2), center, 4.5, 4.5);
        else
            ctx.DrawEllipse(Palette[r.Node % Palette.Length], NodeOutline, center, 4.5, 4.5);
    }

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
