using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Playnite.SDK;
using UniPlaySong;
using System.Threading;
using System;
using System.Runtime.Serialization.Formatters;
using System.Windows.Input;

namespace UniPlaySong.Monitors
{
    class MediaElementsMonitor
    {
        private static IPlayniteAPI playniteApi;
        private static UniPlaySongSettings settings;
        private static bool _classHandlerRegistered;

        static private DispatcherTimer timer;

        class WeakTargetComparer : IEqualityComparer<WeakReference>
        {
            public bool Equals(WeakReference b1, WeakReference b2)
            {
                return ReferenceEquals(b1.Target, b2.Target);
            }

            public int GetHashCode(WeakReference r)
            {
                var Target = r.Target;
                return Target != null ? r.Target.GetHashCode() : 0;
            }
        }
        readonly static private Dictionary<WeakReference, TimeSpan> mediaElementPositions = new Dictionary<WeakReference, TimeSpan>(new WeakTargetComparer());

        private static readonly ILogger Logger = LogManager.GetLogger();

        static public void Attach(IPlayniteAPI api, UniPlaySongSettings settings)
        {
            playniteApi = api;
            MediaElementsMonitor.settings = settings;

            // RegisterClassHandler is permanent (no unregister API) — only call once
            if (!_classHandlerRegistered)
            {
                EventManager.RegisterClassHandler(typeof(MediaElement), MediaElement.MediaOpenedEvent, new RoutedEventHandler(MediaElement_Opened));
                _classHandlerRegistered = true;
            }

            // Reuse existing timer to prevent orphaned timers on repeated Attach() calls
            if (timer == null)
            {
                timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(100);
                timer.Tick += Timer_Tick;
            }
        }

        /// <summary>
        /// Updates the settings reference to the current instance.
        /// Called when Playnite reloads settings (e.g., after saving any plugin's settings).
        /// Without this, the monitor writes VideoIsPlaying to a stale/dead settings object.
        /// </summary>
        static public void UpdateSettings(UniPlaySongSettings newSettings)
        {
            if (newSettings != null)
            {
                settings = newSettings;
            }
        }

        public static List<MediaElement> GetAllMediaElements(DependencyObject parent)
        {
            var list = new List<MediaElement>();
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is MediaElement mediaElement
                    && mediaElement.NaturalVideoWidth != 0
                    && mediaElement.NaturalVideoHeight != 0
                    && mediaElement.HasAudio
                    && mediaElement.IsVisible
                    && (mediaElement.LoadedBehavior == MediaState.Manual || mediaElement.Position < mediaElement.NaturalDuration)
                )
                {
                    list.Add(mediaElement);
                }
                list.AddRange(GetAllMediaElements(child));
            }
            return list;
        }
        static private void Timer_Tick(object sender, EventArgs e)
        {
            bool someIsPlaying = false;

            List<MediaElement> mediaElements = new List<MediaElement>();
            foreach (Window w in Application.Current.Windows)
            {
                mediaElements.AddRange(GetAllMediaElements(w));
            }

            foreach (var mediaElement in mediaElements)
            {
                if (! mediaElementPositions.ContainsKey(new WeakReference(mediaElement)))
                    mediaElementPositions[new WeakReference(mediaElement)] = mediaElement.Position;
            }

            List<MediaElement> playing = new List<MediaElement>();
            var keysToRemove = new List<WeakReference>();

            var keys = new List<WeakReference>();
            keys.AddRange(mediaElementPositions.Keys);
            foreach (var mediaElementReference in keys)
            {
                if (!(mediaElementReference.Target is MediaElement mediaElement))
                {
                    // MediaElement has been collected, remove it from the dictionary
                    keysToRemove.Add(mediaElementReference);
                }
                else if (mediaElementPositions[mediaElementReference] != mediaElement.Position)
                {
                    // Position has changed, update the dictionary
                    mediaElementPositions[mediaElementReference] = mediaElement.Position;
                    
                    bool isPlaying = mediaElement.HasAudio && !mediaElement.IsMuted && mediaElement.Volume > 0;
                    if (isPlaying)
                    {
                        someIsPlaying = true;
                        playing.Add(mediaElement);
                    }
                }
                else if(!mediaElement.IsVisible || mediaElement.LoadedBehavior!=MediaState.Manual && mediaElement.Position>=mediaElement.NaturalDuration)
                {
                    keysToRemove.Add(mediaElementReference);
                }
            }

            if (settings.VideoIsPlaying != someIsPlaying)
            {
                settings.VideoIsPlaying = someIsPlaying;
            }

            foreach (var key in keysToRemove)
            {
                mediaElementPositions.Remove(key);
            }

            if (mediaElementPositions.Count == 0)
            {
                timer.Stop();
                settings.VideoIsPlaying = false;
            }
        }

        static private void MediaElement_Opened(object sender, RoutedEventArgs e)
        {
            Timer_Tick(sender, e);
            // Always start timer when a MediaElement is detected (matches PlayniteSound pattern)
            timer.Start();
        }
    }
}
