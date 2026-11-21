using System;
using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace UniPlaySong.Features.MusicInfoCard
{
    // A second Music Info Card entry where Playnite keeps its own game actions: right below "Create Desktop
    // Shortcut" in Desktop's right-click menu, and right below Play/Install in Fullscreen's game menu. The plugin
    // API only reaches the Extensions block in both, so the entry is inserted into Playnite's menu as it opens.
    // Any failure leaves Playnite's menu untouched; the entry in the UniPlaySong submenu stays the dependable path.
    // "Play Next in Radio Queue" follows it, while Radio Mode plays game music.
    internal static class MusicInfoCardMenuEntries
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();

        private const string EntryTitle = "Music Info Card";
        private const string RadioEntryTitle = "Play Next in Radio Queue";
        private const string EntryTag = "UPS_MusicInfoCard";

        private static bool _registered;
        private static IPlayniteAPI _api;
        private static Func<Game, bool> _hasMusic;
        private static Action<Game> _show;
        private static Action<Game> _playNextInRadio;
        private static Func<UniPlaySongSettings> _getSettings; // read live: Settings -> UI can hide either entry

        public static void Attach(IPlayniteAPI api, Func<Game, bool> hasMusic, Action<Game> show, bool isFullscreen,
            Func<UniPlaySongSettings> getSettings, Action<Game> playNextInRadio)
        {
            _api = api;
            _hasMusic = hasMusic;
            _show = show;
            _getSettings = getSettings;
            _playNextInRadio = playNextInRadio;

            // Class handlers can't be removed, so register once per process.
            if (_registered) return;
            _registered = true;

            if (isFullscreen)
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
            else
                EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnContextMenuOpened));
        }

        private static void OnContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (!(sender is ContextMenu menu) || menu.GetType().Name != "GameMenu") return;

            // Playnite fills this menu in its own Opened handler, which runs after class handlers. Normal priority
            // runs once it has, and before the menu's first render, so the entry never pops in.
            menu.Dispatcher.BeginInvoke(new Action(() => InsertDesktopEntry(menu)), DispatcherPriority.Normal);
        }

        private static void InsertDesktopEntry(ContextMenu menu)
        {
            try
            {
                var settings = _getSettings?.Invoke();
                bool card = settings?.DesktopMenuMusicInfoCardShortcut != false;
                bool radio = RadioQueueOn(settings);
                if (!card && !radio) return;
                var selected = _api?.MainView?.SelectedGames?.Take(2).ToList();
                if (selected == null || selected.Count != 1) return;
                var game = selected[0];
                if (!_hasMusic(game)) return;
                if (menu.Items.OfType<MenuItem>().Any(i => Equals(i.Tag, EntryTag))) return;

                // Matched by its translated label, so every Playnite language works.
                var shortcutLabel = ResourceProvider.GetString("LOCCreateDesktopShortcut");
                var shortcut = menu.Items.OfType<MenuItem>().FirstOrDefault(i => (i.Header as string) == shortcutLabel);
                if (shortcut == null) return;

                int at = menu.Items.IndexOf(shortcut) + 1;
                if (card)
                    menu.Items.Insert(at++, new MenuItem
                    {
                        Header = EntryTitle,
                        Tag = EntryTag,
                        Icon = DesktopIcon(shortcut.Icon as TextBlock, MusicGlyph),
                        Command = new RelayCommand(() => _show(game))
                    });
                if (radio)
                    menu.Items.Insert(at, new MenuItem
                    {
                        Header = RadioEntryTitle,
                        Tag = EntryTag,
                        Icon = DesktopIcon(shortcut.Icon as TextBlock, NextGlyph),
                        Command = new RelayCommand(() => _playNextInRadio?.Invoke(game))
                    });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not add the Music Info Card entry to the game menu");
            }
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is Window window) || window.GetType().Name != "GameMenuWindow") return;
            var settings = _getSettings?.Invoke();
            bool card = settings?.FullscreenMenuMusicInfoCardGameMenu != false;
            bool radio = RadioQueueOn(settings);
            if (!card && !radio) return;

            try
            {
                var vm = window.DataContext;
                var vmType = vm?.GetType();
                if (!(vmType?.GetProperty("Game")?.GetValue(vm) is Game game) || !_hasMusic(game)) return;
                if (!(vmType.GetProperty("GameItems")?.GetValue(vm) is IList items)) return;

                // GameActionItem(RelayCommandBase command, string title): its Template is then set to a copy of
                // the theme's own button layout carrying a music icon.
                var itemType = items.GetType().GetGenericArguments().FirstOrDefault();
                var ctor = itemType?.GetConstructor(new[] { typeof(RelayCommandBase), typeof(string) });
                var templateProperty = itemType?.GetProperty("Template");
                if (ctor == null || templateProperty == null) return;
                var titleProperty = itemType.GetProperty("Title");
                if (items.Cast<object>().Any(i => titleProperty?.GetValue(i) is string t && (t == EntryTitle || t == RadioEntryTitle))) return;

                var close = vmType.GetMethod("Close", Type.EmptyTypes);
                object Entry(string title, string glyph, Action<Game> run)
                {
                    var command = new RelayCommand(() =>
                    {
                        close?.Invoke(vm, null);
                        // Run once the game menu has gone, so a card takes focus rather than the closing menu.
                        window.Dispatcher.BeginInvoke(new Action(() => run?.Invoke(game)));
                    });
                    var entry = ctor.Invoke(new object[] { command, title });
                    templateProperty.SetValue(entry, FullscreenTemplate(glyph));
                    return entry;
                }

                // Index 0 is always Play or Install.
                int at = Math.Min(1, items.Count);
                if (card) items.Insert(at++, Entry(EntryTitle, MusicGlyph, _show));
                if (radio) items.Insert(at, Entry(RadioEntryTitle, NextGlyph, _playNextInRadio));
                CollectionViewSource.GetDefaultView(items).Refresh();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not add the Music Info Card entry to the Fullscreen game menu");
            }
        }

        // Radio Mode playing game music: Spotify radio has no UPS queue to jump.
        private static bool RadioQueueOn(UniPlaySongSettings s)
            => s?.RadioModeEnabled == true && s.RadioMusicSource != RadioMusicSource.Spotify;

        // IcoFont "ui-next" and (below) "music", verified against Playnite's bundled icofont.ttf.
        private const string NextGlyph = "";
        private const string MusicGlyph = "";

        // Sized and coloured like the theme's own shortcut icon. Bound, not copied: on the first open of a session
        // that icon hasn't resolved its theme brush yet, and a copy came out black on a dark menu. Always IcoFont:
        // the glyph is an IcoFont code, so a theme drawing that icon in another font must not lend its font here.
        private static TextBlock DesktopIcon(TextBlock reference, string glyph)
        {
            var icon = new TextBlock { Text = glyph, FontWeight = FontWeights.Light };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "FontIcoFont");
            if (reference != null)
            {
                icon.SetBinding(TextBlock.FontSizeProperty, new Binding(nameof(TextBlock.FontSize)) { Source = reference });
                icon.SetBinding(TextBlock.FontWeightProperty, new Binding(nameof(TextBlock.FontWeight)) { Source = reference });
                icon.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(TextBlock.Foreground)) { Source = reference });
            }
            else
            {
                icon.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            }
            return icon;
        }

        // The theme's button layout (icon, then title) with our icon. The icon slot's size and spacing are
        // read off the theme's own Extensions button, so the entry lines up with the rest of its menu.
        private static DataTemplate FullscreenTemplate(string glyph)
        {
            TextBlock reference = null;
            if (ResourceProvider.GetResource("GameMenuExtensionsTemplate") is DataTemplate themeTemplate)
                reference = FindIcon(themeTemplate.LoadContent());

            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetValue(TextBlock.TextProperty, glyph);
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "FontIcoFont");
            icon.SetValue(TextBlock.FontSizeProperty, LocalOr(reference, TextBlock.FontSizeProperty, 24.0));
            icon.SetValue(FrameworkElement.WidthProperty, LocalOr(reference, FrameworkElement.WidthProperty, 25.0));
            icon.SetValue(FrameworkElement.MarginProperty, LocalOr(reference, FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0)));
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            if (reference?.ReadLocalValue(TextBlock.ForegroundProperty) is System.Windows.Media.Brush brush)
                icon.SetValue(TextBlock.ForegroundProperty, brush);

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding());
            title.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.AppendChild(icon);
            panel.AppendChild(title);
            return new DataTemplate { VisualTree = panel };
        }

        // The icon is the first TextBlock whose text isn't bound (the title is {Binding}).
        private static TextBlock FindIcon(object node)
        {
            if (node is TextBlock tb && BindingOperations.GetBinding(tb, TextBlock.TextProperty) == null) return tb;
            if (!(node is DependencyObject d)) return null;
            foreach (var child in LogicalTreeHelper.GetChildren(d))
            {
                var found = FindIcon(child);
                if (found != null) return found;
            }
            return null;
        }

        // Only values the theme set on the element itself; styles aren't applied to a template's loose copy.
        private static T LocalOr<T>(DependencyObject element, DependencyProperty property, T fallback)
            => element?.ReadLocalValue(property) is T value ? value : fallback;
    }
}
