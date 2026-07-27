using PlaylistGenerator.Core.Models;

namespace PlaylistGenerator.Tests.Core;

public sealed class ProcessResultTests
{
    [Fact]
    public void PrefersStandardErrorForDiagnostics()
    {
        var result = new ProcessResult(1, "routine progress", "the real cause");

        Assert.Equal("the real cause", result.Diagnostics);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FallsBackToStandardOutputWhenErrorIsBlank(string standardError)
    {
        // Some tools report their failure on standard output, which would otherwise leave
        // the user with an error message that explains nothing.
        var result = new ProcessResult(1, "codec not found", standardError);

        Assert.Equal("codec not found", result.Diagnostics);
    }

    [Fact]
    public void HasNoDiagnosticsWhenBothStreamsAreEmpty() =>
        Assert.Equal(string.Empty, new ProcessResult(1, string.Empty, string.Empty).Diagnostics);
}
