namespace ClockworkGit.ViewModels;

/// <summary>Строка коммита в диалоге интерактивного rebase.</summary>
public sealed class RebaseItem : Observable
{
    static readonly RebaseAction[] Actions = Enum.GetValues<RebaseAction>();

    /// <summary>Доступные действия в списке выбора операции rebase.</summary>
    public RebaseAction[] AllActions => Actions;
    public required CommitInfo Commit { get; init; }
    /// <summary>Задаётся до <see cref="Message"/>, чтобы исходное сообщение не считалось изменённым.</summary>
    public required string OriginalMessage { get; init; }

    RebaseAction _action;
    public RebaseAction Action
    {
        get => _action;
        set
        {
            if (!Set(ref _action, value)) return;
            Raise(nameof(Opacity));
            Raise(nameof(Hint));
        }
    }

    string _message = "";
    /// <summary>Изменение сообщения относительно исходного переключает Pick на Reword; другие выбранные действия сохраняются.</summary>
    public string Message
    {
        get => _message;
        set
        {
            if (!Set(ref _message, value)) return;
            Raise(nameof(Subject));
            if (Action == RebaseAction.Pick && value != OriginalMessage) Action = RebaseAction.Reword;
        }
    }

    /// <summary>Первая строка сообщения коммита без лишних пробелов для отображения в списке.</summary>
    public string Subject => Message.Split('\n', 2)[0].Trim();
    /// <summary>Прозрачность для приглушённого отображения коммита, помеченного для удаления.</summary>
    public double Opacity => Action == RebaseAction.Drop ? 0.4 : 1;

    /// <summary>Краткое описание выбранного действия rebase.</summary>
    public string Hint => Action switch
    {
        RebaseAction.Reword => "new message",
        RebaseAction.Squash => "↓ into the commit below",
        RebaseAction.Fixup => "↓ into the commit below, message dropped",
        RebaseAction.Drop => "removed",
        _ => "",
    };

    /// <summary>Создаёт неизменяемую запись плана rebase, представленную этой редактируемой строкой.</summary>
    public RebaseStep ToStep() => new(Commit.Hash, Action, Message, OriginalMessage);
}
