using Microsoft.Maui.ApplicationModel;

namespace xrecorder
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        async void OnStart(object sender, EventArgs e)
        {
#if ANDROID
            // 1) Request Microphone permission (MAUI built-in)
            var mic = await Permissions.RequestAsync<Permissions.Microphone>();
            if (mic != PermissionStatus.Granted)
            {
                StatusLabel.Text = "Microphone permission denied.";
                return;
            }

            // 2) Android 13+ Notifications permission (POST_NOTIFICATIONS) - Android API, not MAUI Permissions
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
            {
                var activity = Platform.CurrentActivity;
                if (activity != null)
                {
                    var granted = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                        activity,
                        Android.Manifest.Permission.PostNotifications
                    ) == Android.Content.PM.Permission.Granted;

                    if (!granted)
                    {
                        AndroidX.Core.App.ActivityCompat.RequestPermissions(
                            activity,
                            new[] { Android.Manifest.Permission.PostNotifications },
                            5001
                        );
                    }
                }
            }

            // 3) Start the Android screen capture consent flow
            xrecorder.Platforms.Android.Recording.ScreenRecorderController.StartCaptureRequest();

            StatusLabel.Text = "Requesting screen capture permission…";
#endif
        }

        void OnStop(object sender, EventArgs e)
        {
#if ANDROID
            xrecorder.Platforms.Android.Recording.ScreenRecorderController.Stop();
            StatusLabel.Text = "Stopping…";
#endif
        }
    }
}
