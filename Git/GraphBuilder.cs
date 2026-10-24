namespace ClockworkGit.Git;

/// <summary>
/// Распределяет коммиты по линиям. Каждая линия хранит хеш коммита, которого ожидает;
/// линии не смещаются, освободившиеся позиции используются повторно, поэтому проходящие линии остаются вертикальными.
/// </summary>
public static class GraphBuilder
{
    /// <summary>Назначает коммитам линии графа в порядке отображения и сохраняет строку графа в каждом коммите.</summary>
    public static void Build(IList<CommitInfo> commits)
    {
        var lanes = new List<string?>();
        foreach (var c in commits)
        {
            var before = lanes.ToArray();

            var node = lanes.IndexOf(c.Hash);
            if (node < 0) node = FreeSlot(lanes);
            for (var j = 0; j < lanes.Count; j++)
                if (lanes[j] == c.Hash) lanes[j] = null;

            var down = new List<int>();
            for (var pi = 0; pi < c.Parents.Length; pi++)
            {
                var parent = c.Parents[pi];
                var k = lanes.IndexOf(parent);
                if (k < 0)
                {
                    k = pi == 0 ? node : FreeSlot(lanes);
                    lanes[k] = parent;
                }
                down.Add(k);
            }

            while (lanes.Count > 0 && lanes[^1] == null) lanes.RemoveAt(lanes.Count - 1);
            c.Graph = new GraphRow(c.Hash, node, before, lanes.ToArray(), down.ToArray());
        }
    }

    /// <summary>Возвращает первую свободную линию или добавляет новую, если заняты все существующие.</summary>
    static int FreeSlot(List<string?> lanes)
    {
        var i = lanes.IndexOf(null);
        if (i >= 0) return i;
        lanes.Add(null);
        return lanes.Count - 1;
    }
}
