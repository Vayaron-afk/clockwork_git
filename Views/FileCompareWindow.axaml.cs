using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClockworkGit.Views;

/// <summary>Сравнение файла целиком (аналог «File Compare» в SmartGit): текст или изображение.</summary>
public partial class FileCompareWindow : Window
{
    const double RowHeight = 17;

    readonly GitRepo _repo = null!;
    readonly CommitInfo _owner = null!;
    readonly FileChange _file = null!;
    readonly bool _hasHead;
    List<DiffRow> _rows = new();
    List<int> _changeStarts = new();
    ScrollViewer? _scroll;
    int _loadGen;

    /// <summary>Инициализирует элементы XAML окна сравнения файлов целиком.</summary>
    public FileCompareWindow()
    {
        InitializeComponent();
    }

    /// <summary>Создаёт представление сравнения изменённого файла и его старой и новой ревизий.</summary>
    public FileCompareWindow(GitRepo repo, CommitInfo owner, FileChange file, bool hasHead, bool svgAsText) : this()
    {
        _repo = repo;
        _owner = owner;
        _file = file;
        _hasHead = hasHead;

        var repoName = Path.GetFileName(repo.Path.TrimEnd('\\', '/'));
        Title = $"[{repoName}/{file.RelPath}] - File Compare";
        var leftLabel = owner.IsWorkingTree ? "HEAD" : owner.Parents.Length > 0 ? $"Before, {owner.Parents[0][..8]}" : "Before";
        var rightLabel = owner.IsWorkingTree ? "Working Tree" : owner.Hash[..8];
        LeftHeader.Text = $"{file.OldPath ?? file.RelPath} ({leftLabel})";
        RightHeader.Text = $"{file.RelPath} ({rightLabel})";

        var textImage = ImageFormats.IsTextImage(file.RelPath);
        ModeImage.IsVisible = ModeText.IsVisible = textImage;
        if (textImage && svgAsText) ModeText.IsChecked = true;
        else ModeImage.IsChecked = true;

        ModeImage.IsCheckedChanged += async (_, _) => await Load();
        IgnoreWs.IsCheckedChanged += async (_, _) => await Load();
        Ruler.RowRequested += i => JumpTo(i);
        Rows.SelectionChanged += (_, _) => UpdateStatus();
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += async (_, _) => await Load();
    }

    /// <summary>Показывает, отображается ли файл в текущем режиме как изображение.</summary>
    bool ShowsImage => ImageFormats.IsImage(_file.RelPath) && ModeText.IsChecked != true;

    /// <summary>Загружает строки diff всего файла или декодированные версии изображения и обновляет шкалу изменений.</summary>
    async Task Load()
    {
        var gen = ++_loadGen;
        var image = ShowsImage;
        TextPanel.IsVisible = !image;
        ImageView.IsVisible = image;
        PrevButton.IsEnabled = NextButton.IsEnabled = IgnoreWs.IsEnabled = !image;
        StatusText.Text = "Loading…";

        if (image)
        {
            ChangeCounter.Text = "";
            StatusRight.Text = "";
            var (oldBytes, newBytes) = await _repo.LoadVersions(_owner, _file, _hasHead);
            if (gen != _loadGen) return;
            await ImageView.ShowAsync(oldBytes, newBytes, _file.RelPath);
            StatusText.Text = _file.StatusText;
            return;
        }

        var rows = (await _repo.DiffRows(_owner, _file, _hasHead, fullFile: true, ignoreWhitespace: IgnoreWs.IsChecked == true)).Rows;
        if (gen != _loadGen) return;
        _rows = rows;
        _changeStarts = Enumerable.Range(0, rows.Count)
            .Where(i => rows[i].Kind == DiffRowKind.Change && (i == 0 || rows[i - 1].Kind != DiffRowKind.Change))
            .ToList();
        Rows.ItemsSource = rows;
        Ruler.Rows = rows;

        var added = rows.Count(r => r.Kind == DiffRowKind.Change && r.HasRight);
        var removed = rows.Count(r => r.Kind == DiffRowKind.Change && r.HasLeft);
        StatusRight.Text = $"{_changeStarts.Count} change(s) · +{added} −{removed} lines";
        StatusText.Text = _file.StatusText;
        UpdateCounter(-1);

        // Scrolling needs the list laid out first.
        Dispatcher.UIThread.Post(() =>
        {
            AttachScroll();
            if (_changeStarts.Count > 0) JumpTo(_changeStarts[0]);
        }, DispatcherPriority.Background);
    }

