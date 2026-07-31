# Hover Glow for Game List Items — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply a lightweight DropShadowEffect glow to whichever game icon the mouse hovers over in Playnite's game list/grid view.

**Architecture:** A new `ListHoverGlowManager` class attaches a `PreviewMouseMove` handler to Playnite's main window. On each move, it hit-tests the visual tree, walks up to find a `ListBoxItem`, walks down to find `PART_ImageIcon`, and applies/removes a `DropShadowEffect` using colors extracted from the icon. Only one icon glows at a time. No SkiaSharp render, no animation timer — just a static WPF effect that appears/disappears on hover.

**Tech Stack:** WPF `VisualTreeHelper`, `DropShadowEffect`, existing `IconColorExtractor`, C# .NET 4.6.2

---

## Design Decisions

1. **Lightweight effect only** — No SkiaSharp bitmap render, no 60fps timer, no Grid wrapper injection. Just a `DropShadowEffect` on the icon `Image` element. This avoids performance issues with virtualized list items being recycled.

2. **Single hover target** — Track the currently-hovered icon. On mouse move to a different item, remove effect from old icon, apply to new one. On mouse leave the list area (or hover non-item space), remove effect.

3. **Separate from IconGlowManager** — The existing `IconGlowManager` handles the selected game's detail-panel icon with full animation. `ListHoverGlowManager` is independent — lighter, simpler, no shared state.

4. **Reuse `IconColorExtractor`** — Extract colors from the hovered icon's `Source` bitmap. Cache is already keyed by game ID; for list items we'll extract from the `ListBoxItem.DataContext` (which is a Playnite `GamesCollectionViewEntry` that has a `Game` property with an `Id`).

5. **Guard against visual tree instability** — List items are virtualized. The hovered icon may be recycled at any time. All operations wrapped in try/catch. If the icon is no longer in the tree, silently clear state.

6. **Gated by `EnableIconGlow` setting** — Reuses the existing master toggle. No new setting needed.

7. **Exclude the selected game's detail icon** — The detail-panel icon already has a full animated glow from `IconGlowManager`. The hover glow should skip it. We detect this by checking if the icon's ancestor is `PART_ControlGameView`.

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `src/IconGlow/ListHoverGlowManager.cs` | **Create** | Mouse tracking, hit-test, effect apply/remove |
| `src/IconGlow/TileFinder.cs` | **Modify** | Add `FindAncestor<T>` and `FindChildByName<T>` as public static (currently private) |
| `src/UniPlaySong.cs` | **Modify** | Instantiate `ListHoverGlowManager`, wire up lifecycle |

---

## Tasks

### Task 1: Make TileFinder helpers public

**Files:**
- Modify: `src/IconGlow/TileFinder.cs:91-122`

Currently `FindChildByName<T>` and `FindAllChildrenByName` are private. We need `FindChildByName<T>` public for `ListHoverGlowManager` to find `PART_ImageIcon` within a `ListBoxItem`. Also add a public `FindAncestor<T>` (currently exists in `DownloadDialogView.xaml.cs` as a private method).

- [ ] **Step 1: Make `FindChildByName<T>` public and add `FindAncestor<T>`**

In `TileFinder.cs`, change `FindChildByName<T>` from `private` to `public`. Add a new public `FindAncestor<T>` method:

```csharp
public static T FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
// (existing body unchanged)

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
```

- [ ] **Step 2: Build to verify no breakage**

Run: `dotnet clean -c Release && dotnet build -c Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/IconGlow/TileFinder.cs
git commit -m "refactor: make TileFinder.FindChildByName public, add FindAncestor helper"
```

---

### Task 2: Create ListHoverGlowManager

**Files:**
- Create: `src/IconGlow/ListHoverGlowManager.cs`

This is the core new file. It:
- Attaches `PreviewMouseMove` + `MouseLeave` on the main window
- Hit-tests to find `ListBoxItem` ancestor
- Finds `PART_ImageIcon` within that item
- Extracts game ID from `DataContext` for color extraction
- Applies/removes `DropShadowEffect`

