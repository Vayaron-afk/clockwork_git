namespace ClockworkGit.ViewModels;

/// <summary>A commit row of the interactive rebase dialog.</summary>
public sealed class RebaseItem : Observable
{
    static readonly RebaseAction[] Actions = Enum.GetValues<RebaseAction>();

    public RebaseAction[] AllActions => Actions;
    public required CommitInfo Commit { get; init; }
    /// <summary>Set before <see cref="Message"/> so the initial message does not count as an edit.</summary>
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
    /// <summary>Editing the message of a picked commit turns it into a reword.</summary>
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

    public string Subject => Message.Split('\n', 2)[0].Trim();
    public double Opacity => Action == RebaseAction.Drop ? 0.4 : 1;

    public string Hint => Action switch
    {
        RebaseAction.Reword => "new message",
        RebaseAction.Squash => "↓ into the commit below",
        RebaseAction.Fixup => "↓ into the commit below, message dropped",
        RebaseAction.Drop => "removed",
        _ => "",
    };

    public RebaseStep ToStep() => new(Commit.Hash, Action, Message, OriginalMessage);
}
