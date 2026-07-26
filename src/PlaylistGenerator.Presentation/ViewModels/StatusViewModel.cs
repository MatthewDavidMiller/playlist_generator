using CommunityToolkit.Mvvm.ComponentModel;

namespace PlaylistGenerator.Presentation.ViewModels;

/// <summary>
/// The shared status line and collapsible diagnostic detail.
/// </summary>
/// <remarks>
/// Both tabs report through one instance so the window always shows a single, current
/// message rather than competing per-tab status text.
/// </remarks>
public sealed partial class StatusViewModel : ObservableObject
{
    /// <summary>Message shown when no operation has run yet.</summary>
    public const string ReadyMessage = "Ready";

    [ObservableProperty]
    private string _message = ReadyMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorDetails))]
    private string _errorDetails = string.Empty;

    /// <summary>Gets whether diagnostic detail is available to expand.</summary>
    public bool HasErrorDetails => !string.IsNullOrWhiteSpace(ErrorDetails);

    /// <summary>Reports progress, leaving any existing diagnostics in place.</summary>
    public void Report(string message) => Message = message;

    /// <summary>Announces a new operation and clears diagnostics from the previous one.</summary>
    public void BeginOperation(string message)
    {
        ErrorDetails = string.Empty;
        Message = message;
    }

    /// <summary>
    /// Reports a failure, keeping the short cause on the status line and the full detail,
    /// including the stack trace, behind the expander.
    /// </summary>
    public void ReportFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Message = $"Error: {exception.Message}";
        ErrorDetails = exception.ToString();
    }
}
