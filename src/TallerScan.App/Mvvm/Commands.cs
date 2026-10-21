using System.Windows.Input;
using TallerScan.App.Services;

namespace TallerScan.App.Mvvm;

// Comando asíncrono: se deshabilita mientras corre (evita doble toque) y muestra
// el error en pantalla en vez de cerrar la app.
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;
        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Ui.AlertAsync("Algo salió mal", ex.Message);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        if (MainThread.IsMainThread)
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        else
            MainThread.BeginInvokeOnMainThread(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
    }
}

public sealed class AsyncCommand<T>(Func<T, Task> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        if (parameter is not T value)
            return;
        try
        {
            await execute(value);
        }
        catch (Exception ex)
        {
            await Ui.AlertAsync("Algo salió mal", ex.Message);
        }
    }
}
