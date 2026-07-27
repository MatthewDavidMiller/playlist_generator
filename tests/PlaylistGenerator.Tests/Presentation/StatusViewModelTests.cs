using PlaylistGenerator.Core.Exceptions;
using PlaylistGenerator.Presentation.ViewModels;

namespace PlaylistGenerator.Tests.Presentation;

public sealed class StatusViewModelTests
{
    [Fact]
    public void StartsReadyWithNoDiagnostics()
    {
        var status = new StatusViewModel();

        Assert.Equal(StatusViewModel.ReadyMessage, status.Message);
        Assert.False(status.HasErrorDetails);
    }

    [Fact]
    public void ReportsTheMessageAndFullDetailOfAFailure()
    {
        var status = new StatusViewModel();

        status.ReportFailure(new PlaylistValidationException("source is invalid"));

        Assert.Contains("source is invalid", status.Message, StringComparison.Ordinal);
        Assert.Contains(
            nameof(PlaylistValidationException),
            status.ErrorDetails,
            StringComparison.Ordinal);
        Assert.True(status.HasErrorDetails);
    }

    [Fact]
    public void StartingANewOperationClearsThePreviousDiagnostics()
    {
        var status = new StatusViewModel();
        status.ReportFailure(new PlaylistValidationException("old failure"));

        status.BeginOperation("Building playlist…");

        Assert.Equal("Building playlist…", status.Message);
        Assert.False(status.HasErrorDetails);
    }

    [Fact]
    public void ReportingProgressLeavesExistingDiagnosticsAlone()
    {
        var status = new StatusViewModel();
        status.ReportFailure(new PlaylistValidationException("failure"));

        status.Report("still working");

        Assert.Equal("still working", status.Message);
        Assert.True(status.HasErrorDetails);
    }

    [Fact]
    public void RaisesChangeNotificationForTheDerivedVisibilityFlag()
    {
        var status = new StatusViewModel();
        var changed = new List<string?>();
        status.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        status.ReportFailure(new PlaylistValidationException("failure"));

        // The expander binds to HasErrorDetails, so it has to be notified too.
        Assert.Contains(nameof(StatusViewModel.HasErrorDetails), changed);
    }

    [Fact]
    public void ReportingDetailsLeavesTheStatusLineAlone()
    {
        var status = new StatusViewModel();
        status.Report("Normalization complete.");

        status.ReportDetails("one.mp3: corrupt header");

        Assert.Equal("Normalization complete.", status.Message);
        Assert.True(status.HasErrorDetails);
        Assert.Equal("one.mp3: corrupt header", status.ErrorDetails);
    }

    [Fact]
    public void ReportingEmptyDetailsHidesTheExpander()
    {
        var status = new StatusViewModel();
        status.ReportDetails("something went wrong");

        status.ReportDetails(string.Empty);

        Assert.False(status.HasErrorDetails);
    }

    [Fact]
    public void RejectsANullException() =>
        Assert.Throws<ArgumentNullException>(() => new StatusViewModel().ReportFailure(null!));
}
