using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SignatureMouse.Cli;
using SignatureMouse.Models;
using SignatureMouse.Processing;
using SignatureMouse.Replay;

namespace SignatureMouse;

internal static class Program
{
    private const double DefaultDelaySeconds = 10;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            ShowHelp();
            return 1;
        }

        var mode = args[0].ToLowerInvariant();
        var parser = new ArgParser(args.Skip(1).ToArray());

        if (parser.Has("help", "h", "?"))
        {
            ShowHelp();
            return 0;
        }

        try
        {
            switch (mode)
            {
                case "analyze":
                    Analyze(parser);
                    return 0;
                case "replay":
                    await Replay(parser);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown mode: {mode}");
                    ShowHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void Analyze(ArgParser parser)
    {
        var input = parser.Get("input", "i");
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("--input is required.");
        }

        var output = parser.Get("output", "o");
        if (string.IsNullOrWhiteSpace(output))
        {
            output = input + ".svg";
        }

        var minComponent = parser.GetInt(-1, "min-component");
        int? minComponentSize = minComponent > 0 ? minComponent : null;

        var simplify = parser.GetFloat(1.5f, "simplify");
        var resample = parser.GetFloat(2.0f, "resample");
        var maxSize = parser.GetInt(1200, "max-size");
        var saveCleaned = parser.Get("save-cleaned");
        var despeckle = !parser.Has("no-despeckle");
        var threshold = parser.GetInt(-1, "threshold");
        int? thresholdValue = threshold >= 0 ? threshold : null;
        var invert = parser.Has("invert");
        var rotate = parser.GetFloat(0f, "rotate");
        var thinIterations = parser.GetInt(-1, "thin-iterations");
        var closeRadius = parser.GetInt(0, "close-radius");
        var smoothIterations = parser.GetInt(0, "smooth-iterations");

        var options = new AnalyzeOptions(
            input,
            output,
            minComponentSize,
            simplify,
            resample,
            maxSize,
            saveCleaned,
            despeckle,
            thresholdValue,
            invert,
            rotate,
            thinIterations,
            closeRadius,
            smoothIterations
        );

        Console.WriteLine("Loading and preprocessing image...");
        var binary = ImagePipeline.Process(options.InputPath, options);

        Console.WriteLine("Skeletonizing...");
        var skeleton = Skeletonizer.Thin(binary.Ink, options.ThinIterations);

        if (!string.IsNullOrWhiteSpace(options.SaveCleanedPath))
        {
            ImagePipeline.SaveBinaryPreview(skeleton, binary.Width, binary.Height, options.SaveCleanedPath!);
        }

        Console.WriteLine("Tracing signature path...");
        var strokes = SkeletonTracer.Trace(skeleton);
        var processed = strokes
            .Select(stroke => PolylineUtils.SimplifyRdp(stroke, options.SimplifyEpsilon))
            .Select(stroke => PolylineUtils.Resample(stroke, options.ResampleSpacing))
            .Select(stroke => options.SmoothIterations > 0
                ? PolylineUtils.SmoothChaikin(stroke, options.SmoothIterations, true)
                : stroke)
            .Select(stroke => options.SmoothIterations > 0
                ? PolylineUtils.Resample(stroke, options.ResampleSpacing)
                : stroke)
            .Where(stroke => stroke.Count > 0)
            .ToList();

        var ordered = PolylineUtils.OrderStrokes(processed);
        var signature = new SignaturePath(binary.Width, binary.Height, ordered);

        signature.SaveSvg(options.OutputPath);
        Console.WriteLine($"Saved vector path: {options.OutputPath}");
    }

    private static async Task Replay(ArgParser parser)
    {
        var input = parser.Get("input", "i");
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("--input is required.");
        }

        var drawRect = parser.Has("draw-rect");
        var plop = parser.Has("plop");
        var padding = parser.GetFloat(0.10f, "padding");
        bool delaySpecified = parser.Has("delay", "d");
        var delay = delaySpecified
            ? parser.GetDouble(DefaultDelaySeconds, "delay", "d")
            : (drawRect || plop ? 0 : DefaultDelaySeconds);
        var speed = parser.GetFloat(3000f, "speed");
        var step = parser.GetFloat(2f, "step");
        var scale = parser.GetFloat(1f, "scale");
        var width = parser.GetFloat(-1f, "width");
        var height = parser.GetFloat(-1f, "height");
        var offsetX = parser.GetFloat(float.NaN, "offset-x");
        var offsetY = parser.GetFloat(float.NaN, "offset-y");
        var backendName = parser.Get("backend") ?? "windows";

        if (drawRect && plop)
        {
            throw new ArgumentException("--draw-rect and --plop cannot be used together.");
        }

        if ((drawRect || plop) && (parser.Has("scale") || parser.Has("width") || parser.Has("height") || parser.Has("offset-x") || parser.Has("offset-y")))
        {
            throw new ArgumentException("--draw-rect and --plop cannot be used with --scale, --width, --height, --offset-x, or --offset-y.");
        }

        var options = new ReplayOptions(
            input,
            delay,
            speed,
            step,
            scale,
            width > 0 ? width : null,
            height > 0 ? height : null,
            float.IsNaN(offsetX) ? null : offsetX,
            float.IsNaN(offsetY) ? null : offsetY,
            backendName,
            drawRect,
            plop,
            padding
        );

        var backend = CreateBackend(options.Backend);
        Console.WriteLine($"Using backend: {backend.Name}");

        var signature = SignaturePath.LoadSvg(options.InputPath);
        if (options.DrawRect)
        {
            var selector = CreateDrawRectSelector(options.Backend);
            var rect = selector.SelectRectangle();
            if (rect == null)
            {
                Console.WriteLine("Selection cancelled.");
                return;
            }

            if (options.Padding < 0 || options.Padding >= 0.45f)
            {
                throw new ArgumentException("--padding must be between 0.0 and 0.45.");
            }

            var (scaleRect, offsetRectX, offsetRectY) = ComputePlacement(signature, rect.Value, options.Padding);
            options = options with
            {
                Scale = scaleRect,
                TargetWidth = null,
                TargetHeight = null,
                OffsetX = offsetRectX,
                OffsetY = offsetRectY
            };
        }
        else if (options.Plop)
        {
            var selector = CreatePlopSelector(options.Backend);
            var result = selector.SelectPlacement(signature);
            if (result == null)
            {
                Console.WriteLine("Placement cancelled.");
                return;
            }

            options = options with
            {
                Scale = result.Value.Scale,
                TargetWidth = null,
                TargetHeight = null,
                OffsetX = result.Value.OffsetX,
                OffsetY = result.Value.OffsetY
            };
        }
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine($"Replaying in {options.DelaySeconds.ToString(CultureInfo.InvariantCulture)}s. Press Ctrl+C to cancel.");
        try
        {
            await ReplayEngine.ReplayAsync(signature, options, backend, cts.Token);
        }
        finally
        {
            backend.MouseUp();
        }
    }

    private static IReplayBackend CreateBackend(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "windows" => OperatingSystem.IsWindows()
                ? new WindowsReplayBackend()
                : throw new PlatformNotSupportedException("Windows backend selected but current OS is not Windows."),
            _ => throw new ArgumentException($"Unknown backend: {name}")
        };
    }

    private static Replay.IDrawRectSelector CreateDrawRectSelector(string backendName)
    {
        return backendName.ToLowerInvariant() switch
        {
            "windows" => OperatingSystem.IsWindows()
                ? new Replay.WindowsDrawRectSelector()
                : throw new PlatformNotSupportedException("Draw-rect is only supported on Windows."),
            _ => throw new ArgumentException($"Draw-rect is not supported for backend: {backendName}")
        };
    }

    private static Replay.IPlopSelector CreatePlopSelector(string backendName)
    {
        return backendName.ToLowerInvariant() switch
        {
            "windows" => OperatingSystem.IsWindows()
                ? new Replay.WindowsPlopSelector()
                : throw new PlatformNotSupportedException("Plop is only supported on Windows."),
            _ => throw new ArgumentException($"Plop is not supported for backend: {backendName}")
        };
    }

    private static (float Scale, float OffsetX, float OffsetY) ComputePlacement(SignaturePath signature, Replay.ScreenRect rect, float padding)
    {
        var bounds = signature.GetBounds();
        float bboxWidth = Math.Max(1, bounds.MaxX - bounds.MinX);
        float bboxHeight = Math.Max(1, bounds.MaxY - bounds.MinY);

        float pad = Math.Clamp(padding, 0f, 0.45f);
        float availableWidth = rect.Width * (1f - pad * 2f);
        float availableHeight = rect.Height * (1f - pad * 2f);
        if (availableWidth <= 0 || availableHeight <= 0)
        {
            throw new ArgumentException("Padding leaves no drawable area inside the selection.");
        }

        float scale = Math.Min(availableWidth / bboxWidth, availableHeight / bboxHeight);
        float drawWidth = bboxWidth * scale;
        float drawHeight = bboxHeight * scale;

        float offsetX = rect.Left + (rect.Width - drawWidth) / 2f - bounds.MinX * scale;
        float offsetY = rect.Top + (rect.Height - drawHeight) / 2f - bounds.MinY * scale;

        return (scale, offsetX, offsetY);
    }

    private static bool IsHelp(string arg)
    {
        return arg == "-h" || arg == "--help" || arg == "/?" || arg == "help";
    }

    private static void ShowHelp()
    {
        Console.WriteLine(@"SignatureMouse (NET 8)

Usage:
  SignatureMouse analyze --input <image> [options]
  SignatureMouse replay  --input <path.svg> [options]

Analyze options:
  -i, --input <file>          Input image (png/jpg/etc.)
  -o, --output <file>         Output SVG path file (default: <input>.svg)
  --min-component <pixels>    Remove connected components smaller than this
  --simplify <epsilon>        RDP simplify epsilon in pixels (default: 1.5)
  --resample <spacing>        Resample spacing in pixels (default: 2.0)
  --max-size <pixels>         Resize largest dimension to this (default: 1200)
  --save-cleaned <file>       Save thinned skeleton image for debugging
  --no-despeckle              Disable despeckle filtering
  --threshold <0-255>         Force a threshold (skips auto thresholding)
  --invert                    Treat lighter pixels as ink (force inversion)
  --rotate <degrees>          Rotate input counter-clockwise before processing (-90, 90, 180)
  --thin-iterations <count>   Limit thinning iterations (default: full thinning)
  --close-radius <pixels>     Morphological closing radius before thinning
  --smooth-iterations <count> Apply Chaikin smoothing after vectorization

Replay options:
  -i, --input <file>          Input SVG path file
  -d, --delay <seconds>       Delay before replay (default: 10)
  --speed <px/sec>            Pen speed in pixels per second (default: 3000)
  --step <pixels>             Movement step size in pixels (default: 2)
  --scale <factor>            Overall scale multiplier (default: 1)
  --width <pixels>            Fit signature to width
  --height <pixels>           Fit signature to height
  --offset-x <pixels>         Top-left X offset on screen
  --offset-y <pixels>         Top-left Y offset on screen
  --draw-rect                 Select target rectangle on screen (mutually exclusive with scale/width/height/offset)
  --padding <0-0.45>           Padding ratio when using --draw-rect (default: 0.10)
  --plop                      Place signature overlay interactively (mutually exclusive with scale/width/height/offset)
  --backend <name>            Replay backend (default: windows)

By default, replay anchors the signature start point to the current mouse cursor
position after the delay. Use --offset-x/--offset-y to override absolute placement.
When using --draw-rect or --plop and no delay is specified, delay defaults to 0.
");
    }
}
