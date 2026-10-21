using TallerScan.App.ViewModels;

namespace TallerScan.App.Views;

public partial class FaultsPage : ContentPage
{
    public FaultsPage(FaultsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
