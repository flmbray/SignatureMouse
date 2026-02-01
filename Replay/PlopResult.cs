namespace SignatureMouse.Replay;

internal readonly struct PlopResult
{
    public float Scale { get; }
    public float OffsetX { get; }
    public float OffsetY { get; }

    public PlopResult(float scale, float offsetX, float offsetY)
    {
        Scale = scale;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }
}
