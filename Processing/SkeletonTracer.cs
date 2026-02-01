using System.Collections.Generic;
using SignatureMouse.Models;

namespace SignatureMouse.Processing;

internal static class SkeletonTracer
{
    private static readonly (int dx, int dy)[] NeighborOffsets =
    {
        (-1, -1), (0, -1), (1, -1),
        (-1,  0),          (1,  0),
        (-1,  1), (0,  1), (1,  1)
    };

    public static List<List<PointF>> Trace(bool[,] skeleton)
    {
        var width = skeleton.GetLength(0);
        var height = skeleton.GetLength(1);
        var degree = new int[width, height];
        var visited = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!skeleton[x, y]) continue;
                int deg = 0;
                foreach (var (dx, dy) in NeighborOffsets)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    if (skeleton[nx, ny]) deg++;
                }
                degree[x, y] = deg;
            }
        }

        var strokes = new List<List<PointF>>();
        var queue = new Queue<(int X, int Y)>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!skeleton[x, y] || visited[x, y]) continue;

                var component = new List<(int X, int Y)>();
                visited[x, y] = true;
                queue.Enqueue((x, y));

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    component.Add((cx, cy));

                    foreach (var (nx, ny) in GetNeighbors(skeleton, cx, cy))
                    {
                        if (visited[nx, ny]) continue;
                        visited[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }

                // Walk each connected skeleton as a single continuous path to reduce pen-up artifacts.
                var start = ChooseStart(component, degree);
                var stroke = WalkComponent(skeleton, degree, width, start.X, start.Y);
                if (stroke.Count > 0)
                {
                    strokes.Add(stroke);
                }
            }
        }

        return strokes;
    }

    private static (int X, int Y) ChooseStart(List<(int X, int Y)> component, int[,] degree)
    {
        // Prefer endpoints to get more natural stroke direction; otherwise pick left-most.
        (int X, int Y)? bestEndpoint = null;
        (int X, int Y)? best = null;

        foreach (var (x, y) in component)
        {
            var candidate = (x, y);
            if (!best.HasValue || x < best.Value.X || (x == best.Value.X && y < best.Value.Y))
            {
                best = candidate;
            }

            if (degree[x, y] == 1)
            {
                if (!bestEndpoint.HasValue || x < bestEndpoint.Value.X || (x == bestEndpoint.Value.X && y < bestEndpoint.Value.Y))
                {
                    bestEndpoint = candidate;
                }
            }
        }

        return bestEndpoint ?? best ?? (0, 0);
    }

    private static List<PointF> WalkComponent(bool[,] skeleton, int[,] degree, int width, int startX, int startY)
    {
        var visitedEdges = new HashSet<long>();
        var stack = new Stack<(int X, int Y)>();
        var points = new List<PointF>();

        int curX = startX;
        int curY = startY;
        int prevX = -1;
        int prevY = -1;
        points.Add(new PointF(curX, curY));

        // Depth-first walk that biases straight motion at junctions for smoother strokes.
        while (true)
        {
            if (TrySelectNext(curX, curY, prevX, prevY, skeleton, degree, visitedEdges, width, out var next))
            {
                MarkEdge(visitedEdges, width, curX, curY, next.X, next.Y);
                stack.Push((curX, curY));
                prevX = curX;
                prevY = curY;
                curX = next.X;
                curY = next.Y;
                points.Add(new PointF(curX, curY));
                continue;
            }

            if (stack.Count == 0)
            {
                break;
            }

            var back = stack.Pop();
            prevX = curX;
            prevY = curY;
            curX = back.X;
            curY = back.Y;
            points.Add(new PointF(curX, curY));
        }

        return points;
    }

    private static bool TrySelectNext(
        int x,
        int y,
        int prevX,
        int prevY,
        bool[,] skeleton,
        int[,] degree,
        HashSet<long> visitedEdges,
        int width,
        out (int X, int Y) next)
    {
        bool hasCandidate = false;
        int bestX = 0;
        int bestY = 0;
        int bestDot = int.MinValue;
        int bestDegree = int.MaxValue;

        int dx1 = prevX >= 0 ? x - prevX : 0;
        int dy1 = prevY >= 0 ? y - prevY : 0;

        foreach (var (nx, ny) in GetNeighbors(skeleton, x, y))
        {
            if (IsEdgeVisited(visitedEdges, width, x, y, nx, ny)) continue;

            int dot;
            if (prevX < 0)
            {
                dot = 0;
            }
            else
            {
                int dx2 = nx - x;
                int dy2 = ny - y;
                dot = dx1 * dx2 + dy1 * dy2;
            }

            int neighborDegree = degree[nx, ny];
            if (!hasCandidate || dot > bestDot || (dot == bestDot && neighborDegree < bestDegree))
            {
                hasCandidate = true;
                bestX = nx;
                bestY = ny;
                bestDot = dot;
                bestDegree = neighborDegree;
            }
        }

        next = hasCandidate ? (bestX, bestY) : (0, 0);
        return hasCandidate;
    }

    private static IEnumerable<(int X, int Y)> GetNeighbors(bool[,] skeleton, int x, int y)
    {
        int width = skeleton.GetLength(0);
        int height = skeleton.GetLength(1);
        foreach (var (dx, dy) in NeighborOffsets)
        {
            int nx = x + dx;
            int ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            if (skeleton[nx, ny]) yield return (nx, ny);
        }
    }

    private static void MarkEdge(HashSet<long> visitedEdges, int width, int ax, int ay, int bx, int by)
    {
        var key = EdgeKey(width, ax, ay, bx, by);
        visitedEdges.Add(key);
    }

    private static bool IsEdgeVisited(HashSet<long> visitedEdges, int width, int ax, int ay, int bx, int by)
    {
        var key = EdgeKey(width, ax, ay, bx, by);
        return visitedEdges.Contains(key);
    }

    private static long EdgeKey(int width, int ax, int ay, int bx, int by)
    {
        int a = ay * width + ax;
        int b = by * width + bx;
        if (a > b) (a, b) = (b, a);
        return ((long)a << 32) | (uint)b;
    }
}
