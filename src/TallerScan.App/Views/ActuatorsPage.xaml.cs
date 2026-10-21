using TallerScan.App.ViewModels;

namespace TallerScan.App.Views;

public partial class ActuatorsPage : ContentPage
{
    public ActuatorsPage(ActuatorsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
