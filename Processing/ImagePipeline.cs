using System;
using System.Collections.Generic;
using System.IO;
using SignatureMouse;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SignatureMouse.Processing;

internal static class ImagePipeline
{
    public static BinaryImage Process(string inputPath, AnalyzeOptions options)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Input image not found.", inputPath);
        }

        using var image = Image.Load<Rgba32>(inputPath);
        ApplyRotation(image, options.RotationDegrees);
        if (options.MaxSize > 0 && (image.Width > options.MaxSize || image.Height > options.MaxSize))
        {
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(options.MaxSize, options.MaxSize)
            }));
        }

        var width = image.Width;
        var height = image.Height;
        var gray = new byte[width, height];
        var alpha = new byte[width, height];
        var histogram = new int[256];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var p = image[x, y];
                var g = (byte)Math.Clamp((int)(0.2126f * p.R + 0.7152f * p.G + 0.0722f * p.B), 0, 255);
                gray[x, y] = g;
                alpha[x, y] = p.A;
                if (p.A > 0)
                {
                    histogram[g]++;
                }
            }
        }

        int totalPixels = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            totalPixels += histogram[i];
        }
        if (totalPixels == 0)
        {
            return new BinaryImage(width, height, new bool[width, height]);
        }

        int threshold = options.Threshold ?? OtsuThreshold(histogram, totalPixels);
        bool useDarkInk = !options.Invert;

        if (!options.Threshold.HasValue)
        {
            // Auto-select both threshold and whether ink is dark or light to handle inverted/washed scans.
            var (autoThreshold, autoUseDark) = AutoSelectThreshold(histogram, totalPixels, threshold);
            threshold = autoThreshold;
            useDarkInk = autoUseDark;
        }

        var ink = new bool[width, height];
        int inkCount = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (alpha[x, y] == 0)
                {
                    ink[x, y] = false;
                    continue;
                }
                var value = gray[x, y];
                var isInk = useDarkInk ? value < threshold : value > threshold;
                ink[x, y] = isInk;
                if (isInk) inkCount++;
            }
        }

        if (options.Despeckle)
        {
            ink = Despeckle(ink, width, height);
        }

        ink = RemoveSmallComponents(ink, width, height, options.MinComponentSize);
        if (options.CloseRadius > 0)
        {
            ink = Close(ink, width, height, options.CloseRadius);
        }

        var trimmed = TrimToSignature(ink, width, height);

        return trimmed;
    }

    private static int OtsuThreshold(int[] histogram, int totalPixels)
    {
        double sum = 0;
        for (int t = 0; t < 256; t++)
        {
            sum += t * histogram[t];
        }

        double sumB = 0;
        int wB = 0;
        int wF = 0;
        double maxVariance = 0;
        int threshold = 0;

        for (int t = 0; t < 256; t++)
        {
            wB += histogram[t];
            if (wB == 0) continue;

            wF = totalPixels - wB;
            if (wF == 0) break;

            sumB += t * histogram[t];

            double mB = sumB / wB;
            double mF = (sum - sumB) / wF;

            double between = wB * wF * Math.Pow(mB - mF, 2);
            if (between > maxVariance)
            {
                maxVariance = between;
                threshold = t;
            }
        }

        return threshold;
    }

    private static (int Threshold, bool UseDarkInk) AutoSelectThreshold(int[] histogram, int totalPixels, int otsuThreshold)
    {
        // Otsu can land on the background in very light scans; guard by validating expected ink ratios.
        const float MinInkRatio = 0.0005f;
        const float MaxInkRatio = 0.5f;

        var cumulative = BuildCumulative(histogram);
        int darkCount = CountBelow(cumulative, otsuThreshold);
        float darkRatio = darkCount / (float)totalPixels;
        if (IsRatioOk(darkRatio, MinInkRatio, MaxInkRatio))
        {
            return (otsuThreshold, true);
        }

        int lightCount = totalPixels - CountBelowOrEqual(cumulative, otsuThreshold);
        float lightRatio = lightCount / (float)totalPixels;
        if (IsRatioOk(lightRatio, MinInkRatio, MaxInkRatio))
        {
            return (otsuThreshold, false);
        }

        // Fallback: use the bright background percentile and bias toward a thin ink ratio.
        int background = Percentile(histogram, totalPixels, 0.95f);
        int fallback = Math.Clamp(background - 25, 0, 255);
        int fallbackDark = CountBelow(cumulative, fallback);
        int fallbackLight = totalPixels - CountBelowOrEqual(cumulative, fallback);

        float target = 0.02f;
        float darkScore = Math.Abs(fallbackDark / (float)totalPixels - target);
        float lightScore = Math.Abs(fallbackLight / (float)totalPixels - target);
        return darkScore <= lightScore ? (fallback, true) : (fallback, false);
    }

    private static int[] BuildCumulative(int[] histogram)
    {
        var cumulative = new int[histogram.Length];
        int running = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            running += histogram[i];
            cumulative[i] = running;
        }
        return cumulative;
    }

    private static int CountBelow(int[] cumulative, int threshold)
    {
        if (threshold <= 0) return 0;
        if (threshold - 1 >= cumulative.Length) return cumulative[^1];
        return cumulative[threshold - 1];
    }

    private static int CountBelowOrEqual(int[] cumulative, int threshold)
    {
        if (threshold < 0) return 0;
        if (threshold >= cumulative.Length) return cumulative[^1];
        return cumulative[threshold];
    }

    private static bool IsRatioOk(float ratio, float min, float max)
    {
        return ratio >= min && ratio <= max;
    }

    private static int Percentile(int[] histogram, int totalPixels, float percentile)
    {
        int target = (int)MathF.Round(totalPixels * percentile);
        int cumulative = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            cumulative += histogram[i];
            if (cumulative >= target) return i;
        }
        return histogram.Length - 1;
    }

    private static bool[,] Despeckle(bool[,] ink, int width, int height)
    {
        var output = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    output[x, y] = ink[x, y];
                    continue;
                }

                int count = 0;
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        if (ink[x + ox, y + oy]) count++;
                    }
                }

                if (ink[x, y])
                {
                    output[x, y] = count >= 2;
                }
                else
                {
                    output[x, y] = count >= 6;
                }
            }
        }

        return output;
    }

    private static bool[,] RemoveSmallComponents(bool[,] ink, int width, int height, int? minComponentSize)
    {
        var visited = new bool[width, height];
        var components = new List<List<(int X, int Y)>>();
        int largest = 0;
        int totalInk = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (ink[x, y]) totalInk++;
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!ink[x, y] || visited[x, y]) continue;

                var queue = new Queue<(int X, int Y)>();
                var pixels = new List<(int X, int Y)>();
                queue.Enqueue((x, y));
                visited[x, y] = true;

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    pixels.Add((cx, cy));

                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            int nx = cx + ox;
                            int ny = cy + oy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            if (visited[nx, ny] || !ink[nx, ny]) continue;
                            visited[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                if (pixels.Count > largest) largest = pixels.Count;
                components.Add(pixels);
            }
        }

        int threshold = minComponentSize ?? Math.Max(10, (int)(totalInk * 0.002));
        var output = new bool[width, height];

        foreach (var component in components)
        {
            if (component.Count >= threshold || component.Count == largest)
            {
                foreach (var (cx, cy) in component)
                {
                    output[cx, cy] = true;
                }
            }
        }

        return output;
    }

    internal static void SaveBinaryPreview(bool[,] ink, int width, int height, string outputPath)
    {
        using var image = new Image<L8>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = ink[x, y] ? new L8(0) : new L8(255);
            }
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
        image.Save(outputPath);
    }

    private static BinaryImage TrimToSignature(bool[,] ink, int width, int height)
    {
        var components = FindComponents(ink, width, height);
        if (components.Count == 0)
        {
            return new BinaryImage(width, height, ink);
        }

        // Pick the main signature blob by balancing size, density, and proximity to image center.
        float centerX = width / 2f;
        float centerY = height / 2f;
        float maxDist = MathF.Sqrt(centerX * centerX + centerY * centerY);
        float bestScore = float.MinValue;
        Component? best = null;

        foreach (var component in components)
        {
            float boxWidth = component.MaxX - component.MinX + 1;
            float boxHeight = component.MaxY - component.MinY + 1;
            float area = Math.Max(1, boxWidth * boxHeight);
            float density = component.Count / area;

            float compCenterX = (component.MinX + component.MaxX) / 2f;
            float compCenterY = (component.MinY + component.MaxY) / 2f;
            float dist = MathF.Sqrt(MathF.Pow(compCenterX - centerX, 2) + MathF.Pow(compCenterY - centerY, 2));
            float distScore = 1f - MathF.Min(1f, dist / maxDist);

            bool inMiddle = compCenterX > width * 0.2f && compCenterX < width * 0.8f &&
                            compCenterY > height * 0.2f && compCenterY < height * 0.8f;
            float middleBonus = inMiddle ? 1.2f : 0.7f;
            float borderPenalty = component.TouchesBorder ? 0.7f : 1f;
            float densityBonus = Clamp(density * 8f, 0.5f, 1.2f);

            float score = component.Count * (0.4f + 0.6f * distScore) * middleBonus * borderPenalty * densityBonus;
            if (score > bestScore)
            {
                bestScore = score;
                best = component;
            }
        }

        var chosen = best ?? components[0];
        int groupMinX = chosen.MinX;
        int groupMinY = chosen.MinY;
        int groupMaxX = chosen.MaxX;
        int groupMaxY = chosen.MaxY;

        int chosenWidth = chosen.MaxX - chosen.MinX + 1;
        int chosenHeight = chosen.MaxY - chosen.MinY + 1;
        // Merge nearby components to avoid chopping off separated letters/initials.
        int gap = Math.Max(10, (int)(Math.Min(width, height) * 0.12f));
        gap = Math.Max(gap, (int)(Math.Max(chosenWidth, chosenHeight) * 0.2f));
        bool allowBorder = chosen.TouchesBorder;

        var included = new bool[components.Count];
        int chosenIndex = components.IndexOf(chosen);
        if (chosenIndex >= 0) included[chosenIndex] = true;

        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < components.Count; i++)
            {
                if (included[i]) continue;
                var component = components[i];
                if (!allowBorder && component.TouchesBorder) continue;
                if (BoxDistance(component, groupMinX, groupMinY, groupMaxX, groupMaxY) > gap) continue;

                included[i] = true;
                groupMinX = Math.Min(groupMinX, component.MinX);
                groupMinY = Math.Min(groupMinY, component.MinY);
                groupMaxX = Math.Max(groupMaxX, component.MaxX);
                groupMaxY = Math.Max(groupMaxY, component.MaxY);
                changed = true;
            }
        } while (changed);

        int margin = Math.Max(5, (int)(Math.Min(width, height) * 0.02f));
        int minX = Math.Max(0, groupMinX - margin);
        int minY = Math.Max(0, groupMinY - margin);
        int maxX = Math.Min(width - 1, groupMaxX + margin);
        int maxY = Math.Min(height - 1, groupMaxY + margin);

        int newWidth = maxX - minX + 1;
        int newHeight = maxY - minY + 1;
        var cropped = new bool[newWidth, newHeight];

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                cropped[x - minX, y - minY] = ink[x, y];
            }
        }

        return new BinaryImage(newWidth, newHeight, cropped);
    }

    private static bool[,] Close(bool[,] ink, int width, int height, int radius)
    {
        var offsets = BuildOffsets(radius);
        var dilated = Dilate(ink, width, height, offsets);
        return Erode(dilated, width, height, offsets);
    }

    private static bool[,] Dilate(bool[,] ink, int width, int height, (int dx, int dy)[] offsets)
    {
        var output = new bool[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (ink[x, y])
                {
                    output[x, y] = true;
                    continue;
                }

                foreach (var (dx, dy) in offsets)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    if (ink[nx, ny])
                    {
                        output[x, y] = true;
                        break;
                    }
                }
            }
        }

        return output;
    }

    private static bool[,] Erode(bool[,] ink, int width, int height, (int dx, int dy)[] offsets)
    {
        var output = new bool[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!ink[x, y]) continue;

                bool keep = true;
                foreach (var (dx, dy) in offsets)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    {
                        keep = false;
                        break;
                    }
                    if (!ink[nx, ny])
                    {
                        keep = false;
                        break;
                    }
                }
                output[x, y] = keep;
            }
        }

        return output;
    }

    private static (int dx, int dy)[] BuildOffsets(int radius)
    {
        if (radius <= 0) return Array.Empty<(int dx, int dy)>();
        var offsets = new List<(int dx, int dy)>();
        int r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (dx * dx + dy * dy <= r2)
                {
                    offsets.Add((dx, dy));
                }
            }
        }
        return offsets.ToArray();
    }

    private static void ApplyRotation(Image<Rgba32> image, float degrees)
    {
        if (Math.Abs(degrees) < 0.01f) return;

        var angle = NormalizeAngle(degrees);
        if (Math.Abs(angle) < 0.01f) return;

        if (!IsRightAngle(angle))
        {
            throw new ArgumentException("Rotation must be one of -90, 90, or 180 degrees.");
        }

        image.Mutate(ctx => ctx.Rotate(ToRotateMode(angle)));
    }

    private static float NormalizeAngle(float degrees)
    {
        float angle = degrees % 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }

    private static bool IsRightAngle(float angle)
    {
        return Math.Abs(angle - 90f) < 0.01f ||
               Math.Abs(angle + 90f) < 0.01f ||
               Math.Abs(Math.Abs(angle) - 180f) < 0.01f;
    }

    private static RotateMode ToRotateMode(float angle)
    {
        if (Math.Abs(angle - 90f) < 0.01f)
        {
            return RotateMode.Rotate270; // CCW 90
        }
        if (Math.Abs(angle + 90f) < 0.01f)
        {
            return RotateMode.Rotate90; // CW 90
        }
        return RotateMode.Rotate180;
    }

    private sealed class Component
    {
        public int Count { get; set; }
        public int MinX { get; set; } = int.MaxValue;
        public int MinY { get; set; } = int.MaxValue;
        public int MaxX { get; set; } = int.MinValue;
        public int MaxY { get; set; } = int.MinValue;
        public bool TouchesBorder { get; set; }
    }

    private static List<Component> FindComponents(bool[,] ink, int width, int height)
    {
        var components = new List<Component>();
        var visited = new bool[width, height];
        var queue = new Queue<(int X, int Y)>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!ink[x, y] || visited[x, y]) continue;

                var component = new Component();
                queue.Enqueue((x, y));
                visited[x, y] = true;

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    component.Count++;
                    if (cx < component.MinX) component.MinX = cx;
                    if (cy < component.MinY) component.MinY = cy;
                    if (cx > component.MaxX) component.MaxX = cx;
                    if (cy > component.MaxY) component.MaxY = cy;

                    if (cx == 0 || cy == 0 || cx == width - 1 || cy == height - 1)
                    {
                        component.TouchesBorder = true;
                    }

                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            int nx = cx + ox;
                            int ny = cy + oy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            if (visited[nx, ny] || !ink[nx, ny]) continue;
                            visited[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                components.Add(component);
            }
        }

        return components;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static int BoxDistance(Component component, int minX, int minY, int maxX, int maxY)
    {
        int dx = 0;
        if (component.MinX > maxX) dx = component.MinX - maxX;
        else if (minX > component.MaxX) dx = minX - component.MaxX;

        int dy = 0;
        if (component.MinY > maxY) dy = component.MinY - maxY;
        else if (minY > component.MaxY) dy = minY - component.MaxY;

        return Math.Max(dx, dy);
    }
}
