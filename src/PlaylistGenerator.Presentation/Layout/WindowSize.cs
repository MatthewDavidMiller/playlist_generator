namespace PlaylistGenerator.Presentation.Layout;

/// <summary>
/// A width and height in device-independent pixels.
/// </summary>
/// <remarks>
/// Presentation stays free of Avalonia types, so sizes cross the boundary as this record
/// rather than as <c>Avalonia.Size</c>.
/// </remarks>
/// <param name="Width">Width in device-independent pixels.</param>
/// <param name="Height">Height in device-independent pixels.</param>
public readonly record struct WindowSize(double Width, double Height);
