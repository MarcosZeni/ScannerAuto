using TallerScan.Core.Actuators;
using TallerScan.Core.Diagnostics;
using TallerScan.Core.Models;
using TallerScan.Infrastructure.Elm;
using TallerScan.Infrastructure.Uds;

namespace TallerScan.Infrastructure.Actuators;

public sealed record ActuatorEvent(DateTimeOffset AtUtc, string Test, string Step, bool Ok, string Message);

// Corre pruebas de actuadores de un perfil sobre la sesión ELM: abre el alcance de
// escritura, direcciona el módulo, abre la sesión extendida, manda el paso, mantiene
// la sesión viva ("sigo acá") y apaga solo al vencer el tiempo o al salir. Una prueba a la vez.
public sealed class ActuatorRunner : IAsyncDisposable
{
    private readonly Elm327Backend _backend;
    private readonly ActuatorProfile _profile;
    private readonly UdsClient _uds;
    private readonly IDisposable _scope;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ActuatorEvent> _events = [];
    private CancellationTokenSource? _keepAlive;
    private bool _sessionOpen;
    private bool _disposed;

    public ActuatorRunner(Elm327Backend backend, ActuatorProfile profile)
    {
        _backend = backend;
        _profile = profile;
        _uds = backend.CreateUds();
        _scope = backend.OpenScope(UdsClient.ScopeFor(profile, $"Pruebas: {profile.Name}"));
    }

    public ActuatorProfile Profile => _profile;

    public ActuatorTest? ActiveTest { get; private set; }

    public ActuatorStep? ActiveStep { get; private set; }

    public DateTimeOffset? ActiveSince { get; private set; }

    public IReadOnlyList<ActuatorEvent> Events
    {
        get { lock (_events) return [.. _events]; }
    }

    public event Action<ActuatorEvent>? EventRaised;

    private void Log(string test, string step, bool ok, string message)
    {
        var e = new ActuatorEvent(DateTimeOffset.UtcNow, test, step, ok, message);
        lock (_events)
        {
            _events.Add(e);
            if (_events.Count > 500)
                _events.RemoveAt(0);
        }
        EventRaised?.Invoke(e);
    }

    // Lee RPM por el camino normal para verificar "motor apagado" antes de forzar algo.
    private async Task<string?> CheckPreconditionAsync(ActuatorTest test, CancellationToken ct)
    {
        if (test.Precondition != ActuatorPrecondition.EngineOff)
            return null;
        await _uds.ReleaseAsync(ct);
        var rpm = await _backend.ReadPidAsync(0x0C, ct);
        var value = rpm.FirstOrDefault(r => r.Status == ReadStatus.Success)?.Value;
        if (value is null)
            return "No se pudo leer el régimen del motor: no se hace la prueba sin saber si está apagado.";
        if (value > 0)
            return $"El motor está en marcha ({value:0} rpm). Esta prueba se hace con el motor apagado y el contacto dado.";
        return null;
    }

    private async Task<UdsResponse> EnsureSessionAsync(ModuleAddress module, CancellationToken ct)
    {
        if (!await _uds.AddressAsync(module, ct))
            return UdsResponse.Failed(ReadStatus.CommunicationError, "El adaptador no aceptó la cabecera del módulo.", null);
        if (_sessionOpen && _uds.CurrentModule == module)
            return new UdsResponse(true, [], null, null, ReadStatus.Success, null);
        var open = await _uds.RequestAsync(_profile.SessionOpen, ct);
        _sessionOpen = open.Ok;
        if (open.Ok)
            StartKeepAlive();
        return open;
    }

    public async Task<ActuatorEvent> RunStepAsync(ActuatorTest test, ActuatorStep step, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(ct);
        try
        {
            if (!step.IsStop)
            {
                // Cambiar de prueba apaga la anterior primero: nunca dos actuadores forzados a la vez.
                if (ActiveTest is not null && ActiveTest != test)
                    await StopCoreAsync(ct);
                var problem = await CheckPreconditionAsync(test, ct);
                if (problem is not null)
                {
                    Log(test.Name, step.Name, false, problem);
                    return Events[^1];
                }
            }

            var module = _profile.Module(test.ModuleName);
            var session = await EnsureSessionAsync(module, ct);
            if (!session.Ok)
            {
                Log(test.Name, step.Name, false, "No se pudo abrir la sesión de diagnóstico: " + session.Message);
                return Events[^1];
            }

            var response = await _uds.RequestAsync(step.Payload, ct);
            if (response.Ok)
            {
                if (step.IsStop)
                {
                    ActiveTest = null;
                    ActiveStep = null;
                    ActiveSince = null;
                }
                else
                {
                    ActiveTest = test;
                    ActiveStep = step;
                    ActiveSince = DateTimeOffset.UtcNow;
                }
                Log(test.Name, step.Name, true, step.IsStop ? "Control devuelto a la ECU." : $"La ECU aceptó el pedido. Se apaga solo a los {test.MaxDuration.TotalSeconds:0} s.");
            }
            else
            {
                Log(test.Name, step.Name, false, response.Message ?? "Sin respuesta.");
            }
            return Events[^1];
        }
        finally
        {
            _gate.Release();
        }
    }

