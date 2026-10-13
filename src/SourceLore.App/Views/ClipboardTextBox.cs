using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceLore.App.Views;

// Use the same Windows text clipboard boundary for path/message copy and paste as for diff copy.
public sealed class ClipboardTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    public ClipboardTextBox()
    {
        AddHandler(KeyDownEvent, HandleClipboardKey, RoutingStrategies.Tunnel);
        var copy = new MenuItem { Header = "复制" };
        var cut = new MenuItem { Header = "剪切" };
        var paste = new MenuItem { Header = "粘贴" };
        copy.Click += async (_, _) => await ObserveAsync(() => CopySelectionAsync(false));
        cut.Click += async (_, _) => await ObserveAsync(() => CopySelectionAsync(true));
        paste.Click += async (_, _) => await ObserveAsync(PasteSelectionAsync);
        ContextMenu = new ContextMenu { ItemsSource = new[] { copy, cut, paste } };
    }
    private nint Owner => TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("文本窗口尚未就绪。");

    private async void HandleClipboardKey(object? sender, KeyEventArgs args)
    {
        if (args.KeyModifiers != KeyModifiers.Control || args.Key is not (Key.C or Key.X or Key.V)) return;
        args.Handled = true;
        await ObserveAsync(args.Key == Key.V ? PasteSelectionAsync : () => CopySelectionAsync(args.Key == Key.X));
    }
    private async Task ObserveAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception error) { ToolTip.SetTip(this, error.Message); }
    }
    public async Task CopySelectionAsync(bool cut)
    {
        if (string.IsNullOrEmpty(SelectedText) || !IsEnabled) return;
        var original = Text;
        var start = SelectionStart; var end = SelectionEnd;
        await WindowsTextClipboard.SetAsync(Owner, SelectedText);
        if (cut && !IsReadOnly && IsEnabled && Text == original && SelectionStart == start && SelectionEnd == end) SelectedText = "";
    }
    public async Task PasteSelectionAsync()
    {
        if (IsReadOnly || !IsEnabled) return;
        var original = Text;
        var start = SelectionStart; var end = SelectionEnd;
        var text = await WindowsTextClipboard.ReadAsync(Owner);
        if (text is not null && !IsReadOnly && IsEnabled && Text == original && SelectionStart == start && SelectionEnd == end) SelectedText = text;
    }
}
