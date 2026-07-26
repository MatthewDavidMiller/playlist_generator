namespace PlaylistGenerator.Core.Abstractions;

/// <summary>
/// Resolves an executable to an absolute path, by search path or by explicit path.
/// </summary>
public interface IExecutableLocator
{
    /// <summary>
    /// Returns the absolute path to <paramref name="executableName"/>, or <see langword="null"/>
    /// when it cannot be found or is not executable.
    /// </summary>
    /// <param name="executableName">
    /// A bare command name resolved against the search path, or a path containing a
    /// directory separator, which is resolved directly.
    /// </param>
    string? Find(string executableName);
}
