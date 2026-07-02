namespace ISGPrinter.App.ViewModels;

/// <summary>
/// Broadcast after an action changes printer state (install, set default,
/// remove). The shell reloads both printer grids and the status bar so the UI
/// stays in sync without a manual refresh.
/// </summary>
public sealed class PrintersChangedMessage;
