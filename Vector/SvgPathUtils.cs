using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SignatureMouse.Models;

namespace SignatureMouse.Vector;

internal static class SvgPathUtils
{
    private static readonly Regex TokenRegex = new Regex(@"[ML]|[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    public static string BuildPath(IReadOnlyList<IReadOnlyList<PointF>> strokes)
    {
        var sb = new StringBuilder();
        foreach (var stroke in strokes)
        {
            if (stroke.Count == 0) continue;
            var p0 = stroke[0];
            sb.AppendFormat(CultureInfo.InvariantCulture, "M {0:F2} {1:F2} ", p0.X, p0.Y);
            for (int i = 1; i < stroke.Count; i++)
            {
                var p = stroke[i];
                sb.AppendFormat(CultureInfo.InvariantCulture, "L {0:F2} {1:F2} ", p.X, p.Y);
            }
        }
        return sb.ToString().Trim();
    }

    public static List<List<PointF>> ParsePath(string d)
    {
        var strokes = new List<List<PointF>>();
        if (string.IsNullOrWhiteSpace(d)) return strokes;

        var tokens = TokenRegex.Matches(d);
        char currentCmd = '\0';
        List<PointF>? current = null;

        int i = 0;
        while (i < tokens.Count)
        {
            var token = tokens[i].Value;
            if (token.Length == 1 && (token[0] == 'M' || token[0] == 'L'))
            {
                currentCmd = token[0];
                i++;
                continue;
            }

            if (i + 1 >= tokens.Count) break;

            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) break;
            if (!float.TryParse(tokens[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) break;
            i += 2;

            if (currentCmd == 'M')
            {
                current = new List<PointF> { new PointF(x, y) };
                strokes.Add(current);
                currentCmd = 'L';
            }
            else if (currentCmd == 'L')
            {
                if (current == null)
                {
                    current = new List<PointF>();
                    strokes.Add(current);
                }
                current.Add(new PointF(x, y));
            }
            else
            {
                current = new List<PointF> { new PointF(x, y) };
                strokes.Add(current);
                currentCmd = 'L';
            }
        }

        return strokes;
    }
}
