using System.Windows.Media;

namespace AddonManager.App.ViewModels;

/// <summary>
/// What the view models need from Windows: <c>Confirm(caption, text)</c> asks Yes or No, <c>LoadImage</c> loads a thumbnail (or <c>null</c>),
/// <c>PickImage</c> opens the open-file dialog for an image (<c>null</c> on cancel), and <c>OpenLink</c> opens a link in the browser (it may throw).
/// </summary>
public sealed record DialogServices(
    Func<string, string, bool> Confirm,
    Func<string?, Task<ImageSource?>> LoadImage,
    Func<string?> PickImage,
    Action<string> OpenLink);
