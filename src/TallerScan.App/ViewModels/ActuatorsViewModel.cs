using System.Collections.ObjectModel;
using System.Globalization;
using TallerScan.App.Mvvm;
using TallerScan.App.Services;
using TallerScan.Core.Actuators;
using TallerScan.Core.Diagnostics;
using TallerScan.Core.Models;
using TallerScan.Infrastructure.Actuators;
using TallerScan.Infrastructure.Sessions;
using TallerScan.Infrastructure.Simulation;

namespace TallerScan.App.ViewModels;

public sealed class StepItem(ActuatorStep step, Func<ActuatorStep, Task> run) : ObservableObject
{
    public string Name => step.Name;
    public bool IsStop => step.IsStop;
    public AsyncCommand Command { get; } = new(() => run(step));
}

public sealed class ActuatorTestItem : ObservableObject
{
    public ActuatorTestItem(ActuatorTest test, Func<ActuatorTest, ActuatorStep, Task> run)
    {
        Test = test;
        Steps = test.Steps.Select(s => new StepItem(s, step => run(test, step))).ToList();
    }

    public ActuatorTest Test { get; }
    public string Name => Test.Name;
    public string Description => Test.Description;
    public string Precondition => Test.Precondition == ActuatorPrecondition.EngineOff ? "Motor apagado, contacto dado" : "Con el motor como esté";
    public IReadOnlyList<StepItem> Steps { get; }

    private string _status = "";
    public string Status { get => _status; set { Set(ref _status, value); Raise(nameof(HasStatus)); } }
    public bool HasStatus => Status.Length > 0;

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

public sealed record ProfileOption(ActuatorProfile? Profile)
{
    public override string ToString() => Profile?.Name ?? "Sin perfil";
}

// Pruebas de actuadores (electro, bomba, inyectores...) según el perfil del auto conectado.
public sealed class ActuatorsViewModel : ObservableObject
{
    private readonly DiagnosticSession _session;
    private IDispatcherTimer? _timer;
    private int _tick;

    public ActuatorsViewModel(DiagnosticSession session)
    {
        _session = session;
        EnterCommand = new AsyncCommand(EnterAsync, () => _session.State == SessionState.Ready && _session.Profile is not null && _session.Actuators is null);
        ExitCommand = new AsyncCommand(ExitAsync, () => _session.Actuators is not null);
        StopAllCommand = new AsyncCommand(StopAllAsync, () => _session.Actuators is not null);
        EngineOffCommand = new AsyncCommand(() => SetEngine(EngineMode.Off));
        EngineIdleCommand = new AsyncCommand(() => SetEngine(EngineMode.Idle));
        ImportProfileCommand = new AsyncCommand(ImportProfileAsync);

        _session.StateChanged += _ => OnUi(RefreshAll);
        _session.ActuatorsChanged += () => OnUi(RefreshAll);
        _session.ProfileChanged += () => OnUi(RefreshAll);
        RefreshAll();
    }

