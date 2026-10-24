namespace ClockworkGit.Utils;

/// <summary>Сортировка без учёта регистра, сравнивающая последовательности цифр как числа: Site2 перед Site10.</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    /// <summary>Сравнивает строки без учёта регистра, воспринимая последовательности цифр как числовые значения.</summary>
    public int Compare(string? a, string? b)
    {
        if (a == null || b == null) return a == null ? (b == null ? 0 : -1) : 1;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var na = a.AsSpan(si, i - si).TrimStart('0');
                var nb = b.AsSpan(sj, j - sj).TrimStart('0');
                var c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : na.CompareTo(nb, StringComparison.Ordinal);
                if (c != 0) return c;
            }
            else
            {
                var c = string.Compare(a, i, b, j, 1, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
