using TallerScan.App.ViewModels;

namespace TallerScan.App.Views;

public partial class TracePage : ContentPage
{
    private readonly TraceViewModel _viewModel;

    public TracePage(TraceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // Solo se actualiza mientras está a la vista.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Attach();
    }

    protected override void OnDisappearing()
    {
        _viewModel.Detach();
        base.OnDisappearing();
    }
}
