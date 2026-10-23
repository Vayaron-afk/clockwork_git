namespace ClockworkGit.Git;

/// <summary>
/// Assigns commits to lanes. Each lane holds the hash it is "waiting for";
/// lanes never shift, freed slots are reused, so pass-through lines stay vertical.
/// </summary>
public static class GraphBuilder
{
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

    static int FreeSlot(List<string?> lanes)
    {
        var i = lanes.IndexOf(null);
        if (i >= 0) return i;
        lanes.Add(null);
        return lanes.Count - 1;
    }
}
