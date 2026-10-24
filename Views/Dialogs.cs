using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClockworkGit.Views;

/// <summary>Small modal dialogs built in code: message, confirm, choice, prompt, commit.</summary>
public static class Dialogs
{
    static Window MakeWindow(string title, Control content, double width = 480) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = new SolidColorBrush(Color.Parse("#2b2d30")),
        Content = new Border { Padding = new Thickness(16), Child = content },
    };

    static StackPanel ButtonRow(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
        };
        row.Children.AddRange(buttons);
        return row;
    }

    static Button Btn(string text, bool isDefault = false, bool isCancel = false) =>
        new() { Content = text, IsDefault = isDefault, IsCancel = isCancel, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };

    public static async Task ShowMessage(Window owner, string title, string text)
    {
        var ok = Btn("OK", isDefault: true, isCancel: true);
        var panel = new StackPanel
        {
            Children =
            {
                new ScrollViewer
                {
                    MaxHeight = 420,
                    Content = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                },
                ButtonRow(ok),
            },
        };
        var w = MakeWindow(title, panel, 560);
        ok.Click += (_, _) => w.Close();
        await w.ShowDialog(owner);
    }

    public static async Task<bool> Confirm(Window owner, string title, string text, string okText = "OK")
    {
        var ok = Btn(okText, isDefault: true);
        var cancel = Btn("Cancel", isCancel: true);
        var panel = new StackPanel
        {
            Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, ButtonRow(ok, cancel) },
        };
        var w = MakeWindow(title, panel);
        ok.Click += (_, _) => w.Close(true);
        cancel.Click += (_, _) => w.Close(false);
        return await w.ShowDialog<bool?>(owner) == true;
    }

    /// <summary>Returns the index of the pressed button; the last button and closing the window mean cancel (-1).</summary>
    public static async Task<int> Choose(Window owner, string title, string text, params string[] buttons)
    {
        var btns = buttons.Select((b, i) => Btn(b, isDefault: i == 0, isCancel: i == buttons.Length - 1)).ToArray();
        var panel = new StackPanel
        {
            Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, ButtonRow(btns) },
        };
        var w = MakeWindow(title, panel, 560);
        for (var i = 0; i < btns.Length; i++)
        {
            var index = i == btns.Length - 1 ? -1 : i;
            btns[i].Click += (_, _) => w.Close(index);
        }
        return await w.ShowDialog<int?>(owner) ?? -1;
    }

    public static async Task<string?> Prompt(Window owner, string title, string label, string initial = "")
    {
        var box = new TextBox { Text = initial };
        var ok = Btn("OK", isDefault: true);
        var cancel = Btn("Cancel", isCancel: true);
        var panel = new StackPanel
        {
            Spacing = 6,
            Children = { new TextBlock { Text = label }, box, ButtonRow(ok, cancel) },
        };
        var w = MakeWindow(title, panel);
        ok.Click += (_, _) => w.Close(box.Text ?? "");
        cancel.Click += (_, _) => w.Close(null);
        w.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        return await w.ShowDialog<string?>(owner);
    }

    /// <param name="warningsFor">Pre-commit check results for "stage all" = true / false.</param>
    public static async Task<CommitRequest?> Commit(
        Window owner, int staged, int total, string? initialMessage, Func<Task<string>> lastMessage,
        Func<bool, IReadOnlyList<CommitWarning>> warningsFor)
    {
        var message = new TextBox
        {
            Text = initialMessage ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 160,
            Watermark = "Commit message (Ctrl+Enter to commit)",
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
        };
        var stageAll = new CheckBox
        {
            Content = $"Stage all changes ({total} files)",
            IsChecked = staged == 0,
            IsVisible = total > staged,
        };
        var amend = new CheckBox { Content = "Amend last commit" };
        var info = new TextBlock
        {
            Text = staged == 0 ? "Nothing is staged." : $"{staged} staged file(s) will be committed.",
            Foreground = Brushes.Gray,
        };
        var commit = Btn("Commit", isDefault: false);
        var commitPush = Btn("Commit & Push");
        var cancel = Btn("Cancel", isCancel: true);

        // Unity .meta and large-file warnings for what this commit would contain.
        var warningText = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
        var stageFixes = new CheckBox { IsChecked = true };
        var warningBox = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#4a3a12")),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 6),
            Child = new StackPanel { Spacing = 6, Children = { warningText, stageFixes } },
        };
        IReadOnlyList<CommitWarning> warnings = [];

        void ShowWarnings()
        {
            warnings = warningsFor(stageAll.IsVisible && stageAll.IsChecked == true);
            warningBox.IsVisible = warnings.Count > 0;
            const int shown = 8;
            warningText.Text = $"⚠ {warnings.Count} warning(s):\n" +
                               string.Join("\n", warnings.Take(shown).Select(x => $"• {x.Path} — {x.Message}")) +
                               (warnings.Count > shown ? $"\n… and {warnings.Count - shown} more" : "");
            var fixable = warnings.Where(x => x.IsFixable).Select(x => x.FixPath!).Distinct().Count();
            stageFixes.IsVisible = fixable > 0;
            stageFixes.Content = $"Also stage the {fixable} .meta file(s) that fix this";
        }

        stageAll.IsCheckedChanged += (_, _) => ShowWarnings();
        ShowWarnings();

        var panel = new StackPanel { Spacing = 8, Children = { info, message, stageAll, amend, warningBox, ButtonRow(commit, commitPush, cancel) } };
        var w = MakeWindow("Commit", panel, 720);

        amend.IsCheckedChanged += async (_, _) =>
        {
            if (amend.IsChecked == true && string.IsNullOrWhiteSpace(message.Text))
                message.Text = await lastMessage();
        };

        void Finish(bool push)
        {
            var text = message.Text?.Trim() ?? "";
            if (text.Length == 0) { message.Focus(); return; }
            var fixes = stageFixes.IsVisible && stageFixes.IsChecked == true
                ? warnings.Where(x => x.IsFixable).Select(x => x.FixPath!).Distinct().ToList()
                : [];
            w.Close(new CommitRequest(text, amend.IsChecked == true, stageAll.IsVisible && stageAll.IsChecked == true, push, fixes));
        }

        commit.Click += (_, _) => Finish(false);
        commitPush.Click += (_, _) => Finish(true);
        cancel.Click += (_, _) => w.Close(null);
        message.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; Finish(false); }
        }, RoutingStrategies.Tunnel);
        w.Opened += (_, _) => message.Focus();
        return await w.ShowDialog<CommitRequest?>(owner);
    }
}
