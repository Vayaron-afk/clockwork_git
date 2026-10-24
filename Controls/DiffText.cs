using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ClockworkGit.Controls;

/// <summary>Строка diff с одним выделенным фрагментом — изменённой частью строки.</summary>
public sealed class DiffText : TextBlock
{
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<DiffText, string?>(nameof(Value));
    public static readonly StyledProperty<int> HiStartProperty =
        AvaloniaProperty.Register<DiffText, int>(nameof(HiStart));
    public static readonly StyledProperty<int> HiLengthProperty =
        AvaloniaProperty.Register<DiffText, int>(nameof(HiLength));
    public static readonly StyledProperty<IBrush?> HiBrushProperty =
        AvaloniaProperty.Register<DiffText, IBrush?>(nameof(HiBrush));

    /// <summary>Применяет стандартные стили темы TextBlock к этому производному элементу управления.</summary>
    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int HiStart { get => GetValue(HiStartProperty); set => SetValue(HiStartProperty, value); }
    public int HiLength { get => GetValue(HiLengthProperty); set => SetValue(HiLengthProperty, value); }
    public IBrush? HiBrush { get => GetValue(HiBrushProperty); set => SetValue(HiBrushProperty, value); }

    /// <summary>Перестраивает встроенную разметку текста при изменении исходного текста или параметров выделения.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == HiStartProperty
            || change.Property == HiLengthProperty || change.Property == HiBrushProperty)
            Rebuild();
    }

    /// <summary>Повторно создаёт встроенные элементы текста с заданным фоном изменённого фрагмента.</summary>
    void Rebuild()
    {
        var text = Value ?? "";
        var start = Math.Clamp(HiStart, 0, text.Length);
        var length = Math.Clamp(HiLength, 0, text.Length - start);
        if (length == 0)
        {
            Inlines = null;
            Text = text;
            return;
        }
        Inlines = new InlineCollection
        {
            new Run(text[..start]),
            new Run(text.Substring(start, length)) { Background = HiBrush },
            new Run(text[(start + length)..]),
        };
    }
}
