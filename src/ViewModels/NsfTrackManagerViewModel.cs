using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using UniPlaySong.Audio;
using UniPlaySong.Common;
using UniPlaySong.Models;

namespace UniPlaySong.ViewModels
{
    public sealed class NsfTrackRow : INotifyPropertyChanged
    {
        private bool _isKept = true;
        private bool _isPreviewing;

        public int Index { get; set; }              // 0-based
        public int DisplayNumber { get; set; }      // 1-based for UI
        public string Name { get; set; }
        public int DurationMs { get; set; }
        public string DurationDisplay
        {
            get
            {
                // GME returns 150000 (2:30) as its default when the NSF has no embedded
                // per-track length metadata. Treat that sentinel as "unknown" rather
                // than displaying a misleading 2:30 for every track.
                if (DurationMs <= 0 || DurationMs == 150000) return "—";
                var ts = TimeSpan.FromMilliseconds(DurationMs);
                return ts.Minutes + ":" + ts.Seconds.ToString("00");
            }
        }

        public bool IsKept
        {
            get { return _isKept; }
            set { _isKept = value; OnPropertyChanged(); }
        }

        public bool IsPreviewing
        {
            get { return _isPreviewing; }
            set { _isPreviewing = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class NsfTrackManagerViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly string _nsfPath;
        private readonly string _gameMusicDir;
        private readonly string _originalBaseName;
        private readonly byte[] _originalBytes;
        private GmePreviewPlayer _previewPlayer;
        private NsfTrackRow _currentPreview;
        private bool _isCommitting;
        private bool _disposed;
        private bool _preserveOriginal = true;
        private NsfManagerTab _activeTab = NsfManagerTab.SplitTracks;
        private NsfLoopRow _currentLoopPreview;

        public bool PreserveOriginal
        {
            get { return _preserveOriginal; }
            set { _preserveOriginal = value; OnPropertyChanged(); }
        }

        public string GameName { get; set; }
        public string FileName { get; set; }
        public ObservableCollection<NsfTrackRow> Tracks { get; }

        public string SelectionSummary
        {
            get
            {
                int kept = Tracks.Count(t => t.IsKept);
                return kept + " of " + Tracks.Count + " selected";
            }
        }

        public bool IsCommitting
        {
            get { return _isCommitting; }
            private set { _isCommitting = value; OnPropertyChanged(); OnPropertyChanged("CanCommit"); }
        }

        public bool CanCommit
        {
            get { return !_isCommitting && Tracks.Any(t => t.IsKept); }
        }

        public ObservableCollection<NsfLoopRow> LoopRows { get; }

        public NsfManagerTab ActiveTab
        {
            get { return _activeTab; }
            set { _activeTab = value; OnPropertyChanged(); }
        }

        public bool SplitTabEnabled { get; private set; }
        public bool EditLoopsTabEnabled { get; private set; }
        public string SplitTabTooltip { get; private set; }
        public string EditLoopsTabTooltip { get; private set; }

        public string LoopsSummary
        {
            get
            {
                int total = LoopRows.Count;
                int customCount = 0;
                foreach (var r in LoopRows)
                    if (r.HasOverride) customCount++;
                return total + " tracks · " + customCount + " have custom loops";
            }
        }

        public bool CanSaveLoops
        {
            get
            {
                if (LoopRows.Count == 0) return false;
                foreach (var r in LoopRows)
                    if (!r.IsValid) return false;
                return true;
            }
        }

        // Bound to window. Action invoked to close dialog with result.
        public Action<bool> CloseRequested;

        public System.Windows.Input.ICommand SelectAllCommand { get; }
        public System.Windows.Input.ICommand SelectNoneCommand { get; }
        public System.Windows.Input.ICommand InvertCommand { get; }
        public System.Windows.Input.ICommand TogglePreviewCommand { get; }
        public System.Windows.Input.ICommand CommitCommand { get; }
        public System.Windows.Input.ICommand CancelCommand { get; }
        public System.Windows.Input.ICommand ToggleLoopPreviewCommand { get; }
        public System.Windows.Input.ICommand SaveLoopsCommand { get; }
        public System.Windows.Input.ICommand StepLoopUpCommand { get; }
        public System.Windows.Input.ICommand StepLoopDownCommand { get; }

        // masterNsfPath may be null when the folder has only mini-NSFs (Edit-Loops-only dialog).
        // miniNsfPaths is the list of mini-NSFs in the game folder; empty when only a master exists.
        public NsfTrackManagerViewModel(string masterNsfPath, List<string> miniNsfPaths, string gameFolder, string gameName)
        {
            GameName = gameName;
            _gameMusicDir = gameFolder;
            Tracks = new ObservableCollection<NsfTrackRow>();
            LoopRows = new ObservableCollection<NsfLoopRow>();

            // Classify tab enablement from inputs.
            SplitTabEnabled = !string.IsNullOrEmpty(masterNsfPath);
            EditLoopsTabEnabled = miniNsfPaths != null && miniNsfPaths.Count > 0;

            SplitTabTooltip = SplitTabEnabled
                ? string.Empty
                : "No splittable multi-track NSF in this game's folder.";
            EditLoopsTabTooltip = EditLoopsTabEnabled
                ? string.Empty
                : "No .nsf files in this game's folder.";

            // Initialize Split Tracks state only when a master exists.
            if (SplitTabEnabled)
            {
                _nsfPath = masterNsfPath;
                _originalBaseName = Path.GetFileNameWithoutExtension(masterNsfPath);
                FileName = Path.GetFileName(masterNsfPath);

                _originalBytes = File.ReadAllBytes(masterNsfPath);
                if (!NsfHeaderPatcher.IsValidNsfHeader(_originalBytes))
                    throw new InvalidDataException("File is not a valid NSF.");

                LoadTrackMetadata();
                foreach (var t in Tracks) t.PropertyChanged += OnTrackPropertyChanged;
            }

            // Initialize Edit Loops rows when mini-NSFs exist.
            if (EditLoopsTabEnabled)
            {
                LoadLoopRows(miniNsfPaths);
                foreach (var r in LoopRows) r.PropertyChanged += OnLoopRowPropertyChanged;
            }

            // Default tab: prefer Split Tracks when both are enabled (most common
            // first-time flow). Otherwise pick whichever is enabled.
            if (SplitTabEnabled) ActiveTab = NsfManagerTab.SplitTracks;
            else if (EditLoopsTabEnabled) ActiveTab = NsfManagerTab.EditLoops;

            SelectAllCommand = new RelayCommand(() => SetAllKept(true));
            SelectNoneCommand = new RelayCommand(() => SetAllKept(false));
            InvertCommand = new RelayCommand(InvertSelection);
            TogglePreviewCommand = new RelayCommand<NsfTrackRow>(TogglePreview);
            CommitCommand = new RelayCommand(Commit, () => CanCommit);
            CancelCommand = new RelayCommand(() =>
            {
                StopPreview();
                var h = CloseRequested; if (h != null) h(false);
            });
            ToggleLoopPreviewCommand = new RelayCommand<NsfLoopRow>(ToggleLoopPreview);
            SaveLoopsCommand = new RelayCommand(SaveLoops, () => CanSaveLoops);
            StepLoopUpCommand = new RelayCommand<NsfLoopRow>(row => StepLoop(row, +LoopStepSeconds));
            StepLoopDownCommand = new RelayCommand<NsfLoopRow>(row => StepLoop(row, -LoopStepSeconds));
        }

        private const int LoopStepSeconds = 5;
        private const int DefaultLoopStartSeconds = 30;

        // Adjusts the row's loop seconds by delta, clamped to [MinLoopSeconds, MaxLoopSeconds].
        // Empty input starts at DefaultLoopStartSeconds before the first step is applied,
        // so the first click on either button produces a sane value.
        private void StepLoop(NsfLoopRow row, int delta)
        {
            if (row == null) return;

            int current;
            if (string.IsNullOrWhiteSpace(row.LoopSecondsInput))
            {
                current = DefaultLoopStartSeconds;
            }
            else if (!int.TryParse(row.LoopSecondsInput.Trim(), out current))
            {
                // If the field has invalid text, reset to the default rather than
                // propagating garbage through arithmetic.
                current = DefaultLoopStartSeconds;
            }

            int next = current + delta;
            if (next < NsfLoopRow.MinLoopSeconds) next = NsfLoopRow.MinLoopSeconds;
            if (next > NsfLoopRow.MaxLoopSeconds) next = NsfLoopRow.MaxLoopSeconds;

            row.LoopSecondsInput = next.ToString();
        }

        private void LoadTrackMetadata()
        {
            IntPtr emu;
            var err = GmeNative.gme_open_file(_nsfPath, out emu, 44100);
            var msg = GmeNative.GetError(err);
            if (msg != null) throw new InvalidOperationException("gme_open_file: " + msg);

            try
            {
                int count = GmeNative.gme_track_count(emu);
                for (int i = 0; i < count; i++)
                {
                    IntPtr infoPtr;
                    var iErr = GmeNative.gme_track_info(emu, out infoPtr, i);
                    var iMsg = GmeNative.GetError(iErr);
                    string songName = string.Empty;
                    int playLen = 0;
                    if (iMsg == null && infoPtr != IntPtr.Zero)
                    {
                        songName = GmeNative.GetInfoString(infoPtr, GmeNative.InfoSong);
                        playLen = GmeNative.GetPlayLength(infoPtr);
                        GmeNative.gme_free_info(infoPtr);
                    }

                    string displayName = string.IsNullOrWhiteSpace(songName)
                        ? _originalBaseName + " - Track " + (i + 1).ToString("00")
                        : songName;

                    Tracks.Add(new NsfTrackRow
                    {
                        Index = i,
                        DisplayNumber = i + 1,
                        Name = displayName,
                        DurationMs = playLen,
                        IsKept = true
                    });
                }
            }
            finally
            {
                GmeNative.gme_delete(emu);
            }
        }

        private void LoadLoopRows(List<string> miniNsfPaths)
        {
            int i = 1;
            foreach (var path in miniNsfPaths)
            {
                var row = new NsfLoopRow
                {
                    DisplayNumber = i++,
                    FileName = Path.GetFileName(path),
                    FilePath = path
                };

                // Seed from existing manifest.
                int? existingMs = NsfLoopManifest.ReadMillisecondsFor(path);
                if (existingMs.HasValue)
                    row.LoopSecondsInput = (existingMs.Value / 1000).ToString();

                LoopRows.Add(row);
            }
        }

        private void OnTrackPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsKept")
            {
                OnPropertyChanged("SelectionSummary");
                OnPropertyChanged("CanCommit");
            }
        }

        private void SetAllKept(bool kept)
        {
            foreach (var t in Tracks) t.IsKept = kept;
        }

        private void InvertSelection()
        {
            foreach (var t in Tracks) t.IsKept = !t.IsKept;
        }

        private void TogglePreview(NsfTrackRow row)
        {
            if (row == null) return;

            if (_currentPreview == row && row.IsPreviewing)
            {
                StopPreview();
                return;
            }

            StopPreview();

            try
            {
                if (_previewPlayer == null)
                {
                    _previewPlayer = new GmePreviewPlayer();
                    _previewPlayer.TrackEnded += OnPreviewEnded;
                }

                // Re-load the master NSF every time — the user may have previewed a
                // mini-NSF in the Edit Loops tab, which replaced _previewPlayer's file.
                _previewPlayer.Load(_nsfPath);
                _previewPlayer.Play(row.Index, Constants.NsfPreviewMaxSeconds);
                row.IsPreviewing = true;
                _currentPreview = row;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Preview failed: " + ex.Message, "NSF Track Manager",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnPreviewEnded(object sender, EventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                StopPreview();
                StopLoopPreview();
            }));
        }

