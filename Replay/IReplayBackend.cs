namespace SignatureMouse.Replay;

internal interface IReplayBackend
{
    string Name { get; }
    int ScreenWidth { get; }
    int ScreenHeight { get; }
    (int X, int Y) GetCursorPosition();
    void MoveTo(int x, int y);
    void MouseDown();
    void MouseUp();
}
