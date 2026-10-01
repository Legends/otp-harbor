using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Activity;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using Java.Util.Concurrent;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using AndroidResult = Android.App.Result;

namespace TOTP.Avalonia.Android;

[Activity(
    Label = "OTP Harbor",
    Theme = "@style/OtpHarborTheme",
    Exported = false,
    ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class LiveQrScannerActivity : ComponentActivity
{
    internal const string PayloadExtra = "otp-harbor.qr.payload";
    internal const string StatusExtra = "otp-harbor.qr.status";
    internal const string InstructionExtra = "otp-harbor.qr.instruction";
    internal const string CancelExtra = "otp-harbor.qr.cancel";
    internal const string UnavailableStatus = "unavailable";
    internal const string FailedStatus = "failed";

    private const int CameraPermissionRequestCode = 0x4351;
    private PreviewView? _preview;
    private ProcessCameraProvider? _cameraProvider;
    private IExecutorService? _analysisExecutor;
    private QrAnalyzer? _analyzer;
    private int _completed;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.Secure);
        BuildContent();

        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera) == Permission.Granted)
            StartCamera();
        else
            RequestPermissions([Manifest.Permission.Camera], CameraPermissionRequestCode);
    }

    public override void OnRequestPermissionsResult(
        int requestCode,
        string[] permissions,
        Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != CameraPermissionRequestCode) return;

        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            StartCamera();
        else
            Complete(null, UnavailableStatus);
    }

    public override void OnBackPressed() => Complete(null, null);

    protected override void OnDestroy()
    {
        StopCamera();
        base.OnDestroy();
    }

    private void BuildContent()
    {
        var root = new FrameLayout(this)
        {
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };

        _preview = new PreviewView(this)
        {
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        _preview.SetImplementationMode(PreviewView.ImplementationMode.Performance);
        _preview.SetScaleType(PreviewView.ScaleType.FillCenter);
        root.AddView(_preview);
        root.AddView(new ScannerOverlayView(this));

        var instruction = new TextView(this)
        {
            Text = Intent?.GetStringExtra(InstructionExtra) ?? string.Empty,
            TextSize = 17,
            Gravity = GravityFlags.Center
        };
        instruction.SetTextColor(Color.White);
        instruction.SetPadding(Dp(20), Dp(12), Dp(20), Dp(12));
        instruction.Background = CreateRoundedBackground(Color.Argb(190, 7, 24, 49), 16);
        var instructionLayout = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            LeftMargin = Dp(24),
            RightMargin = Dp(24),
            TopMargin = Dp(40)
        };
        root.AddView(instruction, instructionLayout);

        var close = new TextView(this)
        {
            Text = "×",
            TextSize = 34,
            Gravity = GravityFlags.Center,
            ContentDescription = Intent?.GetStringExtra(CancelExtra) ?? string.Empty
        };
        close.SetTextColor(Color.White);
        close.Background = CreateRoundedBackground(Color.Argb(190, 7, 24, 49), 28);
        close.Click += (_, _) => Complete(null, null);
        var closeLayout = new FrameLayout.LayoutParams(Dp(56), Dp(56), GravityFlags.Bottom | GravityFlags.CenterHorizontal)
        {
            BottomMargin = Dp(36)
        };
        root.AddView(close, closeLayout);

        SetContentView(root);
    }

    private void StartCamera()
    {
        if (_preview is null || IsFinishing) return;

        var providerFuture = ProcessCameraProvider.GetInstance(this);
        providerFuture.AddListener(
            new ActionRunnable(() =>
            {
                try
                {
                    _cameraProvider = (ProcessCameraProvider?)providerFuture.Get();
                    BindCamera();
                }
                catch
                {
                    Complete(null, FailedStatus);
                }
            }),
            ContextCompat.GetMainExecutor(this));
    }

    private void BindCamera()
    {
        if (_cameraProvider is null || _preview is null || IsFinishing) return;

        var previewUseCase = new Preview.Builder().Build();
        previewUseCase!.SetSurfaceProvider(
            ContextCompat.GetMainExecutor(this),
            _preview.SurfaceProvider);

        var analysisBuilder = new ImageAnalysis.Builder();
        analysisBuilder.SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest);
        var analysis = analysisBuilder.Build()!;
        _analysisExecutor = Executors.NewSingleThreadExecutor();
        _analyzer = new QrAnalyzer(payload => RunOnUiThread(() => Complete(payload, null)));
        analysis!.SetAnalyzer(_analysisExecutor, _analyzer);

        _cameraProvider.UnbindAll();
        _cameraProvider.BindToLifecycle(
            this,
            CameraSelector.DefaultBackCamera!,
            previewUseCase,
            analysis);
    }

    private void Complete(string? payload, string? status)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;

        StopCamera();
        using var result = new Intent();
        if (!string.IsNullOrWhiteSpace(payload))
        {
            result.PutExtra(PayloadExtra, payload);
            SetResult(AndroidResult.Ok, result);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(status)) result.PutExtra(StatusExtra, status);
            SetResult(AndroidResult.Canceled, result);
        }

        Finish();
    }

    private void StopCamera()
    {
        _analyzer?.Stop();
        _cameraProvider?.UnbindAll();
        _analysisExecutor?.ShutdownNow();
        _analyzer?.Dispose();
        _analyzer = null;
        _analysisExecutor?.Dispose();
        _analysisExecutor = null;
        _cameraProvider?.Dispose();
        _cameraProvider = null;
    }

    private GradientDrawable CreateRoundedBackground(Color color, int radiusDp)
    {
        var background = new GradientDrawable();
        background.SetColor(color);
        background.SetCornerRadius(Dp(radiusDp));
        return background;
    }

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

    private sealed class ActionRunnable(Action action) : Java.Lang.Object, Java.Lang.IRunnable
    {
        public void Run() => action();
    }

    private sealed class QrAnalyzer(Action<string> decoded) : Java.Lang.Object, ImageAnalysis.IAnalyzer
    {
        private static readonly IDictionary<DecodeHintType, object> Hints =
            new Dictionary<DecodeHintType, object>
            {
                [DecodeHintType.POSSIBLE_FORMATS] = new[] { BarcodeFormat.QR_CODE },
                [DecodeHintType.CHARACTER_SET] = "UTF-8"
            };

        private int _stopped;
        private long _nextAnalysisAt;
        private byte[] _raw = [];
        private byte[] _luminance = [];

        public global::Android.Util.Size? DefaultTargetResolution => null;
        public int TargetCoordinateSystem => 0;

        public void Analyze(IImageProxy? image)
        {
            if (image is null) return;
            try
            {
                if (Volatile.Read(ref _stopped) != 0) return;
                var now = System.Environment.TickCount64;
                if (now < _nextAnalysisAt) return;
                _nextAnalysisAt = now + 100;

                var text = Decode(image);
                if (!string.IsNullOrWhiteSpace(text) &&
                    Interlocked.Exchange(ref _stopped, 1) == 0)
                    decoded(text);
            }
            catch
            {
                // A malformed or transient camera frame must not stop live scanning.
            }
            finally
            {
                image.Close();
            }
        }

        public void UpdateTransform(Matrix? matrix)
        {
        }

        public void Stop() => Interlocked.Exchange(ref _stopped, 1);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Array.Clear(_raw);
                Array.Clear(_luminance);
            }

            base.Dispose(disposing);
        }

        private string? Decode(IImageProxy image)
        {
            var planes = image.GetPlanes();
            if (planes is null || planes.Length == 0) return null;

            var width = image.Width;
            var height = image.Height;
            if (width <= 0 || height <= 0) return null;

            var plane = planes[0];
            var buffer = plane.Buffer;
            if (buffer is null) return null;
            buffer.Rewind();
            var rawLength = buffer.Remaining();
            if (_raw.Length < rawLength) _raw = new byte[rawLength];
            buffer.Get(_raw, 0, rawLength);

            var luminanceLength = checked(width * height);
            if (_luminance.Length < luminanceLength)
                _luminance = new byte[luminanceLength];
            CompactLuminance(
                _raw,
                _luminance,
                width,
                height,
                plane.RowStride,
                plane.PixelStride);
            var source = new PlanarYUVLuminanceSource(
                _luminance,
                width,
                height,
                0,
                0,
                width,
                height,
                false);
            return TryDecode(source) ?? TryDecode(source.invert());
        }

        private static void CompactLuminance(
            byte[] source,
            byte[] destination,
            int width,
            int height,
            int rowStride,
            int pixelStride)
        {
            if (rowStride == width && pixelStride == 1)
            {
                System.Buffer.BlockCopy(source, 0, destination, 0, checked(width * height));
                return;
            }

            for (var row = 0; row < height; row++)
            {
                var rowStart = row * rowStride;
                var targetStart = row * width;
                for (var column = 0; column < width; column++)
                    destination[targetStart + column] = source[rowStart + (column * pixelStride)];
            }
        }

        private static string? TryDecode(LuminanceSource source)
        {
            var reader = new QRCodeReader();
            try
            {
                return reader.decode(
                    new BinaryBitmap(new HybridBinarizer(source)),
                    Hints)?.Text;
            }
            catch (ReaderException)
            {
                return null;
            }
            finally
            {
                reader.reset();
            }
        }
    }

    private sealed class ScannerOverlayView : View
    {
        private readonly Paint _shade = new() { Color = Color.Argb(105, 0, 0, 0) };
        private readonly Paint _frame = new()
        {
            Color = Color.Rgb(126, 117, 240),
            StrokeWidth = 7,
            StrokeCap = Paint.Cap.Round,
            AntiAlias = true
        };

        public ScannerOverlayView(Context context) : base(context)
        {
            SetLayerType(LayerType.Software, null);
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            var size = Math.Min(Width, Height) * 0.7f;
            var left = (Width - size) / 2f;
            var top = (Height - size) / 2f;
            var right = left + size;
            var bottom = top + size;

            canvas.DrawRect(0, 0, Width, top, _shade);
            canvas.DrawRect(0, bottom, Width, Height, _shade);
            canvas.DrawRect(0, top, left, bottom, _shade);
            canvas.DrawRect(right, top, Width, bottom, _shade);

            var corner = size * 0.16f;
            canvas.DrawLine(left, top, left + corner, top, _frame);
            canvas.DrawLine(left, top, left, top + corner, _frame);
            canvas.DrawLine(right, top, right - corner, top, _frame);
            canvas.DrawLine(right, top, right, top + corner, _frame);
            canvas.DrawLine(left, bottom, left + corner, bottom, _frame);
            canvas.DrawLine(left, bottom, left, bottom - corner, _frame);
            canvas.DrawLine(right, bottom, right - corner, bottom, _frame);
            canvas.DrawLine(right, bottom, right, bottom - corner, _frame);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _shade.Dispose();
                _frame.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
