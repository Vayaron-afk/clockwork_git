using System.Text;

namespace ClockworkGit.Git;

/// <summary>Загружает стадии конфликта, формирует предварительный результат слияния и сохраняет разрешённое содержимое.</summary>
public sealed partial class GitRepo
{
    /// <summary>Загружает объекты общего предка, нашей и их версий из неслитых стадий индекса для указанного пути.</summary>
    public async Task<ConflictSides> LoadConflict(string path)
    {
        var b = Blob(":1", path);
        var o = Blob(":2", path);
        var t = Blob(":3", path);
        await Task.WhenAll(b, o, t);
        return new ConflictSides(b.Result, o.Result, t.Result);
    }

    /// <summary>
    /// Выполняет трёхстороннее слияние diff3 для переданных стадий индекса и возвращает итоговый текст:
    /// бесконфликтные изменения объединяются, а в конфликты включается общий предок. Отсутствующая стадия заменяется пустым файлом.
    /// null возвращается при отрицательном коде завершения; остальные коды приводят к возврату stdout.
    /// </summary>
    public async Task<string?> MergeFile(ConflictSides sides)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClockworkGit", "merge-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            // Writes a merge stage to a temporary file for git merge-file.
            string Write(string name, byte[]? bytes)
            {
                var file = System.IO.Path.Combine(dir, name);
                File.WriteAllBytes(file, bytes ?? []);
                return file;
            }

            var ours = Write("ours", sides.Ours);
            var bas = Write("base", sides.Base);
            var theirs = Write("theirs", sides.Theirs);
            var r = await Run("merge-file", "-p", "--diff3",
                "-L", MergeFileParser.OursLabel, "-L", MergeFileParser.BaseLabel, "-L", MergeFileParser.TheirsLabel,
                ours, bas, theirs);
            // 0 — чистое слияние, 1–127 — конфликты. Проверка ниже считает ошибкой только отрицательный код.
            // Некоторые ошибки процесса возвращаются как 255 или другой положительный код и здесь не распознаются.
            return r.ExitCode >= 0 ? r.Output : null;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    /// <summary>Записывает текст в UTF-8 с заданным BOM и преобразованием LF в CRLF при useCrlf; при stage добавляет файл в индекс.</summary>
    /// <remarks>При useCrlf=false окончания строк входного текста не меняются. Ошибка git add выбрасывается как InvalidOperationException.</remarks>
    public async Task SaveResolution(string path, string text, bool useCrlf, bool bom, bool stage)
    {
        if (useCrlf) text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        await File.WriteAllTextAsync(FullPath(path), text, new UTF8Encoding(bom));
        if (stage)
        {
            var r = await Run("add", "--", path);
            if (!r.Ok) throw new InvalidOperationException(r.Message);
        }
    }
}
