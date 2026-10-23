namespace ClockworkGit.Models;

/// <summary>An entry of git lfs ls-files; <see cref="Downloaded"/> means the object is in the local cache.</summary>
public sealed record LfsFile(string Path, long Size, bool Downloaded, string Oid);