    public ObservableCollection<ActuatorTestItem> Tests { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    public bool IsReady => _session.State == SessionState.Ready;
    public bool HasProfile => IsReady && _session.Profile is not null;
    public bool NoProfile => IsReady && _session.Profile is null;
    public bool IsSimulated => _session.IsSimulated && IsReady;
    public bool IsActive => _session.Actuators is not null;
    public bool CanEnter => HasProfile && !IsActive;
    public string ProfileName => _session.Profile?.Name ?? "";
    public string ProfileSource => _session.Profile?.Source ?? "";
    public bool ProfileIsManual => _session.ProfileIsManual;

    // Perfiles importados que se pueden elegir a mano para un auto real (no en simulación).
    public ObservableCollection<ProfileOption> Profiles { get; } = [];
    public bool CanChooseProfile => IsReady && !_session.IsSimulated && !IsActive;
    public bool HasImportedProfiles => Profiles.Count > 1;

    private ProfileOption? _selectedProfile;
    public ProfileOption? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value) || value is null || !CanChooseProfile)
                return;
            if (!ReferenceEquals(value.Profile, _session.Profile))
            {
                _session.SelectProfile(value.Profile);
                RefreshAll();
            }
        }
    }

    public AsyncCommand ImportProfileCommand { get; }

    private async Task ImportProfileAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Elegí un perfil (.json)" });
        if (file is null)
            return;
        string json;
        await using (var stream = await file.OpenReadAsync())
        using (var reader = new StreamReader(stream))
            json = await reader.ReadToEndAsync();
        ActuatorProfile profile;
        try
        {
            profile = _session.Profiles.Load(json);
        }
        catch (ArgumentException ex)
        {
            await Ui.AlertAsync("No se importó", ex.Message);
            return;
        }
        // Queda guardado para las próximas veces.
        Directory.CreateDirectory(MauiProgram.ProfilesDirectory);
        await File.WriteAllTextAsync(Path.Combine(MauiProgram.ProfilesDirectory, profile.Id + ".json"), json);
        RefreshAll();
        await Ui.AlertAsync("Perfil importado", $"{profile.Name}\n\n{profile.Source}\n\n{profile.Tests.Count} prueba(s), {profile.ModuleClears.Count} borrado(s) por módulo." +
            (profile.VinPrefixes is { Count: > 0 } ? "" : "\n\nNo tiene prefijos de VIN: hay que elegirlo a mano en cada auto."));
    }

    public string EngineText => _session.Simulator?.Engine.Mode switch
    {
        EngineMode.Off => "Motor simulado: apagado (contacto dado)",
        EngineMode.Idle => "Motor simulado: en ralentí",
        EngineMode.Driving => "Motor simulado: andando",
        _ => "",
    };

    private string _activeText = "";
    public string ActiveText { get => _activeText; set { Set(ref _activeText, value); Raise(nameof(HasActive)); } }
    public bool HasActive => ActiveText.Length > 0;

    private string _feedbackText = "";
    public string FeedbackText { get => _feedbackText; set => Set(ref _feedbackText, value); }

    public AsyncCommand EnterCommand { get; }
    public AsyncCommand ExitCommand { get; }
    public AsyncCommand StopAllCommand { get; }
    public AsyncCommand EngineOffCommand { get; }
    public AsyncCommand EngineIdleCommand { get; }

    private ActuatorProfile? _shownProfile;

    private void RefreshAll()
    {
        foreach (var name in new[] { nameof(IsReady), nameof(HasProfile), nameof(NoProfile), nameof(IsSimulated), nameof(IsActive), nameof(CanEnter), nameof(ProfileName), nameof(ProfileSource), nameof(ProfileIsManual), nameof(EngineText), nameof(CanChooseProfile) })
            Raise(name);
        EnterCommand.RaiseCanExecuteChanged();
        ExitCommand.RaiseCanExecuteChanged();
        StopAllCommand.RaiseCanExecuteChanged();

        // Lista para elegir a mano: "ninguno" más los importados.
        var selectable = _session.Profiles.Selectable;
        if (Profiles.Count != selectable.Count + 1 || !selectable.All(p => Profiles.Any(o => ReferenceEquals(o.Profile, p))))
        {
            Profiles.Clear();
            Profiles.Add(new ProfileOption(null));
            foreach (var p in selectable)
                Profiles.Add(new ProfileOption(p));
        }
        var current = Profiles.FirstOrDefault(o => ReferenceEquals(o.Profile, _session.Profile)) ?? Profiles[0];
        if (!ReferenceEquals(_selectedProfile, current))
        {
            _selectedProfile = current;
            Raise(nameof(SelectedProfile));
        }
        Raise(nameof(HasImportedProfiles));

        if (!HasProfile || !ReferenceEquals(_shownProfile, _session.Profile))
        {
            Tests.Clear();
            _shownProfile = null;
        }
        if (HasProfile && Tests.Count == 0)
        {
            _shownProfile = _session.Profile;
            foreach (var t in _session.Profile!.Tests)
                Tests.Add(new ActuatorTestItem(t, RunAsync));
        }
        if (!IsActive)
        {
            StopTimer();
            ActiveText = "";
            FeedbackText = "";
            foreach (var t in Tests)
                t.IsActive = false;
        }
    }

    private Task SetEngine(EngineMode mode)
    {
        if (_session.Simulator is { } sim)
            sim.Engine.Mode = mode;
        Raise(nameof(EngineText));
        return Task.CompletedTask;
    }

    private Task EnterAsync()
    {
        var runner = _session.StartActuators();
        runner.EventRaised += e => OnUi(() =>
        {
            Log.Insert(0, $"{e.AtUtc.ToLocalTime():HH:mm:ss}  {e.Test} · {e.Step}: {e.Message}");
            while (Log.Count > 50)
                Log.RemoveAt(Log.Count - 1);
            var item = Tests.FirstOrDefault(t => t.Name == e.Test);
            if (item is not null)
                item.Status = (e.Ok ? "" : "✖ ") + e.Message;
            UpdateActive(runner);
        });
        StartTimer();
        RefreshAll();
        return Task.CompletedTask;
    }

    private async Task ExitAsync()
    {
        await _session.EndActuatorsAsync();
        RefreshAll();
    }

    private async Task StopAllAsync()
    {
        if (_session.Actuators is { } runner)
            await runner.StopAsync(CancellationToken.None);
        if (_session.Actuators is { } r2)
            UpdateActive(r2);
    }

    private async Task RunAsync(ActuatorTest test, ActuatorStep step)
    {
        var runner = _session.Actuators;
        if (runner is null)
        {
            await Ui.AlertAsync("Pruebas", "Tocá \"Entrar al modo pruebas\" primero.");
            return;
        }
        await runner.RunStepAsync(test, step, CancellationToken.None);
        UpdateActive(runner);
    }

    private void UpdateActive(ActuatorRunner runner)
    {
        foreach (var t in Tests)
            t.IsActive = runner.ActiveTest == t.Test;
        if (runner.ActiveTest is { } active && runner.ActiveSince is { } since)
        {
            var left = Math.Max(0, (active.MaxDuration - (DateTimeOffset.UtcNow - since)).TotalSeconds);
            ActiveText = $"{active.Name} · {runner.ActiveStep?.Name} · se apaga en {left:0} s";
        }
        else
        {
            ActiveText = "";
            FeedbackText = "";
        }
    }

    private void StartTimer()
    {
        if (_timer is not null || Application.Current?.Dispatcher is not { } dispatcher)
            return;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += async (_, _) =>
        {
            if (_session.Actuators is not { } runner)
                return;
            UpdateActive(runner);
            Raise(nameof(EngineText));
            // Lectura de apoyo cada 2 s (p. ej. temperatura mientras gira el electro).
            if (++_tick % 2 == 0 && runner.ActiveTest is { FeedbackPid: not null } test)
            {
                try
                {
                    var fb = await runner.ReadFeedbackAsync(test, CancellationToken.None);
                    if (fb is { Value: double v })
                        FeedbackText = $"{PidCatalog.Find(test.FeedbackPid!.Value)?.Name}: {v.ToString("0.#", CultureInfo.CurrentCulture)} {fb.Value.Unit}";
                }
                catch (InvalidOperationException)
                {
                }
            }
        };
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }
}
