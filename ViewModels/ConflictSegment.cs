using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.ViewModels;

/// <summary>Один конфликт: обе стороны, их общий предок и выбранное либо введённое решение.</summary>
public sealed class ConflictSegment(int number, List<string> ours, List<string> bas, List<string> theirs,
    string oursLabel, string theirsLabel) : MergeSegment
{
    static readonly IBrush Open = new ImmutableSolidColorBrush(Color.Parse("#e0a040"));
    static readonly IBrush Done = new ImmutableSolidColorBrush(Color.Parse("#5fbf5f"));

    public int Number { get; } = number;
    public List<string> Ours { get; } = ours;
    public List<string> Base { get; } = bas;
    public List<string> Theirs { get; } = theirs;
    public string OursLabel { get; } = oursLabel;
    public string TheirsLabel { get; } = theirsLabel;

    /// <summary>Заголовок с порядковым номером конфликта, начиная с единицы.</summary>
    public string Title => $"Conflict {Number}";
    /// <summary>Отформатированное содержимое нашей версии.</summary>
    public string OursText => Show(Ours);
    /// <summary>Отформатированное содержимое общего предка или «(пусто)», если исходного текста нет.</summary>
    public string BaseText => Base.Count == 0 ? "(nothing)" : Show(Base);
    /// <summary>Отформатированное содержимое их версии.</summary>
    public string TheirsText => Show(Theirs);

    string? _resolution;
    /// <summary>Результат слияния; до разрешения конфликта равен null. Ввод текста в поле результата разрешает конфликт.</summary>
    public string? Resolution
    {
        get => _resolution;
        set
        {
            if (!Set(ref _resolution, value)) return;
            Raise(nameof(IsResolved));
            Raise(nameof(Status));
            Raise(nameof(StatusBrush));
        }
    }

    /// <summary>Показывает, указал ли пользователь решение.</summary>
    public bool IsResolved => Resolution != null;
    /// <summary>Краткая подпись состояния рядом с конфликтом.</summary>
    public string Status => IsResolved ? "resolved" : "unresolved";
    /// <summary>Цвет состояния, показывающий, разрешён ли конфликт.</summary>
    public IBrush StatusBrush => IsResolved ? Done : Open;

    /// <summary>Устанавливает решение, выбрав один из вариантов в заданном порядке сторон.</summary>
    public void Take(string choice) => Resolution = choice switch
    {
        "ours" => Show(Ours),
        "theirs" => Show(Theirs),
        "ours+theirs" => Show(Ours.Concat(Theirs)),
        "theirs+ours" => Show(Theirs.Concat(Ours)),
        "base" => Show(Base),
        _ => "",
    };

    /// <summary>Возвращает решение или конфликт с маркерами diff3, если он ещё не разрешён.</summary>
    public override IEnumerable<string> ResultLines =>
        Resolution != null
            ? Resolution.Length == 0 ? [] : Resolution.Replace("\r\n", "\n").Split('\n')
            : new[] { "<<<<<<< ours" }.Concat(Ours).Append("||||||| base").Concat(Base)
                .Append("=======").Concat(Theirs).Append(">>>>>>> theirs");
}
