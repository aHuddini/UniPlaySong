using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.IconGlow
{
    // Finds PART_ImageIcon in Playnite's Desktop visual tree.
    // Targets the game overview panel icon (next to the game title in the details/grid panel),
    // NOT the list item icons which may be hidden or belong to non-selected games.
    public static class TileFinder
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Finds the PART_ImageIcon Image control in the game overview panel.
        // First looks for PART_ControlGameView (the overview panel), then searches within it.
        // Falls back to finding any visible PART_ImageIcon if the overview panel isn't found.
        public static Image FindSelectedGameIcon(DependencyObject root, FileLogger fileLogger = null)
        {
            if (root == null) return null;

            try
            {
                // Strategy 1: Find PART_ControlGameView (game overview panel), then find icon within it
                var gameView = FindChildByName<FrameworkElement>(root, "PART_ControlGameView");
                if (gameView != null)
                {
                    fileLogger?.Debug($"[IconGlow] Found PART_ControlGameView: {gameView.GetType().Name} ({gameView.ActualWidth}x{gameView.ActualHeight})");
                    var icon = FindChildByName<Image>(gameView, "PART_ImageIcon");
                    if (icon != null && icon.ActualWidth > 0 && icon.ActualHeight > 0)
                    {
                        fileLogger?.Debug($"[IconGlow] Found PART_ImageIcon in overview panel ({icon.ActualWidth}x{icon.ActualHeight})");
                        return icon;
                    }
                    fileLogger?.Debug($"[IconGlow] PART_ImageIcon in overview panel: {(icon == null ? "not found" : $"found but size {icon.ActualWidth}x{icon.ActualHeight}")}");
                }
                else
                {
                    fileLogger?.Debug("[IconGlow] PART_ControlGameView not found in visual tree");
                }

                // Strategy 2: Find all PART_ImageIcon elements, pick the first visible one with size > 0
                var allIcons = new List<Image>();
                FindAllChildrenByName(root, "PART_ImageIcon", allIcons);
                fileLogger?.Debug($"[IconGlow] Found {allIcons.Count} total PART_ImageIcon elements");

                foreach (var icon in allIcons)
                {
                    if (icon.IsVisible && icon.ActualWidth > 0 && icon.ActualHeight > 0)
                    {
                        fileLogger?.Debug($"[IconGlow] Using fallback icon ({icon.ActualWidth}x{icon.ActualHeight}, parent: {VisualTreeHelper.GetParent(icon)?.GetType().Name})");
                        return icon;
                    }
                }

                fileLogger?.Debug("[IconGlow] No visible PART_ImageIcon found with non-zero size");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[IconGlow] Error finding game icon in visual tree");
                return null;
            }
        }

        // Finds the parent DockPanel of the given element (needed for border injection).
        public static DockPanel FindParentDockPanel(DependencyObject child)
        {
            if (child == null) return null;

            try
            {
                var parent = VisualTreeHelper.GetParent(child);
                while (parent != null)
                {
                    if (parent is DockPanel dockPanel)
                        return dockPanel;
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[IconGlow] Error finding parent DockPanel");
            }
            return null;
        }

        // Walks up the visual tree to find the nearest ancestor of type T.
        public static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                    return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        public static T FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T element && element.Name == name)
                    return element;

                var result = FindChildByName<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static void FindAllChildrenByName(DependencyObject parent, string name, List<Image> results)
        {
            if (parent == null) return;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is Image image && image.Name == name)
                    results.Add(image);

                FindAllChildrenByName(child, name, results);
            }
        }
    }
}
