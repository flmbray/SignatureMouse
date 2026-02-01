namespace SignatureMouse.Models;

internal readonly struct PointF
{
    public float X { get; }
    public float Y { get; }

    public PointF(float x, float y)
    {
        X = x;
        Y = y;
    }
}
