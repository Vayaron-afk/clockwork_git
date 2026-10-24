using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ClockworkGit.Views;

/// <summary>
/// Редактор трёхсторонних конфликтов: стадии повторно объединяются с помощью diff3, бесконфликтные фрагменты сохраняются,
/// а каждый конфликт разрешается выбором версии или редактированием. Двоичные файлы и отсутствующие стороны разрешаются целиком.
/// ShowDialog возвращает true, если файл сохранён.
/// </summary>
public partial class ConflictEditorWindow : Window
{
    readonly GitRepo _repo = null!;
    readonly string _path = "";
    readonly ConflictSides _sides = null!;
    readonly string _oursLabel = "", _theirsLabel = "";
    readonly List<MergeSegment> _segments = new();
    readonly List<ConflictSegment> _conflicts = new();
    bool _endsWithNewline, _crlf, _bom;
    int _current = -1;

    /// <summary>Инициализирует элементы XAML редактора конфликтов.</summary>
    public ConflictEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>Создаёт редактор конфликта указанного файла и задаёт подписи сторон с учётом текущей операции Git.</summary>
    public ConflictEditorWindow(GitRepo repo, string path, ConflictSides sides, string oursLabel, string theirsLabel) : this()
    {
        _repo = repo;
        _path = path;
        _sides = sides;
        _oursLabel = oursLabel;
        _theirsLabel = theirsLabel;
        Title = $"[{path}] - Resolve Conflict";
        AllOursButton.Content = "All conflicts: ours";
        AllTheirsButton.Content = "All conflicts: theirs";
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += async (_, _) => await Load();
    }

    /// <summary>Выполняет слияние текста, создаёт редактируемые фрагменты конфликтов или переключается на разрешение файла целиком.</summary>
    async Task Load()
    {
        var textual = !_sides.IsBinary && _sides.HasBothSides && (!ImageFormats.IsImage(_path) || ImageFormats.IsTextImage(_path));
        var merged = textual ? await _repo.MergeFile(_sides) : null;
        if (merged == null)
        {
            await ShowWholeFileChoice();
            return;
        }

        _bom = merged.StartsWith('﻿');
        if (_bom) merged = merged[1..];
        // Выбираем CRLF, если он встречается в текущем файле на диске; BOM определён выше по результату merge-file.
        var full = _repo.FullPath(_path);
        _crlf = File.Exists(full) && (await File.ReadAllTextAsync(full)).Contains("\r\n");

        foreach (var chunk in MergeFileParser.Parse(merged, out _endsWithNewline))
        {
            if (!chunk.IsConflict)
            {
                _segments.Add(new CommonSegment(chunk.Common));
                continue;
            }
            var conflict = new ConflictSegment(_conflicts.Count + 1, chunk.Ours!, chunk.Base!, chunk.Theirs!, _oursLabel, _theirsLabel);
            conflict.PropertyChanged += (_, _) => UpdateCounter();
            _conflicts.Add(conflict);
            _segments.Add(conflict);
        }
        Segments.ItemsSource = _segments;
        UpdateCounter();
        StatusText.Text = _conflicts.Count == 0
            ? "git merged all lines on its own; Save & mark resolved writes the merged result."
            : $"{_path}: changes that did not overlap are already merged; {_conflicts.Count} conflict(s) need a decision.";
    }

    /// <summary>Обновляет счётчик неразрешённых конфликтов и разрешает сохранение, только когда разрешены все текстовые конфликты.</summary>
    void UpdateCounter()
    {
        var open = _conflicts.Count(c => !c.IsResolved);
        Counter.Text = _conflicts.Count == 0 ? "No conflicts left"
            : open == 0 ? $"All {_conflicts.Count} resolved"
            : $"{open} of {_conflicts.Count} unresolved";
        SaveResolvedButton.IsEnabled = open == 0;
    }

    // ───────────── Navigation and bulk choices ─────────────

    /// <summary>Переходит к следующему или предыдущему неразрешённому конфликту и прокручивает к нему.</summary>
    void GoTo(int direction)
    {
        if (_conflicts.Count == 0) return;
        var order = direction > 0
            ? Enumerable.Range(1, _conflicts.Count).Select(i => (_current + i) % _conflicts.Count)
            : Enumerable.Range(1, _conflicts.Count).Select(i => ((_current - i) % _conflicts.Count + _conflicts.Count) % _conflicts.Count);
        var next = order.Cast<int?>().FirstOrDefault(i => !_conflicts[i!.Value].IsResolved) ?? (direction > 0 ? 0 : _conflicts.Count - 1);
        _current = next;
        Segments.ContainerFromItem(_conflicts[next])?.BringIntoView();
        StatusText.Text = $"Conflict {next + 1} of {_conflicts.Count}";
    }

    /// <summary>Переходит к следующему неразрешённому конфликту.</summary>
    void OnNext(object? sender, RoutedEventArgs e) => GoTo(1);
    /// <summary>Переходит к предыдущему неразрешённому конфликту.</summary>
    void OnPrev(object? sender, RoutedEventArgs e) => GoTo(-1);

