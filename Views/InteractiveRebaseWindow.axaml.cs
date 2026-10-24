using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Views;

/// <summary>Редактор плана git rebase -i; возвращает план от старых коммитов к новым или null при отмене.</summary>
public partial class InteractiveRebaseWindow : Window
{
    static readonly IBrush ErrorBrush = new ImmutableSolidColorBrush(Color.Parse("#e06060"));
    static readonly IBrush InfoBrush = new ImmutableSolidColorBrush(Color.Parse("#9aa0a6"));

    readonly ObservableCollection<RebaseItem> _items = new();
    readonly List<string> _originalOldestFirst = new();

    /// <summary>Инициализирует элементы XAML диалога интерактивного rebase.</summary>
    public InteractiveRebaseWindow()
    {
        InitializeComponent();
    }

    /// <summary>Создаёт редактор плана rebase по коммитам от новых к старым и необязательным контексту и предупреждениям.</summary>
    public InteractiveRebaseWindow(IEnumerable<RebaseItem> newestFirst, string header, string? warning) : this()
    {
        foreach (var item in newestFirst)
        {
            _items.Add(item);
            item.PropertyChanged += (_, _) => UpdateSummary();
        }
        _originalOldestFirst = _items.Reverse().Select(i => i.Commit.Hash).ToList();
        Items.ItemsSource = _items;
        Items.SelectedIndex = 0;
        HeaderText.Text = header;
        WarningBox.IsVisible = warning != null;
        WarningText.Text = warning;
        UpdateSummary();

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Alt) return;
            if (e.Key == Key.Up) { e.Handled = true; Move(-1); }
            else if (e.Key == Key.Down) { e.Handled = true; Move(1); }
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>Возвращает текущий редактируемый план в требуемом Git порядке от старых коммитов к новым.</summary>
    List<RebaseStep> StepsOldestFirst() => _items.Reverse().Select(i => i.ToStep()).ToList();

    /// <summary>Проверяет план и обновляет сводку действий и состояние кнопки запуска.</summary>
    void UpdateSummary()
    {
        var steps = StepsOldestFirst();
        var error = RebaseTodo.Validate(steps);
        var noOp = RebaseTodo.IsNoOp(steps, _originalOldestFirst);
        var counts = steps.GroupBy(s => s.Action).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}");
        SummaryText.Text = error ?? (noOp ? "Nothing changed yet." : string.Join(", ", counts) + (OrderChanged(steps) ? ", reordered" : ""));
        SummaryText.Foreground = error != null ? ErrorBrush : InfoBrush;
        StartButton.IsEnabled = error == null && !noOp;
    }

    /// <summary>Проверяет, изменён ли исходный порядок коммитов от старых к новым.</summary>
    bool OrderChanged(List<RebaseStep> steps) => !steps.Select(s => s.Hash).SequenceEqual(_originalOldestFirst);

    /// <summary>Перемещает выбранный коммит на одну позицию и обновляет сводку плана.</summary>
    void Move(int delta)
    {
        var from = Items.SelectedIndex;
        var to = from + delta;
        if (from < 0 || to < 0 || to >= _items.Count) return;
        _items.Move(from, to);
        Items.SelectedIndex = to;
        UpdateSummary();
    }

    /// <summary>Перемещает выбранный коммит на одну позицию вверх в списке.</summary>
    void OnMoveUp(object? sender, RoutedEventArgs e) => Move(-1);
    /// <summary>Перемещает выбранный коммит на одну позицию вниз в списке.</summary>
    void OnMoveDown(object? sender, RoutedEventArgs e) => Move(1);
    /// <summary>Закрывает диалог и возвращает проверенный план от старых коммитов к новым.</summary>
    void OnStart(object? sender, RoutedEventArgs e) => Close(StepsOldestFirst());
    /// <summary>Закрывает диалог, не возвращая план rebase.</summary>
    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
