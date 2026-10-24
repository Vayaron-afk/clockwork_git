using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ClockworkGit.Views;

/// <summary>
/// Three-way conflict editor: the stages are merged again with diff3, clean parts are kept,
/// and each conflict is resolved by picking or editing. Binary files and missing sides are resolved as a whole.
/// Returns true from ShowDialog when the file was saved.
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

    public ConflictEditorWindow()
    {
        InitializeComponent();
    }

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
        // git wrote the conflicted file with the working tree's line endings; keep them.
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

    void UpdateCounter()
    {
        var open = _conflicts.Count(c => !c.IsResolved);
        Counter.Text = _conflicts.Count == 0 ? "No conflicts left"
            : open == 0 ? $"All {_conflicts.Count} resolved"
            : $"{open} of {_conflicts.Count} unresolved";
        SaveResolvedButton.IsEnabled = open == 0;
    }

    // ───────────── Navigation and bulk choices ─────────────

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

    void OnNext(object? sender, RoutedEventArgs e) => GoTo(1);
    void OnPrev(object? sender, RoutedEventArgs e) => GoTo(-1);

    void OnTake(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string choice, DataContext: ConflictSegment conflict }) conflict.Take(choice);
    }

    void OnAllOurs(object? sender, RoutedEventArgs e)
    {
        foreach (var c in _conflicts.Where(c => !c.IsResolved)) c.Take("ours");
    }

    void OnAllTheirs(object? sender, RoutedEventArgs e)
    {
        foreach (var c in _conflicts.Where(c => !c.IsResolved)) c.Take("theirs");
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F7) { e.Handled = true; GoTo(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); }
        else if (e.Key == Key.Escape) { e.Handled = true; Close(false); }
    }

    // ───────────── Saving ─────────────

    string BuildResult() =>
        string.Join("\n", _segments.SelectMany(s => s.ResultLines).Select(l => l.TrimEnd('\r'))) + (_endsWithNewline ? "\n" : "");

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

    async void OnSaveResolved(object? sender, RoutedEventArgs e) => await Save(markResolved: true);

    async void OnSaveWithMarkers(object? sender, RoutedEventArgs e) => await Save(markResolved: false);

    void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    // ───────────── Binary files, deleted sides ─────────────

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

    async Task TakeWholeFile(byte[]? side, string flag)
    {
        var r = side == null
            ? await _repo.Run("rm", "-q", "--", _path)
            : await _repo.Run("checkout", flag, "--", _path);
        if (r.Ok && side != null) r = await _repo.Run("add", "--", _path);
        if (r.Ok) Close(true);
        else await Dialogs.ShowMessage(this, "git failed", r.Message);
    }

    async void OnTakeOursFile(object? sender, RoutedEventArgs e) => await TakeWholeFile(_sides.Ours, "--ours");
    async void OnTakeTheirsFile(object? sender, RoutedEventArgs e) => await TakeWholeFile(_sides.Theirs, "--theirs");
}
