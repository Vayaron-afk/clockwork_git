using System.Collections.ObjectModel;
using Avalonia.Media;

namespace ClockworkGit.ViewModels;

/// <summary>Узел дерева веток: группа/папка, HEAD, ветка, тег, stash или восстановимый коммит.</summary>
public sealed class BranchNode : Observable
{
    /// <summary>Стабильный идентификатор между обновлениями, используемый для запоминания раскрытых узлов.</summary>
    public required string Key { get; init; }
    /// <summary>Тип узла, определяющий его значок и действия контекстного меню.</summary>
    public required NodeKind Kind { get; init; }
    /// <summary>Основная подпись в дереве веток.</summary>
    public string Title { get; init; } = "";
    /// <summary>Дополнительная информация, например количество опережающих и отстающих коммитов.</summary>
    public string Detail { get; init; } = "";
    /// <summary>Аргумент git log: полная ссылка, HEAD или хеш восстановимого коммита; null для групп и stash.</summary>
    public string? FullRef { get; init; }
    /// <summary>Имя для команд Git: «main», «origin/main», «v1.0», «stash@{0}».</summary>
    public string? ShortName { get; init; }
    /// <summary>Имя удалённого репозитория для узла удалённой ветки, если применимо.</summary>
    public string? Remote { get; init; }
    /// <summary>Помечает текущую локальную ветку, на которую указывает HEAD; не связано с выделением узла мышью.</summary>
    public bool IsCurrent { get; init; }
    /// <summary>Дочерние узлы папок и группированных имён веток.</summary>
    public ObservableCollection<BranchNode> Children { get; } = new();

    /// <summary>Включать и отключать в графе можно только ссылки.</summary>
    public bool CanCheck => FullRef != null;
    /// <summary>Выделяет текущую ссылку и группы верхнего уровня.</summary>
    public FontWeight Weight => IsCurrent || Kind == NodeKind.Group && Key.IndexOf('/') < 0 ? FontWeight.SemiBold : FontWeight.Normal;

    bool _isChecked;
    /// <summary>Показывает, включена ли история этой ссылки в граф коммитов.</summary>
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    bool _isExpanded;
    /// <summary>Показывает, раскрыты ли дочерние узлы в дереве веток.</summary>
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    /// <summary>Задаёт начальное состояние без уведомления об изменениях.</summary>
    public void InitState(bool isChecked, bool isExpanded)
    {
        _isChecked = isChecked;
        _isExpanded = isExpanded;
    }
}
