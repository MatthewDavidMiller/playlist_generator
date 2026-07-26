namespace PlaylistGenerator.Tests.TestSupport;

/// <summary>
/// Sets a process environment variable for the duration of a test and then restores it.
/// </summary>
/// <remarks>
/// Environment variables are process-global. xUnit runs the methods of one test class
/// sequentially, so only classes that touch the same variable would conflict; keep all such
/// tests in a single class.
/// </remarks>
public sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _originalValue;

    public EnvironmentVariableScope(string name, string? value)
    {
        _name = name;
        _originalValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _originalValue);
}
