using TallerScan.App.ViewModels;

namespace TallerScan.App.Views;

public partial class LivePage : ContentPage
{
    public LivePage(LiveViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
