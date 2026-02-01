using System;
using System.Threading;
using System.Threading.Tasks;
using SignatureMouse.Models;
using SignatureMouse.Processing;

namespace SignatureMouse.Replay;

internal static class ReplayEngine
{
    public static async Task ReplayAsync(SignaturePath path, ReplayOptions options, IReplayBackend backend, CancellationToken token)
    {
        using var timerScope = WindowsTimerResolution.Begin(1);
        if (options.DelaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.DelaySeconds), token);
        }

        var bounds = path.GetBounds();
        float bboxWidth = Math.Max(1, bounds.MaxX - bounds.MinX);
        float bboxHeight = Math.Max(1, bounds.MaxY - bounds.MinY);

        float scale = options.Scale <= 0 ? 1 : options.Scale;
        if (options.TargetWidth.HasValue || options.TargetHeight.HasValue)
        {
            float scaleX = options.TargetWidth.HasValue ? options.TargetWidth.Value / bboxWidth : float.PositiveInfinity;
            float scaleY = options.TargetHeight.HasValue ? options.TargetHeight.Value / bboxHeight : float.PositiveInfinity;
            float fitScale = Math.Min(scaleX, scaleY);
            if (float.IsInfinity(fitScale) || fitScale <= 0) fitScale = 1;
            scale *= fitScale;
        }

        float drawWidth = bboxWidth * scale;
        float drawHeight = bboxHeight * scale;

        // Anchor the first point to the cursor position captured after the delay.
        var cursor = backend.GetCursorPosition();
        var startPoint = GetStartPoint(path);
        float anchorX = cursor.X - startPoint.X * scale;
        float anchorY = cursor.Y - startPoint.Y * scale;

        float offsetX = options.OffsetX ?? anchorX;
        float offsetY = options.OffsetY ?? anchorY;

        float step = options.StepPixels <= 0 ? 2f : options.StepPixels;
        float speed = options.SpeedPixelsPerSecond <= 0 ? 800f : options.SpeedPixelsPerSecond;
        double delayMs = step / speed * 1000.0;
        if (delayMs < 0) delayMs = 0;

        foreach (var stroke in path.Strokes)
        {
            token.ThrowIfCancellationRequested();
            if (stroke.Count == 0) continue;

            var start = Transform(stroke[0], scale, offsetX, offsetY);
            backend.MoveTo((int)MathF.Round(start.X), (int)MathF.Round(start.Y));
            backend.MouseDown();

            for (int i = 1; i < stroke.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var p0 = Transform(stroke[i - 1], scale, offsetX, offsetY);
                var p1 = Transform(stroke[i], scale, offsetX, offsetY);
                float segmentLength = PolylineUtils.Distance(p0, p1);
                int steps = Math.Max(1, (int)MathF.Ceiling(segmentLength / step));

                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    var point = new Models.PointF(
                        p0.X + (p1.X - p0.X) * t,
                        p0.Y + (p1.Y - p0.Y) * t
                    );

                    backend.MoveTo((int)MathF.Round(point.X), (int)MathF.Round(point.Y));
                    if (delayMs > 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), token);
                    }
                }
            }

            backend.MouseUp();
        }
    }

    private static Models.PointF Transform(Models.PointF p, float scale, float offsetX, float offsetY)
    {
        return new Models.PointF(p.X * scale + offsetX, p.Y * scale + offsetY);
    }

    private static Models.PointF GetStartPoint(SignaturePath path)
    {
        foreach (var stroke in path.Strokes)
        {
            if (stroke.Count > 0)
            {
                return stroke[0];
            }
        }
        return new Models.PointF(0, 0);
    }
}
