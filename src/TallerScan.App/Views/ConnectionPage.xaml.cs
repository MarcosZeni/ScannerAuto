using TallerScan.App.ViewModels;

namespace TallerScan.App.Views;

public partial class ConnectionPage : ContentPage
{
    public ConnectionPage(ConnectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
