using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TallerScan.App.Mvvm;

// MVVM mínimo propio: no hace falta un paquete para INotifyPropertyChanged y comandos.
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Los eventos de la sesión llegan desde hilos de fondo: la UI se toca solo en el principal.
    protected static void OnUi(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }
}
