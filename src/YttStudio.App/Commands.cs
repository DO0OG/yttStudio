using System.Windows.Input;

namespace YttStudio.App;

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool executing;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !executing && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        executing = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute();
        }
        finally
        {
            executing = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