    // Lectura de apoyo mientras corre la prueba (p. ej. temperatura con el electro).
    public async Task<(double? Value, string Unit)?> ReadFeedbackAsync(ActuatorTest test, CancellationToken ct)
    {
        if (test.FeedbackPid is not byte pid)
            return null;
        await _gate.WaitAsync(ct);
        try
        {
            // Los PID del modo 01 se piden al mismo módulo por cabecera física: no hace falta soltar la sesión.
            var readings = await _backend.ReadPidAsync(pid, ct);
            var r = readings.FirstOrDefault(x => x.Status == ReadStatus.Success);
            return (r?.Value, PidCatalog.Find(pid)?.Unit ?? "");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> ReadStatusAsync(ActuatorTest test, CancellationToken ct)
    {
        if (test.StatusDid is null)
            return null;
        await _gate.WaitAsync(ct);
        try
        {
            var module = _profile.Module(test.ModuleName);
            var session = await EnsureSessionAsync(module, ct);
            if (!session.Ok)
                return null;
            var r = await _uds.RequestAsync("22" + test.StatusDid, ct);
            return r.Ok && r.Data.Length >= 4 ? string.Join(" ", r.Data[3..].Select(b => b.ToString("X2"))) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Borrado de un módulo por UDS (para los que ignoran el modo 04).
    public async Task<ActuatorEvent> ClearModuleAsync(ModuleClear clear, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var module = _profile.Module(clear.ModuleName);
            var session = await EnsureSessionAsync(module, ct);
            if (!session.Ok)
            {
                Log("Borrar fallas", clear.ModuleName, false, "No se pudo abrir la sesión de diagnóstico: " + session.Message);
                return Events[^1];
            }
            var r = await _uds.RequestAsync(clear.Payload, ct);
            Log("Borrar fallas", clear.ModuleName, r.Ok, r.Ok ? $"El módulo {clear.ModuleName} aceptó el borrado." : (r.Message ?? "Sin respuesta."));
            return Events[^1];
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsOverdue => ActiveTest is { } t && ActiveSince is { } s && DateTimeOffset.UtcNow - s > t.MaxDuration;

    public async Task StopAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await StopCoreAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopCoreAsync(CancellationToken ct)
    {
        var test = ActiveTest;
        if (test?.StopStep is not { } stop)
        {
            ActiveTest = null;
            ActiveStep = null;
            ActiveSince = null;
            return;
        }
        var response = await _uds.RequestAsync(stop.Payload, ct);
        Log(test.Name, stop.Name, response.Ok, response.Ok ? "Control devuelto a la ECU." : "No se pudo apagar: " + response.Message);
        ActiveTest = null;
        ActiveStep = null;
        ActiveSince = null;
    }

    private void StartKeepAlive()
    {
        if (_keepAlive is not null)
            return;
        _keepAlive = new CancellationTokenSource();
        var ct = _keepAlive.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    if (IsOverdue)
                    {
                        var name = ActiveTest?.Name ?? "Prueba";
                        await StopAsync(ct);
                        Log(name, "Tiempo", true, "Se apagó por tiempo máximo.");
                        continue;
                    }
                    await _gate.WaitAsync(ct);
                    try
                    {
                        if (_uds.CurrentModule is not null)
                            await _uds.RequestAsync(_profile.TesterPresent, ct, expectNoResponse: true);
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException)
            {
                // Se cortó la conexión: la sesión ya lo informa.
            }
        }, ct);
    }

    // Apaga lo que esté forzado, vuelve al modo OBD genérico y cierra el alcance de escritura.
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _keepAlive?.Cancel();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await StopAsync(cts.Token);
            if (_sessionOpen)
                await _uds.RequestAsync("1001", cts.Token);
            await _uds.ReleaseAsync(cts.Token);
        }
        catch (Exception)
        {
            // Sin conexión no hay nada que devolver; el alcance se cierra igual.
        }
        finally
        {
            _scope.Dispose();
        }
    }
}
