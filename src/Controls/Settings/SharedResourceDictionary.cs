using System;
using System.Collections.Generic;
using System.Windows;

namespace UniPlaySong.Controls.Settings
{
    // A ResourceDictionary that loads each Source once and merges that one instance everywhere. A plain
    // <ResourceDictionary Source=...> parses the file again for every owner, and all 41 settings pages merge
    // SettingsResources.xaml - opening settings parsed it 42 times. UI thread only, like all WPF resources.
    public class SharedResourceDictionary : ResourceDictionary
    {
        private static readonly Dictionary<Uri, ResourceDictionary> Loaded = new Dictionary<Uri, ResourceDictionary>();

        private Uri _source;

        // Hides ResourceDictionary.Source, so XAML sets this one and the base never loads the file itself.
        public new Uri Source
        {
            get => _source;
            set
            {
                _source = value;
                if (!Loaded.TryGetValue(value, out var shared))
                {
                    shared = new ResourceDictionary { Source = value };
                    Loaded[value] = shared;
                }
                MergedDictionaries.Add(shared);
            }
        }
    }
}