- [ ] **Step 1: Create `ListHoverGlowManager.cs`**

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.IconGlow
{
    // Applies a lightweight DropShadowEffect glow to the game icon
    // under the mouse cursor in Playnite's game list/grid view.
    // Only one icon glows at a time. No animation, no SkiaSharp — pure WPF.
    public class ListHoverGlowManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly SettingsService _settingsService;
        private readonly IconColorExtractor _colorExtractor;
        private readonly FileLogger _fileLogger;

        private UniPlaySongSettings _settings => _settingsService.Current;

        private Window _mainWindow;
        private Image _hoveredIcon;
        private Effect _savedEffect;

        public ListHoverGlowManager(SettingsService settingsService, IconColorExtractor colorExtractor, FileLogger fileLogger = null)
        {
            _settingsService = settingsService;
            _colorExtractor = colorExtractor;
            _fileLogger = fileLogger;
        }

        public void Attach()
        {
            _mainWindow = Application.Current?.MainWindow;
            if (_mainWindow == null) return;

            _mainWindow.PreviewMouseMove += OnPreviewMouseMove;
            _mainWindow.MouseLeave += OnMouseLeave;
        }

        public void Detach()
        {
            ClearHoverGlow();
            if (_mainWindow != null)
            {
                _mainWindow.PreviewMouseMove -= OnPreviewMouseMove;
                _mainWindow.MouseLeave -= OnMouseLeave;
                _mainWindow = null;
            }
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            ClearHoverGlow();
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_settings.EnableIconGlow)
            {
                ClearHoverGlow();
                return;
            }

            try
            {
                var pos = e.GetPosition(_mainWindow);
                var hitResult = VisualTreeHelper.HitTest(_mainWindow, pos);
                if (hitResult?.VisualHit == null)
                {
                    ClearHoverGlow();
                    return;
                }

                // Walk up to find a ListBoxItem (game list entry)
                var listBoxItem = TileFinder.FindAncestor<ListBoxItem>(hitResult.VisualHit);
                if (listBoxItem == null)
                {
                    ClearHoverGlow();
                    return;
                }

                // Skip if this item is inside the game details panel (already has full animated glow)
                var gameView = TileFinder.FindAncestor<FrameworkElement>(listBoxItem);
                if (IsInsideGameView(listBoxItem))
                {
                    ClearHoverGlow();
                    return;
                }

                // Find PART_ImageIcon within this list item
                var icon = TileFinder.FindChildByName<Image>(listBoxItem, "PART_ImageIcon");
                if (icon == null || icon.ActualWidth <= 0 || icon.ActualHeight <= 0)
                {
                    ClearHoverGlow();
                    return;
                }

                // Same icon as before — nothing to do
                if (icon == _hoveredIcon) return;

                // New icon — clear old, apply new
                ClearHoverGlow();
                ApplyHoverGlow(icon, listBoxItem);
            }
            catch (Exception ex)
            {
                // Visual tree may be in flux during scrolling/recycling
                ClearHoverGlow();
            }
        }

        private void ApplyHoverGlow(Image icon, ListBoxItem listBoxItem)
        {
            try
            {
                // Extract game ID from DataContext for color lookup
                var gameId = GetGameId(listBoxItem.DataContext);
                Color glowColor;
                if (gameId != Guid.Empty && icon.Source != null)
                {
                    var (primary, _) = _colorExtractor.GetGlowColors(gameId, icon.Source);
                    glowColor = primary;
                }
                else
                {
                    glowColor = Color.FromRgb(100, 149, 237); // fallback cornflower blue
                }

                _savedEffect = icon.Effect;
                icon.Effect = new DropShadowEffect
                {
                    ShadowDepth = 0,
                    Color = glowColor,
                    BlurRadius = 12,
                    Opacity = 0.85
                };
                _hoveredIcon = icon;
            }
            catch
            {
                // Icon may have been recycled
            }
        }

        private void ClearHoverGlow()
        {
            if (_hoveredIcon == null) return;

            try
            {
                _hoveredIcon.Effect = _savedEffect;
            }
            catch
            {
                // Icon may have been removed from visual tree
            }

            _hoveredIcon = null;
            _savedEffect = null;
        }

        // Check if element is inside PART_ControlGameView (the details panel)
        private bool IsInsideGameView(DependencyObject element)
        {
            var current = element;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.Name == "PART_ControlGameView")
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        // Extract Game.Id from a list item's DataContext.
        // Playnite uses GamesCollectionViewEntry which has a Game property.
        private Guid GetGameId(object dataContext)
        {
            if (dataContext == null) return Guid.Empty;

            try
            {
                // Playnite's list items have DataContext = GamesCollectionViewEntry
                // which has a .Game property (or .Id directly)
                var type = dataContext.GetType();

                // Try .Id property first (GamesCollectionViewEntry.Id)
                var idProp = type.GetProperty("Id");
                if (idProp != null && idProp.PropertyType == typeof(Guid))
                    return (Guid)idProp.GetValue(dataContext);

                // Try .Game.Id
                var gameProp = type.GetProperty("Game");
                if (gameProp != null)
                {
                    var game = gameProp.GetValue(dataContext);
                    if (game != null)
                    {
                        var gameIdProp = game.GetType().GetProperty("Id");
                        if (gameIdProp != null)
                            return (Guid)gameIdProp.GetValue(game);
                    }
                }
            }
            catch
            {
                // Reflection failure — not a game item
            }

            return Guid.Empty;
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet clean -c Release && dotnet build -c Release`
Expected: Build succeeded, 0 errors

---

### Task 3: Wire up in UniPlaySong.cs

**Files:**
- Modify: `src/UniPlaySong.cs` — field declaration (~line 114), instantiation in `OnApplicationStarted` (~line 542-556), cleanup in `OnApplicationStopped` (~line 924)

- [ ] **Step 1: Add field declaration**

Near `_iconGlowManager` field (~line 114):

```csharp
private IconGlow.ListHoverGlowManager _listHoverGlowManager;
```

- [ ] **Step 2: Instantiate and attach in `OnApplicationStarted`**

After the `_iconGlowManager` block (~line 556), add:

```csharp
_listHoverGlowManager = new IconGlow.ListHoverGlowManager(_settingsService, _iconGlowManager?.ColorExtractor ?? new IconGlow.IconColorExtractor(), _fileLogger);
System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
    Application.Current?.Dispatcher?.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Loaded,
        new Action(() => _listHoverGlowManager?.Attach())));
