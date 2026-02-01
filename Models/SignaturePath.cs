using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SignatureMouse.Vector;

namespace SignatureMouse.Models;

internal sealed class SignaturePath
{
    public int Width { get; }
    public int Height { get; }
    public List<List<PointF>> Strokes { get; }

    public SignaturePath(int width, int height, List<List<PointF>> strokes)
    {
        Width = width;
        Height = height;
        Strokes = strokes;
    }

    public (float MinX, float MinY, float MaxX, float MaxY) GetBounds()
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var stroke in Strokes)
        {
            foreach (var p in stroke)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }

        if (minX == float.MaxValue)
        {
            minX = minY = maxX = maxY = 0;
        }

        return (minX, minY, maxX, maxY);
    }

    public void SaveSvg(string outputPath)
    {
        var ns = (XNamespace)"http://www.w3.org/2000/svg";
        var pathData = SvgPathUtils.BuildPath(Strokes);

        var svg = new XElement(ns + "svg",
            new XAttribute("xmlns", ns.NamespaceName),
            new XAttribute("width", Width.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("height", Height.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("viewBox", $"0 0 {Width.ToString(CultureInfo.InvariantCulture)} {Height.ToString(CultureInfo.InvariantCulture)}"),
            new XElement(ns + "path",
                new XAttribute("id", "signature"),
                new XAttribute("d", pathData),
                new XAttribute("fill", "none"),
                new XAttribute("stroke", "black"),
                new XAttribute("stroke-width", "1"))
        );

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), svg);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
        doc.Save(outputPath);
    }

    public static SignaturePath LoadSvg(string inputPath)
    {
        var doc = XDocument.Load(inputPath);
        var root = doc.Root ?? throw new InvalidDataException("Invalid SVG: missing root element.");
        var ns = root.Name.Namespace;

        var width = ParseLength(root.Attribute("width")?.Value) ?? 0;
        var height = ParseLength(root.Attribute("height")?.Value) ?? 0;

        var viewBox = root.Attribute("viewBox")?.Value;
        if (!string.IsNullOrWhiteSpace(viewBox))
        {
            var parts = viewBox.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                if (float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vbWidth))
                {
                    width = vbWidth;
                }
                if (float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var vbHeight))
                {
                    height = vbHeight;
                }
            }
        }

        var paths = root.Descendants(ns + "path").ToList();
        if (paths.Count == 0)
        {
            throw new InvalidDataException("Invalid SVG: no path elements found.");
        }

        var strokes = new List<List<PointF>>();
        foreach (var path in paths)
        {
            var d = path.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(d)) continue;
            strokes.AddRange(SvgPathUtils.ParsePath(d));
        }

        return new SignaturePath((int)MathF.Round(width), (int)MathF.Round(height), strokes);
    }

    private static float? ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        var digits = new string(trimmed.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        if (float.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return null;
    }
}