    /// <summary>Находит область прокрутки списка и синхронизирует с ней область просмотра обзорной шкалы.</summary>
    void AttachScroll()
    {
        if (_scroll != null) return;
        _scroll = Rows.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scroll == null) return;
        _scroll.ScrollChanged += (_, _) =>
        {
            Ruler.ViewportStart = _scroll.Offset.Y / RowHeight;
            Ruler.ViewportCount = _scroll.Viewport.Height / RowHeight;
        };
    }

    /// <summary>Индекс самой верхней видимой строки diff.</summary>
    int TopIndex => _scroll == null ? 0 : (int)(_scroll.Offset.Y / RowHeight);
    /// <summary>Выбранная строка diff или самая верхняя видимая строка, если ничего не выбрано.</summary>
    int CurrentIndex => Rows.SelectedIndex >= 0 ? Rows.SelectedIndex : TopIndex;

    /// <summary>Выбирает строку и прокручивает к ней так, чтобы она оказалась в верхней трети области просмотра.</summary>
    void JumpTo(int index)
    {
        if (_rows.Count == 0) return;
        index = Math.Clamp(index, 0, _rows.Count - 1);
        AttachScroll();
        if (_scroll != null)
            _scroll.Offset = new Vector(0, Math.Max(0, index * RowHeight - _scroll.Viewport.Height / 3));
        else
            Rows.ScrollIntoView(index);
        Rows.SelectedIndex = index;
        UpdateCounter(index);
    }

    /// <summary>Переходит к следующему блоку изменений.</summary>
    void OnNext(object? sender, RoutedEventArgs e) => Next();
    /// <summary>Переходит к предыдущему блоку изменений.</summary>
    void OnPrev(object? sender, RoutedEventArgs e) => Prev();

    /// <summary>Переходит к первому блоку изменений после текущей строки.</summary>
    void Next()
    {
        var current = CurrentIndex;
        var next = _changeStarts.FirstOrDefault(s => s > current, -1);
        if (next >= 0) JumpTo(next);
    }

    /// <summary>Переходит к последнему блоку изменений перед текущей строкой.</summary>
    void Prev()
    {
        var current = CurrentIndex;
        var prev = _changeStarts.LastOrDefault(s => s < current, -1);
        if (prev >= 0) JumpTo(prev);
    }

    /// <summary>Отображает номер текущего блока изменений или сообщает об отсутствии текстовых изменений.</summary>
    void UpdateCounter(int index)
    {
        if (_changeStarts.Count == 0)
        {
            ChangeCounter.Text = ShowsImage ? "" : "No changes";
            return;
        }
        var k = _changeStarts.FindLastIndex(s => s <= index);
        ChangeCounter.Text = k < 0 ? $"{_changeStarts.Count} change(s)" : $"Change {k + 1} of {_changeStarts.Count}";
    }

    /// <summary>Показывает номера выбранных строк старой и новой версий и обновляет счётчик изменений.</summary>
    void UpdateStatus()
    {
        if (Rows.SelectedItem is not DiffRow row) return;
        var parts = new List<string>();
        if (row.HasLeft) parts.Add($"Left line {row.LeftNo}");
        if (row.HasRight) parts.Add($"Right line {row.RightNo}");
        StatusText.Text = parts.Count > 0 ? string.Join(" · ", parts) : _file.StatusText;
        UpdateCounter(Rows.SelectedIndex);
    }

    /// <summary>Обрабатывает сочетания клавиш для навигации по изменениям, копирования в буфер обмена и закрытия окна.</summary>
    async void OnKey(object? sender, KeyEventArgs e)
    {
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if ((e.Key == Key.F7 && !shift) || (alt && e.Key == Key.Down)) { e.Handled = true; Next(); }
        else if ((e.Key == Key.F7 && shift) || (alt && e.Key == Key.Up)) { e.Handled = true; Prev(); }
        else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        else if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control && Clipboard != null)
        {
            var lines = Rows.Selection.SelectedIndexes.Order()
                .Select(i => _rows[i])
                .Select(r => r.HasRight ? r.RightText : r.LeftText);
            e.Handled = true;
            await Clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
        }
    }
}
