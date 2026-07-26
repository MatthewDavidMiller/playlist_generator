using PlaylistGenerator.CommandLine.Parsing;

namespace PlaylistGenerator.Tests.CommandLine;

public sealed class OptionParserTests
{
    private const string Source = "--source-directory";
    private const string Output = "--output-directory";

    [Fact]
    public void ParsesOptionValuePairs()
    {
        var options = OptionParser.Parse([Source, "music", Output, "out"], Source, Output);

        Assert.Equal("music", options.Required(Source));
        Assert.Equal("out", options.Required(Output));
    }

    [Fact]
    public void AcceptsValuesContainingSpacesAndLeadingDashes()
    {
        var options = OptionParser.Parse([Source, "-my music-"], Source);

        Assert.Equal("-my music-", options.Required(Source));
    }

    [Fact]
    public void RejectsUnknownOptions()
    {
        var exception = Assert.Throws<CliUsageException>(
            () => OptionParser.Parse(["--unknown", "value"], Source));

        Assert.Contains("Unknown option", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsRepeatedOptions()
    {
        var exception = Assert.Throws<CliUsageException>(
            () => OptionParser.Parse([Source, "a", Source, "b"], Source));

        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnOptionWithNoValue()
    {
        var exception = Assert.Throws<CliUsageException>(
            () => OptionParser.Parse([Source], Source));

        Assert.Contains("requires a value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnOptionWhoseValueLooksLikeAnotherOption()
    {
        // Without this check, --source-directory would silently swallow --output-directory.
        var exception = Assert.Throws<CliUsageException>(
            () => OptionParser.Parse([Source, Output, "out"], Source, Output));

        Assert.Contains("looks like an option", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void RejectsHelpMixedIntoOptions(string helpFlag)
    {
        var exception = Assert.Throws<CliUsageException>(
            () => OptionParser.Parse([Source, "music", helpFlag], Source));

        Assert.Contains("--help", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void RecognizesALoneHelpRequest(string helpFlag) =>
        Assert.True(OptionParser.IsHelpRequest([helpFlag]));

    // Each case is wrapped so the string array is one argument rather than the params list.
    [Theory]
    [InlineData(new object[] { new string[0] })]
    [InlineData(new object[] { new[] { "--help", "extra" } })]
    [InlineData(new object[] { new[] { "normalize-volume" } })]
    public void DoesNotTreatOtherArgumentsAsHelp(string[] arguments) =>
        Assert.False(OptionParser.IsHelpRequest(arguments));

    [Fact]
    public void ReportsMissingRequiredOptions()
    {
        var options = OptionParser.Parse([], Source);

        var exception = Assert.Throws<CliUsageException>(() => options.Required(Source));
        Assert.Contains("is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesIntegersInvariantly()
    {
        var options = OptionParser.Parse(["--insert-every", "42"], "--insert-every");

        Assert.Equal(42, options.RequiredInteger("--insert-every"));
    }

    [Theory]
    [InlineData("four")]
    [InlineData("4.5")]
    [InlineData("1,000")]
    [InlineData("")]
    public void RejectsNonIntegerValues(string value)
    {
        var options = OptionParser.Parse(["--insert-every", value], "--insert-every");

        Assert.Throws<CliUsageException>(() => options.RequiredInteger("--insert-every"));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => OptionParser.Parse(null!, Source));
        Assert.Throws<ArgumentNullException>(() => OptionParser.Parse([], null!));
    }
}
