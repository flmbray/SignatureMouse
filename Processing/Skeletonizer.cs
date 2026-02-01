using System.Collections.Generic;

namespace SignatureMouse.Processing;

internal static class Skeletonizer
{
    public static bool[,] Thin(bool[,] source, int maxIterations = -1)
    {
        var width = source.GetLength(0);
        var height = source.GetLength(1);
        var img = new bool[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                img[x, y] = source[x, y];
            }
        }

        bool changed;
        var toRemove = new List<(int X, int Y)>();

        int iterations = 0;
        do
        {
            changed = false;
            toRemove.Clear();

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    if (!img[x, y]) continue;

                    var (p2, p3, p4, p5, p6, p7, p8, p9) = Neighbors(img, x, y);
                    int b = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9;
                    if (b < 2 || b > 6) continue;
                    int a = Transitions(p2, p3, p4, p5, p6, p7, p8, p9);
                    if (a != 1) continue;
                    if (p2 * p4 * p6 != 0) continue;
                    if (p4 * p6 * p8 != 0) continue;

                    toRemove.Add((x, y));
                }
            }

            if (toRemove.Count > 0)
            {
                foreach (var (x, y) in toRemove)
                {
                    img[x, y] = false;
                }
                changed = true;
            }

            toRemove.Clear();

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    if (!img[x, y]) continue;

                    var (p2, p3, p4, p5, p6, p7, p8, p9) = Neighbors(img, x, y);
                    int b = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9;
                    if (b < 2 || b > 6) continue;
                    int a = Transitions(p2, p3, p4, p5, p6, p7, p8, p9);
                    if (a != 1) continue;
                    if (p2 * p4 * p8 != 0) continue;
                    if (p2 * p6 * p8 != 0) continue;

                    toRemove.Add((x, y));
                }
            }

            if (toRemove.Count > 0)
            {
                foreach (var (x, y) in toRemove)
                {
                    img[x, y] = false;
                }
                changed = true;
            }

            iterations++;
            if (maxIterations > 0 && iterations >= maxIterations)
            {
                break;
            }

        } while (changed);

        return img;
    }

    private static (int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9) Neighbors(bool[,] img, int x, int y)
    {
        int p2 = img[x, y - 1] ? 1 : 0;
        int p3 = img[x + 1, y - 1] ? 1 : 0;
        int p4 = img[x + 1, y] ? 1 : 0;
        int p5 = img[x + 1, y + 1] ? 1 : 0;
        int p6 = img[x, y + 1] ? 1 : 0;
        int p7 = img[x - 1, y + 1] ? 1 : 0;
        int p8 = img[x - 1, y] ? 1 : 0;
        int p9 = img[x - 1, y - 1] ? 1 : 0;
        return (p2, p3, p4, p5, p6, p7, p8, p9);
    }

    private static int Transitions(int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9)
    {
        int transitions = 0;
        if (p2 == 0 && p3 == 1) transitions++;
        if (p3 == 0 && p4 == 1) transitions++;
        if (p4 == 0 && p5 == 1) transitions++;
        if (p5 == 0 && p6 == 1) transitions++;
        if (p6 == 0 && p7 == 1) transitions++;
        if (p7 == 0 && p8 == 1) transitions++;
        if (p8 == 0 && p9 == 1) transitions++;
        if (p9 == 0 && p2 == 1) transitions++;
        return transitions;
    }
}
