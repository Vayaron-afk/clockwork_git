namespace ClockworkGit.Git;

/// <summary>Предоставляет кэшированный индекс соответствия GUID ресурсов Unity их путям для аннотаций diff.</summary>
public sealed partial class GitRepo
{
    static readonly TimeSpan GuidIndexLifetime = TimeSpan.FromMinutes(2);

    Task<UnityGuidIndex>? _guidIndex;
    DateTime _guidIndexBuilt;

    /// <summary>Индекс GUID рабочей копии; перестраивается, если ему больше нескольких минут.</summary>
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