        private void StopPreview()
        {
            if (_currentPreview != null)
            {
                _currentPreview.IsPreviewing = false;
                _currentPreview = null;
            }
            if (_previewPlayer != null) _previewPlayer.Stop();
        }

        private void ToggleLoopPreview(NsfLoopRow row)
        {
            if (row == null) return;

            if (_currentLoopPreview == row && row.IsPreviewing)
            {
                StopLoopPreview();
                return;
            }

            StopLoopPreview();
            StopPreview(); // also stop any Split Tracks preview

            try
            {
                if (_previewPlayer == null)
                {
                    _previewPlayer = new GmePreviewPlayer();
                    _previewPlayer.TrackEnded += OnPreviewEnded;
                }

                _previewPlayer.Load(row.FilePath);
                // Mini-NSF's GME track index is always 0 after the header patch.
                _previewPlayer.Play(0, Constants.NsfPreviewMaxSeconds);
                row.IsPreviewing = true;
                _currentLoopPreview = row;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Preview failed: " + ex.Message, "NSF Track Manager",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void StopLoopPreview()
        {
            if (_currentLoopPreview != null)
            {
                _currentLoopPreview.IsPreviewing = false;
                _currentLoopPreview = null;
            }
            if (_previewPlayer != null) _previewPlayer.Stop();
        }

        private void OnLoopRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NsfLoopRow.LoopSecondsInput)
                || e.PropertyName == nameof(NsfLoopRow.HasOverride)
                || e.PropertyName == nameof(NsfLoopRow.IsValid))
            {
                OnPropertyChanged(nameof(LoopsSummary));
                OnPropertyChanged(nameof(CanSaveLoops));
            }
        }

        private void SaveLoops()
        {
            try
            {
                var overrides = new Dictionary<string, int>();
                foreach (var row in LoopRows)
                {
                    if (row.HasOverride)
                        overrides[row.FileName] = row.LoopSecondsValue;
                }

                NsfLoopManifest.Save(_gameMusicDir, overrides);

                var h = CloseRequested; if (h != null) h(true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Save failed: " + ex.Message, "NSF Track Manager",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void Commit()
        {
            StopPreview();
            IsCommitting = true;
            var writtenPaths = new System.Collections.Generic.List<string>();
            string preservedPath = null;
            try
            {
                var kept = Tracks.Where(t => t.IsKept).ToList();
                foreach (var row in kept)
                {
                    var patched = NsfHeaderPatcher.PatchForTrack(_originalBytes, row.Index);
                    var safe = SanitizeFileName(row.Name);
                    var target = ResolveTargetPath(safe);
                    File.WriteAllBytes(target, patched);
                    writtenPaths.Add(target);
                }

                if (writtenPaths.Count > 0)
                {
                    if (_preserveOriginal)
                        preservedPath = PreserveOriginalFile();
                    File.Delete(_nsfPath);
                }

                var h = CloseRequested; if (h != null) h(true);
            }
            catch (Exception ex)
            {
                // Best-effort rollback: mini-NSFs written this run + any preservation copy.
                foreach (var p in writtenPaths)
                {
                    try { File.Delete(p); } catch { /* ignore — rollback is best-effort */ }
                }
                if (preservedPath != null)
                {
                    try { File.Delete(preservedPath); } catch { /* ignore */ }
                }

                System.Windows.MessageBox.Show("Commit failed: " + ex.Message, "NSF Track Manager",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                IsCommitting = false;
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        // Copies the original NSF to <parentDir>/PreservedOriginals/<gameFolderName>/<filename>
        // matching the convention used by AudioAmplifyService/AudioTrimService.
        private string PreserveOriginalFile()
        {
            var parentDir = Directory.GetParent(_gameMusicDir)?.FullName ?? _gameMusicDir;
            var preservedOriginalsDir = Path.Combine(parentDir, Constants.PreservedOriginalsFolderName);
            var gameFolderName = Path.GetFileName(_gameMusicDir);
            var gamePreservedDir = Path.Combine(preservedOriginalsDir, gameFolderName);
            Directory.CreateDirectory(gamePreservedDir);

            var target = Path.Combine(gamePreservedDir, Path.GetFileName(_nsfPath));
            if (File.Exists(target))
            {
                var baseName = Path.GetFileNameWithoutExtension(_nsfPath);
                var ext = Path.GetExtension(_nsfPath);
                for (int n = 2; n < 1000; n++)
                {
                    var alt = Path.Combine(gamePreservedDir, baseName + " (" + n + ")" + ext);
                    if (!File.Exists(alt)) { target = alt; break; }
                }
            }

            File.Copy(_nsfPath, target, false);
            return target;
        }

        private string ResolveTargetPath(string baseName)
        {
            var target = Path.Combine(_gameMusicDir, baseName + ".nsf");
            if (!File.Exists(target)) return target;
            for (int n = 2; n < 1000; n++)
            {
                target = Path.Combine(_gameMusicDir, baseName + " (" + n + ").nsf");
                if (!File.Exists(target)) return target;
            }
            throw new IOException("Unable to find available filename for " + baseName);
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var s = new string((name ?? "Unknown")
                .Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim();
            if (s.Length > 100) s = s.Substring(0, 100).TrimEnd();
            return string.IsNullOrWhiteSpace(s) ? "Unknown" : s;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopPreview();
            StopLoopPreview();

            foreach (var r in LoopRows) r.PropertyChanged -= OnLoopRowPropertyChanged;

            if (_previewPlayer != null)
            {
                _previewPlayer.TrackEnded -= OnPreviewEnded;
                _previewPlayer.Dispose();
                _previewPlayer = null;
            }
        }
    }
}
