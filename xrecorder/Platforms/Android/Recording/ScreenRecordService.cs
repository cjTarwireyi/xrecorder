using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Android.Provider;
using Android.Util;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace xrecorder.Platforms.Android.Recording;

[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeMediaProjection
)]
internal class ScreenRecordService : Service
{
    public const string ActionStart = "ACTION_START_RECORD";
    public const string ActionStop = "ACTION_STOP_RECORD";

    const int NotificationId = 2001;
    const string ChannelId = "screen_record_channel";

    MediaProjection? _projection;
    global::Android.Hardware.Display.VirtualDisplay? _virtualDisplay;
    MediaRecorder? _recorder;

    // IMPORTANT: cache the recorder surface so we never call _recorder.Surface during teardown
    // (accessing _recorder.Surface after Stop/Reset/Release can trigger JNI getSurface() and the warnings you saw)
    global::Android.Views.Surface? _recorderSurface;

    global::Android.Net.Uri? _mediaStoreUri;
    ParcelFileDescriptor? _pfd;


    // Android 14+ requires registering a callback before starting capture
    MediaProjection.Callback? _mpCallback;
    Handler? _mpHandler;

    int _width, _height, _dpi;
    string? _outputPath;

    bool _isRecording;

    public static void Start(Context context, int resultCode, Intent data)
    {
        var intent = new Intent(context, typeof(ScreenRecordService));
        intent.SetAction(ActionStart);
        intent.PutExtra("resultCode", resultCode);
        intent.PutExtra("data", data);

        // Safer on Android 8+ (foreground service start path)
        ContextCompat.StartForegroundService(context, intent);
    }
    (global::Android.Net.Uri uri, ParcelFileDescriptor pfd) CreateGalleryOutput()
    {
        var resolver = ContentResolver;

        var fileName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "video/mp4");

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            // Shows under Movies/XRecorder
            values.Put(MediaStore.IMediaColumns.RelativePath, "Movies/XRecorder");
            // Mark pending while we write
            values.Put(MediaStore.IMediaColumns.IsPending, 1);
        }

        var uri = resolver.Insert(MediaStore.Video.Media.ExternalContentUri, values)
                  ?? throw new Exception("Failed to create MediaStore video row.");

        var pfd = resolver.OpenFileDescriptor(uri, "w")
                  ?? throw new Exception("Failed to open MediaStore file descriptor.");

        return (uri, pfd);
    }


    public override void OnDestroy()
    {
        StopRecording();
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            StopSelf(); // cleanup happens in OnDestroy
            return StartCommandResult.NotSticky;
        }

        if (intent?.Action == ActionStart)
        {
            CreateNotificationChannel();
            var notification = BuildNotification("Recording screen…");
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Q) // API 29+
            {
                global::AndroidX.Core.App.ServiceCompat.StartForeground(
                    this,
                    NotificationId,
                    notification,
                    (int)global::Android.Content.PM.ForegroundService.TypeMediaProjection
                );
            }
            else
            {
                StartForeground(NotificationId, notification);
            }

            var resultCode = intent.GetIntExtra("resultCode", (int)Result.Canceled);
            var data = (Intent?)intent.GetParcelableExtra("data");

            if (data != null && resultCode == (int)Result.Ok)
                StartRecording(resultCode, data);
            else
                StopSelf();
        }

        return StartCommandResult.Sticky;
    }

    void StartRecording(int resultCode, Intent data)
    {
        // If we somehow get called twice, stop the previous session first
        if (_isRecording) StopRecording();

        var metrics = Resources?.DisplayMetrics!;
        _dpi = (int)metrics.DensityDpi;

        // Prefer real screen size
        var wm = GetSystemService(global::Android.Content.Context.WindowService) as global::Android.Views.IWindowManager;
        var display = wm?.DefaultDisplay;

        var size = new global::Android.Graphics.Point();
        display?.GetRealSize(size);

        _width = size.X;
        _height = size.Y;

        // Fallback if GetRealSize returns zeros on some devices/API levels
        if (_width <= 0 || _height <= 0)
        {
            _width = metrics.WidthPixels;
            _height = metrics.HeightPixels;
        }

        _outputPath = System.IO.Path.Combine(
            FileSystem.AppDataDirectory,
            $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
        );

        // MediaProjection
        var mgr = (MediaProjectionManager)GetSystemService(global::Android.Content.Context.MediaProjectionService)!;
        _projection = mgr.GetMediaProjection(resultCode, data);

        // Android 14+ requirement: register callback BEFORE starting capture
        _mpHandler = new Handler(Looper.MainLooper);
        _mpCallback = new ProjectionCallback(this);
        _projection.RegisterCallback(_mpCallback, _mpHandler);

        // MediaRecorder
        _recorder = new MediaRecorder();
        _recorder.SetAudioSource(AudioSource.Mic);
        _recorder.SetVideoSource(VideoSource.Surface);
        _recorder.SetOutputFormat(OutputFormat.Mpeg4);
        //_recorder.SetOutputFile(_outputPath);
        (_mediaStoreUri, _pfd) = CreateGalleryOutput();
        _recorder.SetOutputFile(_pfd.FileDescriptor);

        _recorder.SetVideoEncoder(VideoEncoder.H264);
        _recorder.SetAudioEncoder(AudioEncoder.Aac);

        _recorder.SetVideoSize(_width, _height);
        _recorder.SetVideoFrameRate(30);
        _recorder.SetVideoEncodingBitRate(6_000_000);

        _recorder.SetAudioEncodingBitRate(128_000);
        _recorder.SetAudioSamplingRate(44100);

        _recorder.Prepare();

        // Cache the surface ONCE right after Prepare()
        _recorderSurface = _recorder.Surface;

        Log.Debug("REC", "MediaProjection obtained");

        // VirtualDisplay feeding the recorder surface (use cached surface)
        _virtualDisplay = _projection.CreateVirtualDisplay(
            "ScreenRecorder",
            _width, _height, _dpi,
            global::Android.Views.DisplayFlags.None,
            _recorderSurface,
            null, null
        );
        Log.Debug("REC", _virtualDisplay != null ? "VirtualDisplay created" : "VirtualDisplay NULL");

        _recorder.Start();
        Log.Debug("REC", $"Recorder started -> {_outputPath}");

        _isRecording = true;
    }
    void StopRecording()
    {
        // quick guard to prevent double execution
        if (!_isRecording && _projection == null && _recorder == null && _virtualDisplay == null)
            return;

        Log.Debug("REC", "StopRecording called");

        _isRecording = false;

        // run the expensive teardown off the UI thread
        System.Threading.Tasks.Task.Run(() =>
        {
            // IMPORTANT: Stop recorder FIRST so the file is finalized.
            try { _recorder?.Stop(); } catch { /* stop can throw */ }

            // Recorder teardown
            try { _recorder?.Reset(); } catch { }
            try { _recorder?.Release(); } catch { }
            _recorder = null;

            try { _recorderSurface?.Release(); } catch { }
            _recorderSurface = null;

            // Release display
            try { _virtualDisplay?.Release(); } catch { }
            _virtualDisplay = null;

            // Unregister callback + stop projection
            try
            {
                if (_projection != null && _mpCallback != null)
                    _projection.UnregisterCallback(_mpCallback);
            }
            catch { }

            _mpCallback = null;
            _mpHandler = null;

            try { _projection?.Stop(); } catch { }
            _projection = null;

            // ============================
            // FINALIZE THE MEDIASTORE ITEM
            // ============================

            // Close the file descriptor so MediaStore can see a completed file
            try { _pfd?.Close(); } catch { }
            _pfd = null;

            // Mark the item as not pending (Android 10+)
            if (_mediaStoreUri != null && Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                try
                {
                    var values = new ContentValues();
                    values.Put(MediaStore.IMediaColumns.IsPending, 0);
                    ContentResolver.Update(_mediaStoreUri, values, null, null);
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Warn("REC", $"Failed to finalize MediaStore item: {ex}");
                }
            }

            _mediaStoreUri = null;

            // Foreground stop must be called on main thread
            new Handler(Looper.MainLooper).Post(() =>
            {
                try { StopForeground(true); } catch { }
            });
        });
    }

    void PublishToGallery(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            return;

        var fileName = System.IO.Path.GetFileName(filePath);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
            values.Put(MediaStore.IMediaColumns.MimeType, "video/mp4");
            values.Put(MediaStore.IMediaColumns.RelativePath, "Movies/XRecorder");
            values.Put(MediaStore.IMediaColumns.IsPending, 1);

            var collection = MediaStore.Video.Media.GetContentUri(MediaStore.VolumeExternalPrimary);
            var uri = ContentResolver?.Insert(collection, values);
            if (uri == null) return;

            using (var outStream = ContentResolver!.OpenOutputStream(uri)!)
            using (var inStream = System.IO.File.OpenRead(filePath))
            {
                inStream.CopyTo(outStream);
            }

            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending, 0);
            ContentResolver.Update(uri, values, null, null);
        }
        else
        {
            // On Android 9 and below, MediaStore insert is different; this at least triggers a scan
            global::Android.Media.MediaScannerConnection.ScanFile(
                this,
                new[] { filePath },
                new[] { "video/mp4" },
                null
            );
        }
    }




    class ProjectionCallback : MediaProjection.Callback
    {
        readonly ScreenRecordService _service;
        public ProjectionCallback(ScreenRecordService service) => _service = service;

        public override void OnStop()
        {
            // System stopped projection (user revoked, etc.)
            // Avoid re-entrancy: just stop the service; OnDestroy will cleanup.
            _service.StopSelf();
        }
    }

    Notification BuildNotification(string text)
    {
        var stopIntent = new Intent(this, typeof(ScreenRecordService));
        stopIntent.SetAction(ActionStop);

        var stopPendingIntent = PendingIntent.GetService(
            this, 0, stopIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent
        );

        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Screen Recorder")
            .SetContentText(text)
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuCamera)
            .AddAction(0, "Stop", stopPendingIntent)
            .SetOngoing(true)
            .Build();
    }

    void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channel = new NotificationChannel(ChannelId, "Screen Recording", NotificationImportance.Low);
        var nm = (NotificationManager)GetSystemService(NotificationService)!;
        nm.CreateNotificationChannel(channel);
    }
}
