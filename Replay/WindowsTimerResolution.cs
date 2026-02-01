using System;
using System.Runtime.InteropServices;

namespace SignatureMouse.Replay;

internal static class WindowsTimerResolution
{
    public static IDisposable Begin(uint milliseconds)
    {
        if (!OperatingSystem.IsWindows())
        {
            return NoopScope.Instance;
        }

        timeBeginPeriod(milliseconds);
        return new Scope(milliseconds);
    }

    private sealed class Scope : IDisposable
    {
        private readonly uint _milliseconds;
        private bool _disposed;

        public Scope(uint milliseconds)
        {
            _milliseconds = milliseconds;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            timeEndPeriod(_milliseconds);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);
}
