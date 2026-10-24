using Avalonia.Controls;
using Avalonia.Interactivity;
using static ClockworkGit.Utils.MenuFactory;

namespace ClockworkGit.Views;

// Partial staging: Unstaged/Staged/All modes of the diff panel; stage, unstage or discard selected lines/blocks.
public partial class MainWindow
{
    /// <summary>
    /// Сохраняет выбранный режим, пока выбран тот же файл; для другого файла выбирает сторону с изменениями.
    /// Также обновляет переключатель режима и кнопки действий.
    /// </summary>
    WorkTreeDiffMode PickDiffMode(FileChange f)
    {
        var hasUnstaged = f.WorkStatus != ' ' && !f.IsUntracked;
        var hasStaged = f.IsStaged;
        if (f.RelPath != _diffModeFile)
        {
            _diffModeFile = f.RelPath;
            _diffMode = hasUnstaged ? WorkTreeDiffMode.Unstaged : hasStaged ? WorkTreeDiffMode.Staged : WorkTreeDiffMode.All;
        }
        else if (_diffMode == WorkTreeDiffMode.Unstaged && !hasUnstaged)
            _diffMode = hasStaged ? WorkTreeDiffMode.Staged : WorkTreeDiffMode.All;
        else if (_diffMode == WorkTreeDiffMode.Staged && !hasStaged)
            _diffMode = hasUnstaged ? WorkTreeDiffMode.Unstaged : WorkTreeDiffMode.All;

        // Untracked and conflicted files, and repositories without commits, are staged as whole files only.
        var partial = !f.IsUntracked && !f.IsConflict && _headHash != null;
        if (!partial) _diffMode = WorkTreeDiffMode.All;
        StagePanel.IsVisible = partial;

        _updatingDiffMode = true;
        ModeUnstaged.IsEnabled = hasUnstaged;
        ModeStaged.IsEnabled = hasStaged;
        ModeUnstaged.IsChecked = _diffMode == WorkTreeDiffMode.Unstaged;
        ModeStaged.IsChecked = _diffMode == WorkTreeDiffMode.Staged;
        ModeAll.IsChecked = _diffMode == WorkTreeDiffMode.All;
        _updatingDiffMode = false;

        var verb = _diffMode == WorkTreeDiffMode.Staged ? "Unstage" : "Stage";
        BlockButton.Content = $"{verb} block";
        LinesButton.Content = $"{verb} lines";
        BlockButton.IsEnabled = LinesButton.IsEnabled = _diffMode != WorkTreeDiffMode.All;
        DiscardLinesButton.IsVisible = _diffMode == WorkTreeDiffMode.Unstaged;
        return _diffMode;
    }

    /// <summary>Переключает сравнение выбранного файла рабочей копии между staged, unstaged и всеми изменениями.</summary>
    void OnDiffModeChosen(RadioButton button, WorkTreeDiffMode mode)
    {
        if (_updatingDiffMode || button.IsChecked != true) return;
        _diffMode = mode;
        ShowDiff(SingleSelectedFile());
    }

    /// <summary>Добавляет в индекс или удаляет из него выбранные блоки изменений целиком.</summary>
    async void OnBlockAction(object? sender, RoutedEventArgs e) => await ApplySelection(wholeBlocks: true, discard: false);
    /// <summary>Добавляет в индекс или удаляет из него выбранные изменённые строки diff.</summary>
    async void OnLinesAction(object? sender, RoutedEventArgs e) => await ApplySelection(wholeBlocks: false, discard: false);
    /// <summary>Запрашивает подтверждение перед отменой выбранных изменений строк в рабочей копии.</summary>
    async void OnDiscardLines(object? sender, RoutedEventArgs e) => await ApplySelection(wholeBlocks: false, discard: true);

