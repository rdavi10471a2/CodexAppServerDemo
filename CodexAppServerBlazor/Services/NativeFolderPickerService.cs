using System.Windows.Forms;

namespace CodexAppServerBlazor.Services;

public sealed class NativeFolderPickerService
{
    public Task<string?> PickFolderAsync(string? initialPath, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<string?>(cancellationToken);
        }

        TaskCompletionSource<string?> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                using FolderBrowserDialog dialog = new();
                dialog.Description = "Choose Codex working directory";
                dialog.UseDescriptionForTitle = true;
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                {
                    dialog.SelectedPath = initialPath;
                }

                DialogResult result = dialog.ShowDialog();
                source.TrySetResult(result == DialogResult.OK ? dialog.SelectedPath : null);
            }
            catch (Exception ex)
            {
                source.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return source.Task;
    }
}
