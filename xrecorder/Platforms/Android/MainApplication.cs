using Android.App;
using Android.Runtime;

// ✅ Inject permissions into the generated AndroidManifest.xml (no XML editing needed)
[assembly: UsesPermission(Android.Manifest.Permission.RecordAudio)]
[assembly: UsesPermission(Android.Manifest.Permission.ForegroundService)]
// Android 14+ MediaProjection foreground service permission (use string for compatibility)
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION")]

namespace xrecorder
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