    /// <summary>Применяет выбранную сторону или порядок сторон к конфликту, связанному с нажатой кнопкой.</summary>
    void OnTake(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string choice, DataContext: ConflictSegment conflict }) conflict.Take(choice);
    }

    /// <summary>Разрешает все открытые конфликты выбором нашей версии.</summary>
    void OnAllOurs(object? sender, RoutedEventArgs e)
    {
        foreach (var c in _conflicts.Where(c => !c.IsResolved)) c.Take("ours");
    }

    /// <summary>Разрешает все открытые конфликты выбором их версии.</summary>
    void OnAllTheirs(object? sender, RoutedEventArgs e)
    {
        foreach (var c in _conflicts.Where(c => !c.IsResolved)) c.Take("theirs");
    }

    /// <summary>Обрабатывает сочетания клавиш для перехода между конфликтами и отмену по Escape.</summary>
    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F7) { e.Handled = true; GoTo(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); }
        else if (e.Key == Key.Escape) { e.Handled = true; Close(false); }
    }

    // ───────────── Saving ─────────────

    /// <summary>Объединяет разрешённые и бесконфликтные фрагменты, сохраняя исходное наличие перевода строки в конце.</summary>
    string BuildResult() =>
        string.Join("\n", _segments.SelectMany(s => s.ResultLines).Select(l => l.TrimEnd('\r'))) + (_endsWithNewline ? "\n" : "");

    /// <summary>Записывает собранное решение, при необходимости добавляет его в индекс и закрывает окно с успешным результатом.</summary>
    async Task Save(bool markResolved)
    {
        try
        {
            await _repo.SaveResolution(_path, BuildResult(), _crlf, _bom, stage: markResolved);
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.ShowMessage(this, "Saving failed", ex.Message);
        }
    }

    /// <summary>Сохраняет решение и отмечает файл разрешённым, добавляя его в индекс.</summary>
    async void OnSaveResolved(object? sender, RoutedEventArgs e) => await Save(markResolved: true);

    /// <summary>Сохраняет текущий текст, не добавляя файл в индекс как разрешённый.</summary>
    async void OnSaveWithMarkers(object? sender, RoutedEventArgs e) => await Save(markResolved: false);

    /// <summary>Закрывает редактор конфликтов без сохранения.</summary>
    void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    // ───────────── Binary files, deleted sides ─────────────

    /// <summary>Отображает элементы выбора версии для двоичных конфликтов или конфликтов с одной отсутствующей стороной.</summary>
    async Task ShowWholeFileChoice()
    {
        TextPanel.IsVisible = false;
        WholeFilePanel.IsVisible = true;
        SaveResolvedButton.IsVisible = SaveMarkersButton.IsVisible = false;
        AllOursButton.IsVisible = AllTheirsButton.IsVisible = false;

        string Side(byte[]? bytes) => bytes == null ? "deleted" : Format.Size(bytes.Length);
        var why = _sides.IsBinary ? "This is a binary file, so lines cannot be merged."
            : !_sides.HasBothSides ? "One side deleted the file while the other changed it."
            : "This file cannot be merged line by line.";
        WholeFileText.Text = $"{why} Choose the version to keep.\n" +
                             $"{_oursLabel}: {Side(_sides.Ours)}     {_theirsLabel}: {Side(_sides.Theirs)}";
        TakeOursFileButton.Content = _sides.Ours == null ? $"Keep it deleted ({_oursLabel})" : $"Take {_oursLabel}";
        TakeTheirsFileButton.Content = _sides.Theirs == null ? $"Keep it deleted ({_theirsLabel})" : $"Take {_theirsLabel}";
        Counter.Text = "1 file-level conflict";
        StatusText.Text = "";

        if (ImageFormats.IsImage(_path))
        {
            ImageView.IsVisible = true;
            await ImageView.ShowAsync(_sides.Ours, _sides.Theirs, _path);
        }
    }

    /// <summary>Заменяет или удаляет файл рабочей копии согласно выбранной стороне, затем добавляет результат в индекс.</summary>
    async Task TakeWholeFile(byte[]? side, string flag)
    {
        var r = side == null
            ? await _repo.Run("rm", "-q", "--", _path)
            : await _repo.Run("checkout", flag, "--", _path);
        if (r.Ok && side != null) r = await _repo.Run("add", "--", _path);
        if (r.Ok) Close(true);
        else await Dialogs.ShowMessage(this, "git failed", r.Message);
    }

    /// <summary>Сохраняет нашу версию или состояние её удаления для конфликта файла целиком.</summary>
    async void OnTakeOursFile(object? sender, RoutedEventArgs e) => await TakeWholeFile(_sides.Ours, "--ours");
    /// <summary>Сохраняет их версию или состояние её удаления для конфликта файла целиком.</summary>
    async void OnTakeTheirsFile(object? sender, RoutedEventArgs e) => await TakeWholeFile(_sides.Theirs, "--theirs");
}
