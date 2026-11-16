using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace UniPlaySong.Controls.Settings
{
    // Builds a settings page the first time its tab is shown, then keeps it, instead of on every settings open. Used for
    // the About pages only, which are static info; the settings pages stay built up front. Anything that must happen
    // when settings open belongs in the view model's BeginEdit, never in a lazy page: an unvisited page runs no code.
    //
    // "Shown" is selected AND visible: each group's inner tab strip selects its first tab even while the group itself
    // is hidden, so selection alone would build the page anyway.
    public static class LazyTab
    {
        public static readonly DependencyProperty PageProperty = DependencyProperty.RegisterAttached(
            "Page", typeof(Type), typeof(LazyTab), new PropertyMetadata(null, OnPageChanged));

        public static void SetPage(TabItem item, Type value) => item.SetValue(PageProperty, value);
        public static Type GetPage(TabItem item) => (Type)item.GetValue(PageProperty);

        private static void OnPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TabItem item)) return;
            item.AddHandler(Selector.SelectedEvent, new RoutedEventHandler((s, a) =>
            {
                if (a.OriginalSource == item) CreateIfShown(item);
            }));
            item.IsVisibleChanged += (s, a) => CreateIfShown(item);
            CreateIfShown(item);
        }

        private static void CreateIfShown(TabItem item)
        {
            if (item.Content != null || !item.IsSelected || !item.IsVisible) return;
            var type = GetPage(item);
            if (type != null) item.Content = Activator.CreateInstance(type);
        }
    }
}
