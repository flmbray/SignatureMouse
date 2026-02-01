namespace SignatureMouse.Processing;

internal sealed class BinaryImage
{
    public int Width { get; }
    public int Height { get; }
    public bool[,] Ink { get; }

    public BinaryImage(int width, int height, bool[,] ink)
    {
        Width = width;
        Height = height;
        Ink = ink;
    }
}
