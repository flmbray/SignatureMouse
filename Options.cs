namespace SignatureMouse;

internal sealed record AnalyzeOptions(
    string InputPath,
    string OutputPath,
    int? MinComponentSize,
    float SimplifyEpsilon,
    float ResampleSpacing,
    int MaxSize,
    string? SaveCleanedPath,
    bool Despeckle,
    int? Threshold,
    bool Invert,
    float RotationDegrees,
    int ThinIterations,
    int CloseRadius,
    int SmoothIterations
);

internal sealed record ReplayOptions(
    string InputPath,
    double DelaySeconds,
    float SpeedPixelsPerSecond,
    float StepPixels,
    float Scale,
    float? TargetWidth,
    float? TargetHeight,
    float? OffsetX,
    float? OffsetY,
    string Backend,
    bool DrawRect,
    bool Plop,
    float Padding
);
