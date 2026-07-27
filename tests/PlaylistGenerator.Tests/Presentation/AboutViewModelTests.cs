using PlaylistGenerator.Presentation.ViewModels;

namespace PlaylistGenerator.Tests.Presentation;

/// <summary>
/// Covers the about tab's content, which a published binary carries as its licence notice.
/// </summary>
public sealed class AboutViewModelTests
{
    [Fact]
    public void TheLicenceTextIsCarriedInTheBuild()
    {
        var about = new AboutViewModel();

        Assert.Contains("MIT License", about.LicenseText, StringComparison.Ordinal);
        Assert.Contains(
            "Permission is hereby granted, free of charge",
            about.LicenseText,
            StringComparison.Ordinal);
        Assert.Contains("WITHOUT WARRANTY", about.LicenseText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCopyrightHolderIsTakenFromTheLicence()
    {
        var about = new AboutViewModel();

        Assert.StartsWith("Copyright", about.Copyright, StringComparison.Ordinal);
        Assert.Contains("Matthew David Miller", about.Copyright, StringComparison.Ordinal);

        // The line is quoted from the licence rather than restated beside it.
        Assert.Contains(about.Copyright, about.LicenseText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProjectLinkIsAnAbsoluteAddressOfTheRepository()
    {
        var about = new AboutViewModel();

        Assert.True(about.ProjectUrl.IsAbsoluteUri);
        Assert.Equal(Uri.UriSchemeHttps, about.ProjectUrl.Scheme);
        Assert.Equal("github.com", about.ProjectUrl.Host);
        Assert.Equal(AboutViewModel.ProjectAddress, about.ProjectUrl.ToString());
    }

    [Fact]
    public void TheVersionIsReportedWithoutBuildMetadata()
    {
        var about = new AboutViewModel();

        Assert.NotEmpty(about.Version);
        Assert.DoesNotContain('+', about.Version);
        Assert.StartsWith("0.", about.Version, StringComparison.Ordinal);
    }

    [Fact]
    public void TheApplicationIsNamedAndItsLicenceIsTitled()
    {
        var about = new AboutViewModel();

        Assert.Equal("Playlist Generator", about.ApplicationName);
        Assert.Equal("MIT License", about.LicenseName);
    }
}
