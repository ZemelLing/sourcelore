using System.ComponentModel;
using SourceLore.Core.Workspaces;

namespace SourceLore.App.ViewModels;

public sealed class FileChoice(FileChange file, bool initial, Action<bool> changed) : INotifyPropertyChanged
{
    private bool isChecked = initial;
    public FileChange File { get; } = file;
    public bool IsFile => !File.IsDirectory;
    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (value == isChecked) return;
            isChecked = value;
            PropertyChanged?.Invoke(this, new(nameof(IsChecked)));
            changed(value);
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
