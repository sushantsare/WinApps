namespace AppStore.Core.Services;

/// <summary>
/// Runs work on the UI thread. The app sets <see cref="Post"/> to its DispatcherQueue at start-up;
/// tests leave the default (run immediately).
/// </summary>
public static class Ui
{
    public static Action<Action> Post { get; set; } = a => a();
}

/// <summary>What the view-models need from the window: dialogs and file pickers.</summary>
public interface IDialogs
{
    /// <param name="warnings">Lines shown in a yellow warning box (e.g. apps that will be closed).</param>
    /// <param name="info">Line shown in a blue info box (e.g. the one-admin-popup note), or null.</param>
    Task<bool> ConfirmAsync(string title, string body, string primary, IReadOnlyList<string>? warnings = null, string? info = null, bool destructive = false);

    Task<Models.PackageOptions?> EditOptionsAsync(Models.PackageItem app, Models.PackageOptions current, IReadOnlyList<string> versions);

    /// <summary>Lets the user tick apps from an imported list; returns the chosen ones or null when cancelled.</summary>
    Task<IReadOnlyList<Models.AppListEntry>?> ChooseImportAsync(Models.AppListFile file, ISet<string> alreadyInstalled);

    Task<string?> PickSaveFileAsync(string suggestedName, string extension, string typeLabel);

    Task<string?> PickOpenFileAsync(string extension, string typeLabel);

    Task ShowMessageAsync(string title, string body);

    void Toast(string message, bool isError = false);

    void OpenUri(string uri);
}

/// <summary>Runs a batch with one admin popup (the app starts itself elevated). Implemented in the app.</summary>
public interface IElevation
{
    bool IsElevated { get; }

    /// <summary>Runs the requests in an elevated copy of App Store and reports each <see cref="Models.BatchEvent"/>.
    /// Returns false when Windows' popup was declined (nothing ran).</summary>
    Task<bool> RunBatchAsync(IReadOnlyList<Models.OperationRequest> requests, Action<Models.BatchEvent> onEvent, CancellationToken ct);

    /// <summary>Asks the running elevated batch to stop one item (index in the batch) or everything (-1).</summary>
    void Cancel(int index);

    Task<Models.OpResult?> RunElevatedTaskAsync(string task, CancellationToken ct);
}
