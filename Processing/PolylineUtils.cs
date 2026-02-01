using System;
using System.Collections.Generic;
using SignatureMouse.Models;

namespace SignatureMouse.Processing;

internal static class PolylineUtils
{
    public static List<PointF> SimplifyRdp(List<PointF> points, float epsilon)
    {
        if (points.Count < 3 || epsilon <= 0)
        {
            return new List<PointF>(points);
        }

        int index = 0;
        double dmax = 0;
        var start = points[0];
        var end = points[^1];

        for (int i = 1; i < points.Count - 1; i++)
        {
            var d = PerpendicularDistance(points[i], start, end);
            if (d > dmax)
            {
                index = i;
                dmax = d;
            }
        }

        if (dmax > epsilon)
        {
            var first = SimplifyRdp(points.GetRange(0, index + 1), epsilon);
            var second = SimplifyRdp(points.GetRange(index, points.Count - index), epsilon);
            first.RemoveAt(first.Count - 1);
            first.AddRange(second);
            return first;
        }

        return new List<PointF> { start, end };
    }

    public static List<PointF> Resample(List<PointF> points, float spacing)
    {
        if (points.Count < 2 || spacing <= 0)
        {
            return new List<PointF>(points);
        }

        var resampled = new List<PointF> { points[0] };
        float remaining = spacing;

        for (int i = 1; i < points.Count; i++)
        {
            var prev = points[i - 1];
            var current = points[i];
            float segmentLength = Distance(prev, current);

            while (segmentLength >= remaining)
            {
                float t = remaining / segmentLength;
                var newPoint = Lerp(prev, current, t);
                resampled.Add(newPoint);
                prev = newPoint;
                segmentLength = Distance(prev, current);
                remaining = spacing;
            }

            remaining -= segmentLength;
        }

        if (resampled.Count == 0 || Distance(resampled[^1], points[^1]) > 0.01f)
        {
            resampled.Add(points[^1]);
        }

        return resampled;
    }

    public static List<PointF> SmoothChaikin(List<PointF> points, int iterations, bool preserveEndpoints)
    {
        if (iterations <= 0 || points.Count < 3)
        {
            return new List<PointF>(points);
        }

        var current = new List<PointF>(points);
        for (int iter = 0; iter < iterations; iter++)
        {
            var next = new List<PointF>();
            if (preserveEndpoints)
            {
                next.Add(current[0]);
            }

            for (int i = 0; i < current.Count - 1; i++)
            {
                var p0 = current[i];
                var p1 = current[i + 1];

                var q = new PointF(0.75f * p0.X + 0.25f * p1.X, 0.75f * p0.Y + 0.25f * p1.Y);
                var r = new PointF(0.25f * p0.X + 0.75f * p1.X, 0.25f * p0.Y + 0.75f * p1.Y);

                if (!preserveEndpoints || i > 0)
                {
                    next.Add(q);
                }
                next.Add(r);
            }

            if (preserveEndpoints)
            {
                next[^1] = current[^1];
            }

            current = next;
        }

        return current;
    }

    public static List<List<PointF>> OrderStrokes(List<List<PointF>> strokes)
    {
        if (strokes.Count == 0) return new List<List<PointF>>();

        var remaining = new List<List<PointF>>(strokes);
        var ordered = new List<List<PointF>>();

        int startIndex = 0;
        float bestMinX = float.MaxValue;
        for (int i = 0; i < remaining.Count; i++)
        {
            var stroke = remaining[i];
            if (stroke.Count == 0) continue;
            float minX = stroke[0].X;
            for (int j = 1; j < stroke.Count; j++)
            {
                if (stroke[j].X < minX) minX = stroke[j].X;
            }
            if (minX < bestMinX)
            {
                bestMinX = minX;
                startIndex = i;
            }
        }

        var current = remaining[startIndex];
        remaining.RemoveAt(startIndex);
        ordered.Add(new List<PointF>(current));

        var currentEnd = ordered[^1].Count > 0 ? ordered[^1][^1] : new PointF(0, 0);

        while (remaining.Count > 0)
        {
            int bestIndex = 0;
            bool reverse = false;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < remaining.Count; i++)
            {
                var stroke = remaining[i];
                if (stroke.Count == 0) continue;

                var start = stroke[0];
                var end = stroke[^1];
                float dStart = Distance(currentEnd, start);
                float dEnd = Distance(currentEnd, end);

                if (dStart < bestDistance)
                {
                    bestDistance = dStart;
                    bestIndex = i;
                    reverse = false;
                }

                if (dEnd < bestDistance)
                {
                    bestDistance = dEnd;
                    bestIndex = i;
                    reverse = true;
                }
            }

            var nextStroke = remaining[bestIndex];
            remaining.RemoveAt(bestIndex);
            if (reverse)
            {
                nextStroke = new List<PointF>(nextStroke);
                nextStroke.Reverse();
            }
            else
            {
                nextStroke = new List<PointF>(nextStroke);
            }
            ordered.Add(nextStroke);
            if (nextStroke.Count > 0)
            {
                currentEnd = nextStroke[^1];
            }
        }

        return ordered;
    }

    public static float Distance(PointF a, PointF b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static double PerpendicularDistance(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        if (Math.Abs(dx) < 0.0001f && Math.Abs(dy) < 0.0001f)
        {
            return Distance(p, a);
        }

        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
        double projX = a.X + t * dx;
        double projY = a.Y + t * dy;
        double pdx = p.X - projX;
        double pdy = p.Y - projY;
        return Math.Sqrt(pdx * pdx + pdy * pdy);
    }

    private static PointF Lerp(PointF a, PointF b, float t)
    {
        return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }
}
