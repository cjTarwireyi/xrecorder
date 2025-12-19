using Android.App;
using Android.Content;
using Android.OS;

namespace xrecorder.Platforms.Android.Recording;

[Activity(NoHistory = true, Exported = false)]
public class ScreenCaptureActivity : Activity
{
    const int RequestCode = 1001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        System.Diagnostics.Debug.WriteLine("ScreenCaptureActivity OnCreate");

        var mgr = (global::Android.Media.Projection.MediaProjectionManager)
            GetSystemService(global::Android.Content.Context.MediaProjectionService)!;

        System.Diagnostics.Debug.WriteLine("Starting MediaProjection permission intent");
        StartActivityForResult(mgr.CreateScreenCaptureIntent(), RequestCode);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        System.Diagnostics.Debug.WriteLine($"OnActivityResult rc={requestCode} result={resultCode} dataNull={data == null}");

        if (requestCode == RequestCode && resultCode == Result.Ok && data != null)
        {
            var intent = new Intent(this, typeof(ScreenRecordService));
            intent.SetAction(ScreenRecordService.ActionStart);
            intent.PutExtra("resultCode", (int)resultCode);
            intent.PutExtra("data", data);

            AndroidX.Core.Content.ContextCompat.StartForegroundService(this, intent);
        }

        Finish();
    }
}
