using TallerScan.App.ViewModels;

namespace TallerScan.App.Views;

public partial class HistoryDetailPage : ContentPage, IQueryAttributable
{
    private readonly HistoryDetailViewModel _viewModel;

    public HistoryDetailPage(HistoryDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var value) && Guid.TryParse(value?.ToString(), out var id))
            await _viewModel.LoadAsync(id);
    }
}
