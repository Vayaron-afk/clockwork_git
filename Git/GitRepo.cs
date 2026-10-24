using System.Text;

namespace ClockworkGit.Git;

/// <summary>
/// Репозиторий на диске: команды Git выполняются через <see cref="GitRunner"/>, локальные файлы также читаются напрямую.
/// Части класса отвечают за историю и состояние, diff, blame, конфликты, LFS, проекты и индекс GUID Unity.
/// </summary>
public sealed partial class GitRepo
{
    /// <summary>SHA-1 пустого дерева, используемый как «родитель» корневого коммита при сравнении; не подходит для репозиториев SHA-256.</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    /// <summary>Создаёт объект репозитория по путям к рабочей копии и метаданным Git.</summary>
    GitRepo(string path, string gitDir)
    {
        Path = path;
        GitDir = gitDir;
    }

    /// <summary>Переданный при открытии каталог, используемый как рабочий каталог команд и база относительных путей файлов.</summary>
    public string Path { get; }
    /// <summary>Абсолютный каталог метаданных Git, возвращённый rev-parse --absolute-git-dir.</summary>
    public string GitDir { get; }

    /// <summary>Открывает репозиторий, содержащий <paramref name="path"/>, или возвращает null, если Git не может определить его.</summary>
    /// <remarks>Путь сохраняется без приведения к корню рабочей копии; вызывающий код должен передать корень для корректной работы с путями файлов.</remarks>
    public static async Task<GitRepo?> Open(string path)
    {
        var r = await GitRunner.RunAsync(path, "rev-parse", "--absolute-git-dir");
        return r.Ok ? new GitRepo(path, r.Output.Trim()) : null;
    }

    /// <summary>Запускает команду Git в этом репозитории без стандартного ввода и интерактивных запросов.</summary>
    public Task<GitResult> Run(params string[] args) => GitRunner.RunAsync(Path, args);

    /// <summary>Запускает команду Git в этом репозитории и передаёт указанный текст в стандартный ввод.</summary>
    public Task<GitResult> RunWithInput(string input, params string[] args) => GitRunner.RunWithInputAsync(Path, input, args);

    /// <summary>Запускает команду Git в этом репозитории с дополнительными переменными среды процесса.</summary>
    public Task<GitResult> RunWithEnv(IReadOnlyDictionary<string, string> env, params string[] args) =>
        GitRunner.RunWithEnvAsync(Path, env, args);

    /// <summary>Определяет состояние merge, cherry-pick, revert или rebase по служебным файлам и каталогам Git; чистоту рабочей копии не проверяет.</summary>
    public RepoState GetState()
    {
        if (File.Exists(System.IO.Path.Combine(GitDir, "MERGE_HEAD"))) return RepoState.Merging;
        if (File.Exists(System.IO.Path.Combine(GitDir, "CHERRY_PICK_HEAD"))) return RepoState.CherryPicking;
        if (File.Exists(System.IO.Path.Combine(GitDir, "REVERT_HEAD"))) return RepoState.Reverting;
        if (Directory.Exists(System.IO.Path.Combine(GitDir, "rebase-merge"))
            || Directory.Exists(System.IO.Path.Combine(GitDir, "rebase-apply"))) return RepoState.Rebasing;
        return RepoState.Clean;
    }

    /// <summary>Объединяет путь Git относительно репозитория с корнем рабочей копии, используя системные разделители.</summary>
    public string FullPath(string relPath) =>
        System.IO.Path.Combine(Path, relPath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>Записывает сообщение во временный файл UTF-8 без BOM и возвращает путь; удалить файл должен вызывающий код.</summary>
    public static string WriteTempMessage(string message)
    {
        var tmp = System.IO.Path.GetTempFileName();
        File.WriteAllText(tmp, message, new UTF8Encoding(false));
        return tmp;
    }
}
