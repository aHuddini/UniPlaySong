using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using UniPlaySong.Audio;
using UniPlaySong.Common;

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
                if (DurationMs <= 0) return string.Empty;
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

        // Bound to window. Action invoked to close dialog with result.
        public Action<bool> CloseRequested;

        public System.Windows.Input.ICommand SelectAllCommand { get; }
        public System.Windows.Input.ICommand SelectNoneCommand { get; }
        public System.Windows.Input.ICommand InvertCommand { get; }
        public System.Windows.Input.ICommand TogglePreviewCommand { get; }
        public System.Windows.Input.ICommand CommitCommand { get; }
        public System.Windows.Input.ICommand CancelCommand { get; }

        public NsfTrackManagerViewModel(string nsfPath, string gameName)
        {
            _nsfPath = nsfPath;
            _gameMusicDir = Path.GetDirectoryName(nsfPath);
            _originalBaseName = Path.GetFileNameWithoutExtension(nsfPath);
            GameName = gameName;
            FileName = Path.GetFileName(nsfPath);
            Tracks = new ObservableCollection<NsfTrackRow>();

            _originalBytes = File.ReadAllBytes(nsfPath);
            if (!NsfHeaderPatcher.IsValidNsfHeader(_originalBytes))
                throw new InvalidDataException("File is not a valid NSF.");

            LoadTrackMetadata();

            foreach (var t in Tracks) t.PropertyChanged += OnTrackPropertyChanged;

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
                    _previewPlayer.Load(_nsfPath);
                    _previewPlayer.TrackEnded += OnPreviewEnded;
                }

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
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(StopPreview));
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

        private void Commit()
        {
            StopPreview();
            IsCommitting = true;
            var writtenPaths = new System.Collections.Generic.List<string>();
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

                if (writtenPaths.Count > 0) File.Delete(_nsfPath);

                var h = CloseRequested; if (h != null) h(true);
            }
            catch (Exception ex)
            {
                // Best-effort rollback of any mini-NSFs written this run.
                foreach (var p in writtenPaths)
                {
                    try { File.Delete(p); } catch { /* ignore — rollback is best-effort */ }
                }

                System.Windows.MessageBox.Show("Commit failed: " + ex.Message, "NSF Track Manager",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                IsCommitting = false;
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
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
            if (_previewPlayer != null)
            {
                _previewPlayer.TrackEnded -= OnPreviewEnded;
                _previewPlayer.Dispose();
                _previewPlayer = null;
            }
        }
    }
}
