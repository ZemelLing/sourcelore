using Avalonia;
using AvaloniaEdit;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace SourceLore.App.Views;

/// <summary>A compiled-bindable source for AvaloniaEdit's CLR-only Text property.</summary>
public sealed class DiffTextEditor : TextEditor
{
    private readonly SemaphoreSlim copyGate = new(1, 1);
    protected override Type StyleKeyOverride => typeof(TextEditor);
    public static readonly StyledProperty<string> SourceTextProperty = AvaloniaProperty.Register<DiffTextEditor, string>(nameof(SourceText), "");
    public string SourceText { get => GetValue(SourceTextProperty); set => SetValue(SourceTextProperty, value); }

    public DiffTextEditor()
    {
        TextArea.TextView.LineTransformers.Add(new DiffColors());
        AddHandler(KeyDownEvent, CopyKey, RoutingStrategies.Tunnel);
        var copy = new MenuItem { Header = "复制" };
        copy.Click += async (_, _) => await CopyFromUiAsync();
        ContextMenu = new ContextMenu { ItemsSource = new[] { copy } };
    }

    private sealed class DiffColors : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line.Offset, Math.Min(2, line.Length));
            var brush = text switch { "+ " => Brushes.LightGreen, "- " => Brushes.Salmon, _ => null };
            if (brush is not null) ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private async void CopyKey(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.C || args.KeyModifiers != KeyModifiers.Control) return;
        args.Handled = true;
        await CopyFromUiAsync();
    }

    private async Task CopyFromUiAsync()
    {
        try { await CopySelectionAsync(); }
        catch (Exception error) { ToolTip.SetTip(this, "复制失败：" + error.Message); }
    }

    public async Task CopySelectionAsync()
    {
        if (SelectionLength == 0) return;
        var window = TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("剪贴板不可用。");
        var text = SelectedText.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
        await copyGate.WaitAsync();
        try
        {
            await WindowsTextClipboard.SetAsync(window.TryGetPlatformHandle()?.Handle ?? 0, text);
        }
        finally { copyGate.Release(); }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceTextProperty) Text = SourceText;
    }
}
