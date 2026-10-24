using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClockworkGit.Models;

/// <summary>Базовая реализация INotifyPropertyChanged для моделей и моделей представления с привязками.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    /// <summary>Событие вызывается после изменения свойства, связанного с интерфейсом.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Обновляет поле и вызывает <see cref="PropertyChanged"/> только при изменении значения.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    /// <summary>Вызывает <see cref="PropertyChanged"/> для указанного свойства.</summary>
    protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
