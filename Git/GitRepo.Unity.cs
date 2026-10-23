namespace ClockworkGit.Git;

// Unity projects: GUID → asset path index for annotating diffs.
public sealed partial class GitRepo
{
    static readonly TimeSpan GuidIndexLifetime = TimeSpan.FromMinutes(2);

    Task<UnityGuidIndex>? _guidIndex;
    DateTime _guidIndexBuilt;

    /// <summary>The GUID index of the working tree, rebuilt when older than a couple of minutes.</summary>
    public Task<UnityGuidIndex> GuidIndex()
    {
        if (_guidIndex == null || _guidIndex.IsFaulted || DateTime.Now - _guidIndexBuilt > GuidIndexLifetime)
        {
            _guidIndexBuilt = DateTime.Now;
            _guidIndex = UnityGuidIndex.Build(this);
        }
        return _guidIndex;
    }
}
