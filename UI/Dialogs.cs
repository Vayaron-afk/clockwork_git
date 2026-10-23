using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClockworkGit.UI;

public sealed record CommitRequest(string Message, bool Amend, bool StageAll, bool Push);

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

    public static async Task<CommitRequest?> Commit(
        Window owner, int staged, int total, string? initialMessage, Func<Task<string>> lastMessage)
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

        var panel = new StackPanel { Spacing = 8, Children = { info, message, stageAll, amend, ButtonRow(commit, commitPush, cancel) } };
        var w = MakeWindow("Commit", panel, 620);

        amend.IsCheckedChanged += async (_, _) =>
        {
            if (amend.IsChecked == true && string.IsNullOrWhiteSpace(message.Text))
                message.Text = await lastMessage();
        };

        void Finish(bool push)
        {
            var text = message.Text?.Trim() ?? "";
            if (text.Length == 0) { message.Focus(); return; }
            w.Close(new CommitRequest(text, amend.IsChecked == true, stageAll.IsVisible && stageAll.IsChecked == true, push));
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
