namespace ClockworkGit.Git;

/// <summary>Parses <c>git merge-file -p --diff3 -L OURS -L BASE -L THEIRS</c> output into chunks.</summary>
public static class MergeFileParser
{
    public const string OursLabel = "OURS", BaseLabel = "BASE", TheirsLabel = "THEIRS";

    enum State { Common, Ours, Base, Theirs }

    public static List<MergeChunk> Parse(string merged, out bool endsWithNewline)
    {
        endsWithNewline = merged.EndsWith('\n');
        var lines = merged.Split('\n').ToList();
        if (endsWithNewline) lines.RemoveAt(lines.Count - 1);

        var chunks = new List<MergeChunk>();
        var common = new List<string>();
        List<string> ours = new(), bas = new(), theirs = new();
        var state = State.Common;

        foreach (var line in lines)
        {
            var marker = line.TrimEnd('\r');
            if (state == State.Common && marker.StartsWith("<<<<<<< " + OursLabel))
            {
                if (common.Count > 0) chunks.Add(new MergeChunk(common));
                common = new List<string>();
                ours = new(); bas = new(); theirs = new();
                state = State.Ours;
            }
            else if (state == State.Ours && marker.StartsWith("||||||| " + BaseLabel)) state = State.Base;
            else if (state is State.Ours or State.Base && marker == "=======") state = State.Theirs;
            else if (state == State.Theirs && marker.StartsWith(">>>>>>> " + TheirsLabel))
            {
                chunks.Add(new MergeChunk(new List<string>(), ours, bas, theirs));
                state = State.Common;
            }
            else
            {
                (state switch { State.Ours => ours, State.Base => bas, State.Theirs => theirs, _ => common }).Add(line);
            }
        }
        if (common.Count > 0) chunks.Add(new MergeChunk(common));
        return chunks;
    }
}
