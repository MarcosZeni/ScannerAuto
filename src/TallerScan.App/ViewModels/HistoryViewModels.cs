using System.Collections.ObjectModel;
using System.Globalization;
using TallerScan.App.Mvvm;
using TallerScan.App.Services;
using TallerScan.Infrastructure.History;
using TallerScan.Infrastructure.Reporting;

namespace TallerScan.App.ViewModels;

public sealed record HistoryItem(Guid Id, string Title, string Subtitle, string Badge, Color BadgeColor, bool Simulated);

public sealed class HistoryViewModel : ObservableObject
{
    private readonly JsonHistoryStore _store;

    public HistoryViewModel(JsonHistoryStore store)
    {
        _store = store;
        RefreshCommand = new AsyncCommand(RefreshAsync);
        ImportCommand = new AsyncCommand(ImportAsync);
        OpenCommand = new AsyncCommand<HistoryItem>(item =>
            Shell.Current.GoToAsync($"detalle?id={item.Id:N}"));
    }

    public ObservableCollection<HistoryItem> Items { get; } = [];

    private bool _isRefreshing;
    public bool IsRefreshing { get => _isRefreshing; set => Set(ref _isRefreshing, value); }

    private string _empty = "Todavía no hay diagnósticos guardados.";
    public string EmptyText { get => _empty; set => Set(ref _empty, value); }

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand<HistoryItem> OpenCommand { get; }

    public async Task RefreshAsync()
    {
        try
        {
            var list = await _store.ListAsync();
            Items.Clear();
            foreach (var s in list)
            {
                var title = string.IsNullOrWhiteSpace(s.Plate) ? "Sin patente" : s.Plate;
                var subtitle = $"{s.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture)}{(string.IsNullOrWhiteSpace(s.Vehicle) ? "" : " · " + s.Vehicle)}";
                var (badge, color) = s.DtcCount == 0
                    ? ("Sin códigos", Color.FromArgb("#1E7A46"))
                    : ($"{s.DtcCount} código(s)", Color.FromArgb("#B42318"));
                Items.Add(new HistoryItem(s.Id, title + (s.Simulated ? "  · SIMULADO" : ""), subtitle, badge, color, s.Simulated));
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task ImportAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Elegí un diagnóstico exportado (.json)" });
        if (file is null)
            return;
        await using var stream = await file.OpenReadAsync();
        var result = await _store.ImportAsync(stream);
        await Ui.AlertAsync(result.Ok ? "Importado" : "No se importó", result.Message);
        if (result.Ok)
            await RefreshAsync();
    }
}

public sealed class HistoryDetailViewModel : ObservableObject
{
    private readonly JsonHistoryStore _store;
    private SessionRecord? _record;

    public HistoryDetailViewModel(JsonHistoryStore store)
    {
        _store = store;
        ShareReportCommand = new AsyncCommand(ShareReportAsync, () => _record is not null);
        ExportCommand = new AsyncCommand(ExportAsync, () => _record is not null);
        DeleteCommand = new AsyncCommand(DeleteAsync, () => _record is not null);
    }

    private HtmlWebViewSource? _html;
    public HtmlWebViewSource? Html { get => _html; set => Set(ref _html, value); }

    private string _title = "Diagnóstico";
    public string Title { get => _title; set => Set(ref _title, value); }

    public AsyncCommand ShareReportCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public AsyncCommand DeleteCommand { get; }

    public async Task LoadAsync(Guid id)
    {
        _record = await _store.LoadAsync(id);
        if (_record is null)
        {
            Html = new HtmlWebViewSource { Html = "<p style='font-family:sans-serif'>No se pudo leer el diagnóstico.</p>" };
            return;
        }
        Title = string.IsNullOrWhiteSpace(_record.Plate) ? "Diagnóstico" : _record.Plate;
        Html = new HtmlWebViewSource { Html = HtmlReport.Build(_record) };
        ShareReportCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }

    private string BaseName => $"{Ui.SafeFileName(_record!.Plate)}-{_record.CreatedUtc.ToLocalTime():yyyyMMdd-HHmm}";

    private Task ShareReportAsync() =>
        Ui.ShareTextFileAsync($"diagnostico-{BaseName}.html", HtmlReport.Build(_record!), "Informe de diagnóstico");

    // El .json se importa en otro equipo (del celular a la PC o al revés).
    private async Task ExportAsync()
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"tallerscan-{BaseName}.json");
        File.Copy(_store.FileOf(_record!.Id), path, overwrite: true);
        await Ui.ShareFileAsync(path, "Exportar diagnóstico");
    }

    private async Task DeleteAsync()
    {
        if (!await Ui.ConfirmAsync("Eliminar", "¿Eliminar este diagnóstico del historial de este equipo?", "Eliminar"))
            return;
        _store.Delete(_record!.Id);
        await Shell.Current.GoToAsync("..");
    }
}
