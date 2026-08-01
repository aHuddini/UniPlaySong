using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Runs the long-running library operations (PlayniteSound migration, bulk delete, music tag
    // scan) behind a progress dialog. Lifted out of UniPlaySong.cs, which only ever passed its
    // PlayniteAPI to this code — it had no other reason to live on the plugin class.
    //
    // The migration and delete flows were near-identical copies differing only in how they mapped
    // their result and worded their summary, so they now share one implementation.
    public class MigrationProgressRunner
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly IPlayniteAPI _api;

        public MigrationProgressRunner(IPlayniteAPI api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        // Runs a migration and reports copied/skipped/failed counts on completion.
        public void RunMigrationWithProgress(
            string title,
            Func<IProgress<MigrationProgress>, CancellationToken, Task<MigrationBatchResult>> migrationTask)
        {
            RunWithProgressDialog(
                title,
                noun: "Migration",
                work: migrationTask,
                toBatchResult: r => r,
                buildSummary: r =>
                    $"Migration Complete!\n\n" +
                    $"Games processed: {r.TotalGames}\n" +
                    $"Files copied: {r.TotalFilesCopied}\n" +
                    $"Files skipped (already exist): {r.TotalFilesSkipped}\n" +
                    $"Failed: {r.FailedGames}");
        }

        // Runs a delete and reports deleted/removed/failed counts on completion.
        public void RunDeleteWithProgress(
            string title,
            Func<IProgress<MigrationProgress>, CancellationToken, Task<PlayniteSoundDeleteResult>> deleteTask)
        {
            RunWithProgressDialog(
                title,
                noun: "Delete",
                work: deleteTask,
                toBatchResult: r => new MigrationBatchResult
                {
                    TotalGames = r.TotalGames,
                    SuccessfulGames = r.GamesProcessed,
                    FailedGames = r.GamesFailed,
                    TotalFilesCopied = r.FilesDeleted, // reused for display
                    WasCancelled = r.WasCancelled
                },
                buildSummary: r =>
                    $"Delete Complete!\n\n" +
                    $"Games processed: {r.GamesProcessed}\n" +
                    $"Files deleted: {r.FilesDeleted}\n" +
                    $"Folders removed: {r.FoldersDeleted}\n" +
                    $"Failed: {r.FilesFailed}");
        }

        // Shared body: show the modal progress dialog, run the work on a background task, marshal
        // progress and completion back to the UI thread. noun supplies the dialog captions
        // ("Migration Complete" / "Delete Error" and so on).
        private void RunWithProgressDialog<TResult>(
            string title,
            string noun,
            Func<IProgress<MigrationProgress>, CancellationToken, Task<TResult>> work,
            Func<TResult, MigrationBatchResult> toBatchResult,
            Func<TResult, string> buildSummary)
        {
            try
            {
                var progressDialog = new Views.MigrationProgressDialog();
                progressDialog.SetTitle(title);

                var window = DialogHelper.CreateFixedDialog(
                    _api,
                    title,
                    progressDialog,
                    width: 550,
                    height: 450);

                DialogHelper.AddFocusReturnHandler(window, _api, $"{noun.ToLowerInvariant()} dialog close");

                Task.Run(async () =>
                {
                    try
                    {
                        var progress = new Progress<MigrationProgress>(p =>
                            OnUi(() => progressDialog.ReportProgress(p)));

                        var result = await work(progress, progressDialog.CancellationToken);
                        var batch = toBatchResult(result);

                        OnUi(() =>
                        {
                            progressDialog.ReportCompletion(batch);
                            if (!batch.WasCancelled)
                                _api.Dialogs.ShowMessage(buildSummary(result), $"{noun} Complete");
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        OnUi(() => _api.Dialogs.ShowMessage($"{noun} was cancelled.", $"{noun} Cancelled"));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error during {noun.ToLowerInvariant()}");
                        OnUi(() => _api.Dialogs.ShowErrorMessage(
                            $"Error during {noun.ToLowerInvariant()}: {ex.Message}", $"{noun} Error"));
                    }
                });

                window.ShowDialog(); // blocks until closed
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error showing {noun.ToLowerInvariant()} progress dialog");
                _api.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", $"{noun} Error");
            }
        }

        // Scans the library for music and tags games accordingly. Uses Playnite's own global
        // progress rather than the migration dialog, so it does not share the body above.
        public void RunTagScanWithProgress(GameMusicTagService tagService)
        {
            try
            {
                var progressOptions = new GlobalProgressOptions("Scanning games for music status...", true)
                {
                    IsIndeterminate = false
                };

                _api.Dialogs.ActivateGlobalProgress((args) =>
                {
                    try
                    {
                        var progress = new Progress<TagScanProgress>(p =>
                        {
                            args.CurrentProgressValue = p.ProcessedCount;
                            args.ProgressMaxValue = p.TotalCount;
                            args.Text = $"Scanning: {p.CurrentGame}\n({p.ProcessedCount}/{p.TotalCount})";
                        });

                        var task = tagService.ScanAndTagAllGamesAsync(progress, args.CancelToken);
                        task.Wait(args.CancelToken);
                        var result = task.Result;

                        if (!args.CancelToken.IsCancellationRequested)
                        {
                            OnUi(() => _api.Dialogs.ShowMessage(
                                $"Tag scan complete!\n\n" +
                                $"Games scanned: {result.TotalGames}\n" +
                                $"With music: {result.GamesWithMusic}\n" +
                                $"Without music: {result.GamesWithoutMusic}\n" +
                                $"Tags updated: {result.GamesModified}",
                                "Scan Complete"));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // User cancelled, do nothing
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error during tag scan");
                        OnUi(() => _api.Dialogs.ShowErrorMessage($"Error during scan: {ex.Message}", "Scan Error"));
                    }
                }, progressOptions);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error showing tag scan progress");
                _api.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "Scan Error");
            }
        }

        private static void OnUi(Action action)
        {
            Application.Current?.Dispatcher?.BeginInvoke(action);
        }
    }
}
