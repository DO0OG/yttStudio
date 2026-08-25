using System.Windows.Input;

namespace YttStudio.App;

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool executing;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !executing && (canExecute?.Invoke() ?? true);

    /// <summary>
    /// <see cref="ICommand"/> 는 void 반환을 강제하므로
    /// 실제 작업은 <see cref="ExecuteAsync"/> 에 두고 여기서 그 실패를 관찰한다.
    /// 연속 작업이 없으면 대기한 작업의 예외가 관찰되지 않은 채
    /// 프로세스를 내릴 수 있다.
    /// </summary>
    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _ = ExecuteAsync().ContinueWith(
            task => Serilog.Log.Error(task.Exception, "Async command failed"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    /// <summary>재진입 방지 상태를 유지한 채 커맨드 본문을 실행한다.</summary>
    public async Task ExecuteAsync()
    {
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

public sealed class DelegateCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
