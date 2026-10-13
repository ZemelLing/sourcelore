using System.Windows.Input;

namespace SourceLore.App.ViewModels;

public sealed class AsyncCommand(Func<Task> action, Func<bool> enabled, Action<Exception> failure) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => enabled();
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        try { await action(); }
        catch (Exception error) { failure(error); }
    }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
