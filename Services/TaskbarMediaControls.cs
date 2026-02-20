using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Adds Previous/Play-Pause/Next thumbnail buttons to Playnite's taskbar preview pane.
    // Uses WPF's built-in TaskbarItemInfo (wraps ITaskbarList3). Desktop mode only.
    public class TaskbarMediaControls
    {
        private const int IconSize = 32; // Render at 32x32 for crisp display on high-DPI

        private TaskbarItemInfo _taskbarInfo;
        private ThumbButtonInfo _prevButton;
        private ThumbButtonInfo _playPauseButton;
        private ThumbButtonInfo _nextButton;
        private bool _isAttached;
        private readonly FileLogger _fileLogger;

        // Cache icons to avoid re-rendering on every state change
        private static ImageSource _playIcon;
        private static ImageSource _pauseIcon;
        private static ImageSource _nextIcon;
        private static ImageSource _prevIcon;

        public event Action PlayPauseClicked;
        public event Action NextClicked;
        public event Action PreviousClicked;

        public TaskbarMediaControls(FileLogger fileLogger = null)
        {
            _fileLogger = fileLogger;
        }

        public void Attach(bool isCurrentlyPlaying)
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null) return;

            // Don't overwrite if Playnite (or another plugin) already set TaskbarItemInfo
            if (mainWindow.TaskbarItemInfo != null)
            {
                _fileLogger?.Debug("TaskbarMediaControls: TaskbarItemInfo already set, skipping");
                return;
            }

            EnsureIcons();

            _prevButton = new ThumbButtonInfo
            {
                Description = "Previous",
                ImageSource = _prevIcon,
                DismissWhenClicked = false
            };
            _prevButton.Click += (s, e) => PreviousClicked?.Invoke();

            _playPauseButton = new ThumbButtonInfo
            {
                Description = isCurrentlyPlaying ? "Pause" : "Play",
                ImageSource = isCurrentlyPlaying ? _pauseIcon : _playIcon,
                DismissWhenClicked = false
            };
            _playPauseButton.Click += (s, e) => PlayPauseClicked?.Invoke();

            _nextButton = new ThumbButtonInfo
            {
                Description = "Next",
                ImageSource = _nextIcon,
                DismissWhenClicked = false
            };
            _nextButton.Click += (s, e) => NextClicked?.Invoke();

            _taskbarInfo = new TaskbarItemInfo();
            _taskbarInfo.ThumbButtonInfos.Add(_prevButton);
            _taskbarInfo.ThumbButtonInfos.Add(_playPauseButton);
            _taskbarInfo.ThumbButtonInfos.Add(_nextButton);

            mainWindow.TaskbarItemInfo = _taskbarInfo;
            _isAttached = true;

            _fileLogger?.Debug("TaskbarMediaControls: Attached thumbnail buttons");
        }

        public void Detach()
        {
            if (!_isAttached) return;

            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow?.TaskbarItemInfo == _taskbarInfo)
            {
                mainWindow.TaskbarItemInfo = null;
            }
            _isAttached = false;
            _fileLogger?.Debug("TaskbarMediaControls: Detached");
        }

        // Call when playback state changes to update the play/pause icon
        public void UpdatePlaybackState(bool isPlaying)
        {
            if (_playPauseButton == null) return;

            _playPauseButton.ImageSource = isPlaying ? _pauseIcon : _playIcon;
            _playPauseButton.Description = isPlaying ? "Pause" : "Play";
        }

        #region Icon Generation (32x32 vector-rendered to bitmap)

        private static void EnsureIcons()
        {
            if (_playIcon != null) return;
            _playIcon = CreatePlayIcon();
            _pauseIcon = CreatePauseIcon();
            _nextIcon = CreateNextIcon();
            _prevIcon = CreatePreviousIcon();
        }

        private static ImageSource CreatePlayIcon()
        {
            return RenderIcon(dc =>
            {
                var triangle = new StreamGeometry();
                using (var ctx = triangle.Open())
                {
                    ctx.BeginFigure(new Point(9, 4), true, true);
                    ctx.LineTo(new Point(26, 16), true, false);
                    ctx.LineTo(new Point(9, 28), true, false);
                }
                triangle.Freeze();
                dc.DrawGeometry(Brushes.White, null, triangle);
            });
        }

        private static ImageSource CreatePauseIcon()
        {
            return RenderIcon(dc =>
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(7, 4, 6, 24));
                dc.DrawRectangle(Brushes.White, null, new Rect(19, 4, 6, 24));
            });
        }

        private static ImageSource CreateNextIcon()
        {
            return RenderIcon(dc =>
            {
                var triangle = new StreamGeometry();
                using (var ctx = triangle.Open())
                {
                    ctx.BeginFigure(new Point(4, 4), true, true);
                    ctx.LineTo(new Point(20, 16), true, false);
                    ctx.LineTo(new Point(4, 28), true, false);
                }
                triangle.Freeze();
                dc.DrawGeometry(Brushes.White, null, triangle);
                dc.DrawRectangle(Brushes.White, null, new Rect(22, 4, 5, 24));
            });
        }

        private static ImageSource CreatePreviousIcon()
        {
            return RenderIcon(dc =>
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(5, 4, 5, 24));
                var triangle = new StreamGeometry();
                using (var ctx = triangle.Open())
                {
                    ctx.BeginFigure(new Point(28, 4), true, true);
                    ctx.LineTo(new Point(12, 16), true, false);
                    ctx.LineTo(new Point(28, 28), true, false);
                }
                triangle.Freeze();
                dc.DrawGeometry(Brushes.White, null, triangle);
            });
        }

        private static RenderTargetBitmap RenderIcon(Action<DrawingContext> draw)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                draw(dc);
            }
            var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        #endregion
    }
}
