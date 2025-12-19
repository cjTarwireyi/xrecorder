namespace xrecorder.Views;

public partial class LandingPage : ContentPage
{
    public LandingPage()
    {
        InitializeComponent();
    }

    private async void OnStartRecordingClicked(object sender, EventArgs e)
        => await DisplayAlert("Start", "Hook this to your recording start flow.", "OK");

    private async void OnPrivacyTapped(object sender, TappedEventArgs e)
        => await DisplayAlert("Privacy", "Open privacy page.", "OK");

    private async void OnTermsTapped(object sender, TappedEventArgs e)
        => await DisplayAlert("Terms", "Open terms page.", "OK");

    private async void OnHelpTapped(object sender, TappedEventArgs e)
        => await DisplayAlert("Help", "Open help page.", "OK");
}