    /// <summary>
    /// Преобразует выбранные строки diff в патч и применяет его к индексу (добавление),
    /// к индексу в обратном направлении (удаление из индекса в режиме Staged) либо к рабочей копии
    /// в обратном направлении (отмена в режиме Unstaged).
    /// </summary>
    async Task ApplySelection(bool wholeBlocks, bool discard)
    {
        var f = SingleSelectedFile();
        var diff = _shownDiff;
        var repo = _repo;
        if (repo == null || f == null || diff == null || diff.Raw.Length == 0 || !StagePanel.IsVisible) return;
        if (_diffMode == WorkTreeDiffMode.All || (discard && _diffMode != WorkTreeDiffMode.Unstaged)) return;

        var selectedRows = DiffList.Selection.SelectedIndexes.ToList();
        if (selectedRows.Count == 0)
        {
            await Dialogs.ShowMessage(this, "Partial staging", "Select lines in the diff first (Ctrl/Shift+click selects several).");
            return;
        }
        var lines = PatchBuilder.SelectLines(diff.Rows, selectedRows, wholeBlocks);
        var unstage = _diffMode == WorkTreeDiffMode.Staged;
        var patch = PatchBuilder.Build(diff.Raw, lines, reverse: discard || unstage);
        if (patch == null)
        {
            SetStatus("The selection contains no changed lines.");
            return;
        }
        if (discard && !await Dialogs.Confirm(this, "Discard lines",
                $"Revert {lines.Count} changed line(s) of {f.Name} in the working tree to the staged version? This cannot be undone.", "Discard"))
            return;

        string[] args = discard ? ["apply", "--reverse", "--recount", "--whitespace=nowarn"]
            : unstage ? ["apply", "--cached", "--reverse", "--recount", "--whitespace=nowarn"]
            : ["apply", "--cached", "--recount", "--whitespace=nowarn"];
        var done = discard ? "Discarded" : unstage ? "Unstaged" : "Staged";
        await Do(async () =>
        {
            var r = await repo.RunWithInput(patch, args);
            if (r.Ok) SetStatus($"{done} {lines.Count} line(s) of {f.RelPath}");
            else await Dialogs.ShowMessage(this, "git apply failed", r.Message);
        });
    }

    /// <summary>Создаёт контекстные действия diff для частичного добавления в индекс, удаления из индекса, отмены и копирования.</summary>
    void OnDiffContext(object? sender, ContextRequestedEventArgs e)
    {
        if (_shownDiff == null) return;
        if ((e.Source as Control)?.DataContext is DiffRow clicked && DiffList.SelectedItems?.Contains(clicked) != true)
            DiffList.SelectedItem = clicked;

        var menu = new ContextMenu();
        if (StagePanel.IsVisible && _diffMode != WorkTreeDiffMode.All)
        {
            var verb = _diffMode == WorkTreeDiffMode.Staged ? "Unstage" : "Stage";
            menu.Items.Add(Item($"{verb} block\tCtrl+B", () => ApplySelection(wholeBlocks: true, discard: false)));
            menu.Items.Add(Item($"{verb} selected lines\tCtrl+L", () => ApplySelection(wholeBlocks: false, discard: false)));
            if (_diffMode == WorkTreeDiffMode.Unstaged)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Discard block…", () => ApplySelection(wholeBlocks: true, discard: true)));
                menu.Items.Add(Item("Discard selected lines…", () => ApplySelection(wholeBlocks: false, discard: true)));
            }
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Item("Copy", CopySelectedDiffLines));
        menu.Open(DiffList);
        e.Handled = true;
    }

    /// <summary>Копирует выбранные строки diff в буфер обмена как обычный текст.</summary>
    Task CopySelectedDiffLines()
    {
        var rows = _shownDiff?.Rows;
        if (rows == null) return Task.CompletedTask;
        var text = DiffList.Selection.SelectedIndexes.Order()
            .Where(i => i < rows.Count)
            .Select(i => rows[i].HasRight ? rows[i].RightText : rows[i].LeftText);
        return CopyText(string.Join(Environment.NewLine, text));
    }
}