```

**Note:** We need to share the `IconColorExtractor` between managers so the color cache is shared. This requires exposing it from `IconGlowManager` — add a public property:

In `IconGlowManager.cs` (~line 25):
```csharp
public IconColorExtractor ColorExtractor => _colorExtractor;
```

- [ ] **Step 3: Clean up in `OnApplicationStopped`**

After `_iconGlowManager?.Destroy()` (~line 924):

```csharp
_listHoverGlowManager?.Detach();
_listHoverGlowManager = null;
```

- [ ] **Step 4: Build and package**

Run: `dotnet clean -c Release && dotnet build -c Release && powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1`
Expected: Build succeeded, package created

---

### Task 4: Test and verify

- [ ] **Step 1: Install in Playnite and verify behavior**

Check:
- Hovering over game icons in list view shows a colored glow halo
- Moving to a different game icon switches the glow
- Moving to non-icon space (empty area, text) removes the glow
- The selected game's detail-panel icon still has its full animated glow (no double glow)
- Scrolling the list doesn't crash or leave orphaned effects
- Disabling "Enable Icon Glow" in settings disables hover glow too
- Performance is acceptable (no lag on mouse move)

---

## Risk Notes

- **Playnite theme variability** — Different themes may not have `ListBoxItem` or `PART_ImageIcon` in the expected locations. The code is defensive (returns silently on null).
- **Virtualization recycling** — Icons can be recycled mid-hover. All effect operations are wrapped in try/catch.
- **PreviewMouseMove frequency** — Fires on every pixel of mouse movement. The handler is cheap (hit-test + ancestor walk + reference comparison for same-icon skip), but worth monitoring for perf.
