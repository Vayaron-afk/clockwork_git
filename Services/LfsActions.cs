using Avalonia.Controls;

namespace ClockworkGit.Services;

/// <summary>Общие сценарии включения и отключения отслеживания LFS и преобразования файлов для главного окна и окна LFS.</summary>
public static class LfsActions
{
    /// <summary>Возвращает «*.png» для «a/b/c.png»; пустую строку, если у файла нет расширения.</summary>
    public static string ExtensionPattern(string relPath)
    {
        var ext = Path.GetExtension(relPath);
        return ext.Length > 0 ? "*" + ext : "";
    }

    /// <summary>Для буквального пути добавляет префикс :(literal); для шаблона удаляет начальный «/» без преобразования семантики шаблона.</summary>
    static string Pathspec(string pattern, bool literal) => literal ? ":(literal)" + pattern : pattern.TrimStart('/');

    /// <summary>Добавляет правило отслеживания Git LFS и при необходимости преобразует соответствующие отслеживаемые файлы.</summary>
    /// <remarks>Добавляет весь корневой .gitattributes в индекс. Конвертация через add --renormalize включает текущие правки рабочей копии и не переписывает историю.</remarks>
    public static async Task<bool> Track(Window owner, GitRepo repo, string pattern, bool literal)
    {
        var choice = await Dialogs.Choose(owner, "Track in LFS",
            $"Add '{pattern}' to the LFS rules in .gitattributes and stage .gitattributes.\n\n" +
            "Also convert matching files that are already in the repository? Their current versions get staged " +
            "as LFS pointers and move to LFS with your next commit. Older versions stay in history as they are.",
            "Track and convert", "Track rule only", "Cancel");
        if (choice < 0) return false;

        var args = literal ? new[] { "lfs", "track", "--filename", pattern } : new[] { "lfs", "track", pattern };
        if (!await Run(owner, repo, args)) return false;
        if (!await Run(owner, repo, "add", ".gitattributes")) return false;
        return choice != 0 || await Run(owner, repo, "add", "--renormalize", "--", Pathspec(pattern, literal));
    }

    /// <summary>Удаляет правило отслеживания Git LFS и при необходимости преобразует подходящие файлы в обычные объекты Git.</summary>
    /// <remarks>Добавляет весь .gitattributes в индекс. При конвертации действуют оставшиеся правила атрибутов; старые объекты LFS сохраняются.</remarks>
    public static async Task<bool> Untrack(Window owner, GitRepo repo, string pattern)
    {
        var choice = await Dialogs.Choose(owner, "Untrack from LFS",
            $"Remove the rule '{pattern}' from .gitattributes and stage .gitattributes.\n\n" +
            "Also convert matching files back to regular git files? Their current versions get staged in full. " +
            "Versions already uploaded stay in the LFS storage on GitHub and keep using its quota.",
            "Untrack and convert back", "Remove rule only", "Cancel");
        if (choice < 0) return false;

        if (!await Run(owner, repo, "lfs", "untrack", pattern)) return false;
        if (!await Run(owner, repo, "add", ".gitattributes")) return false;
        return choice != 0 || await Run(owner, repo, "add", "--renormalize", "--", Pathspec(pattern, false));
    }

    /// <summary>Повторно добавляет указанные отслеживаемые файлы в индекс, чтобы применить к ним текущие правила LFS.</summary>
    public static async Task<bool> ConvertExisting(Window owner, GitRepo repo, IReadOnlyCollection<string> paths)
    {
        var r = await repo.RunWithInput(string.Join('\0', paths) + '\0',
            "add", "--renormalize", "--pathspec-from-file=-", "--pathspec-file-nul");
        if (!r.Ok) await Dialogs.ShowMessage(owner, "git add --renormalize failed", r.Message);
        return r.Ok;
    }

    /// <summary>Запускает команду Git и отображает ошибку в родительском окне при неудачном выполнении.</summary>
    static async Task<bool> Run(Window owner, GitRepo repo, params string[] args)
    {
        var r = await repo.Run(args);
        if (!r.Ok) await Dialogs.ShowMessage(owner, $"git {string.Join(' ', args.Take(2))} failed", r.Message);
        return r.Ok;
    }
}
