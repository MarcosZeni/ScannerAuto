using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using TallerScan.App.Mvvm;
using TallerScan.App.Services;
using TallerScan.Infrastructure.Elm;

namespace TallerScan.App.ViewModels;

public sealed record TraceLine(string Time, string Command, string Response, string Status, Color StatusColor);

// Diagnóstico técnico: lo que se mandó al adaptador y lo que respondió. Solo lectura.
public sealed class TraceViewModel : ObservableObject
{
    private const int MaxVisible = 400;

    private readonly TraceLog _trace;
    private readonly ConcurrentQueue<TraceEntry> _incoming = new();
    private IDispatcherTimer? _timer;

    public TraceViewModel(TraceLog trace)
    {
        _trace = trace;
        _trace.Added += entry => _incoming.Enqueue(entry);
        ClearCommand = new AsyncCommand(() =>
        {
            _trace.Clear();
            Lines.Clear();
            return Task.CompletedTask;
        });
        ShareCommand = new AsyncCommand(ShareAsync);
    }

    public ObservableCollection<TraceLine> Lines { get; } = [];

    public AsyncCommand ClearCommand { get; }
    public AsyncCommand ShareCommand { get; }

    // Se agregan en tandas para no redibujar por cada respuesta.
    public void Attach()
    {
        if (_timer is not null || Application.Current?.Dispatcher is not { } dispatcher)
            return;
        Lines.Clear();
        foreach (var e in _trace.Snapshot().TakeLast(MaxVisible))
            Lines.Insert(0, ToLine(e));
        _incoming.Clear();
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += (_, _) => Flush();
        _timer.Start();
    }

    public void Detach()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void Flush()
    {
        while (_incoming.TryDequeue(out var e))
            Lines.Insert(0, ToLine(e));
        while (Lines.Count > MaxVisible)
            Lines.RemoveAt(Lines.Count - 1);
    }

    private static TraceLine ToLine(TraceEntry e)
    {
        var color = e.Status switch
        {
            "OK" => Color.FromArgb("#1E7A46"),
            "BLOQUEADO" => Color.FromArgb("#6B3FA0"),
            "DESCARTADA" => Color.FromArgb("#8A5A00"),
            _ => Color.FromArgb("#B42318"),
        };
        var response = e.Response.Replace("\r", " ⏎ ").Replace("\n", "");
        return new TraceLine(e.AtUtc.ToLocalTime().ToString("HH:mm:ss.fff"), e.Command, response, $"{e.Status} · {e.ElapsedMs} ms", color);
    }

    private Task ShareAsync()
    {
        var sb = new StringBuilder();
        foreach (var e in _trace.Snapshot())
            sb.AppendLine($"{e.AtUtc.ToLocalTime():HH:mm:ss.fff}\t{e.Status}\t{e.ElapsedMs}ms\t{e.Command}\t{e.Response.Replace("\r", "\\r").Replace("\n", "\\n")}");
        return Ui.ShareTextFileAsync($"traza-{DateTime.Now:yyyyMMdd-HHmmss}.txt", sb.ToString(), "Traza del adaptador");
    }
}
