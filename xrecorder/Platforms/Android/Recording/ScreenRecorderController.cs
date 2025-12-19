using Android.Content;
using System;
using System.Collections.Generic;
using System.Text;

namespace xrecorder.Platforms.Android.Recording;

public static class ScreenRecorderController
{
    public static void StartCaptureRequest()
    {
        System.Diagnostics.Debug.WriteLine("StartCaptureRequest called");

        var activity = Platform.CurrentActivity;
        if (activity != null)
        {
            System.Diagnostics.Debug.WriteLine("Launching ScreenCaptureActivity via CurrentActivity");
            var i = new Intent(activity, typeof(ScreenCaptureActivity));
            activity.StartActivity(i);
            return;
        }

        // Fallback (should be rare)
        System.Diagnostics.Debug.WriteLine("Platform.CurrentActivity is NULL - fallback to AppContext");
        var ctx = Platform.AppContext;
        var intent = new Intent(ctx, typeof(ScreenCaptureActivity));
        intent.AddFlags(ActivityFlags.NewTask);
        ctx.StartActivity(intent);
    }

    public static void Stop()
    {
        var ctx = Platform.AppContext;
        var intent = new Intent(ctx, typeof(ScreenRecordService));
        intent.SetAction(ScreenRecordService.ActionStop);
        ctx.StartService(intent);
    }
}