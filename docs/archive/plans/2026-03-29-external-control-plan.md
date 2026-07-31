# External Control via URI Handler — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Allow external tools to control UniPlaySong playback via `playnite://uniplaysong/` URI commands.

**Architecture:** A thin `ExternalControlService` parses URI arguments and dispatches to `IMusicPlaybackService`. `UniPlaySong.cs` registers the URI source on startup and unregisters on shutdown. Error notifications use Playnite's built-in notification system.

**Tech Stack:** C# / .NET 4.6.2 / Playnite SDK 6.15 (`IUriHandlerAPI`, `PlayniteUriEventArgs`, `NotificationMessage`)

---

### Task 1: Create ExternalControlService

**Files:**
- Create: `src/Services/ExternalControlService.cs`

**Step 1: Create the service file**

```csharp
using Playnite.SDK;
using Playnite.SDK.Events;

namespace UniPlaySong.Services
{
    // Handles external playback commands received via Playnite URI protocol.
    // URI format: playnite://uniplaysong/{command}[/{argument}]
    public class ExternalControlService
    {
        private readonly IMusicPlaybackService _playbackService;
        private readonly IPlayniteAPI _api;
        private const string NotificationPrefix = "UniPlaySong_ExtCtrl";

        public ExternalControlService(IMusicPlaybackService playbackService, IPlayniteAPI api)
        {
            _playbackService = playbackService;
            _api = api;
        }

        public void HandleCommand(PlayniteUriEventArgs args)
        {
            if (args.Arguments == null || args.Arguments.Length == 0)
            {
                Notify("No command specified");
                return;
            }

            var command = args.Arguments[0].ToLowerInvariant();

            switch (command)
            {
                case "play":
                    _playbackService.Resume();
                    break;

                case "pause":
                    _playbackService.Pause();
                    break;

                case "playpausetoggle":
                    if (_playbackService.IsPlaying)
                        _playbackService.Pause();
                    else
                        _playbackService.Resume();
                    break;

                case "skip":
                    _playbackService.SkipToNextSong();
                    break;

                case "restart":
                    _playbackService.RestartCurrentSong();
                    break;

                case "stop":
                    _playbackService.Stop();
                    break;

                case "volume":
                    HandleVolume(args.Arguments);
                    break;

                default:
                    Notify($"Unknown command \"{command}\"");
                    break;
            }
        }

        private void HandleVolume(string[] arguments)
        {
            if (arguments.Length < 2)
            {
                Notify("Volume requires a value (0-100)");
                return;
            }

            if (!int.TryParse(arguments[1], out int volume))
            {
                Notify($"Invalid volume value \"{arguments[1]}\"");
                return;
            }

            if (volume < 0 || volume > 100)
            {
                Notify("Volume must be between 0 and 100");
                return;
            }

            _playbackService.SetVolume(volume);
        }

        private void Notify(string message)
        {
            _api.Notifications.Add(new NotificationMessage(
                NotificationPrefix,
                $"UniPlaySong: {message}",
                NotificationType.Info));
        }
    }
}
```

**Step 2: Build to verify compilation**

```bash
dotnet clean -c Release && dotnet build -c Release
```
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Services/ExternalControlService.cs
git commit -m "add ExternalControlService for URI-based playback control"
```

---

### Task 2: Register URI Handler in UniPlaySong.cs

**Files:**
- Modify: `src/UniPlaySong.cs`

**Step 1: Add the service field**

After line ~113 (`private Services.Controller.ControllerEventRouter _controllerEventRouter;`), add:

```csharp
private Services.ExternalControlService _externalControlService;
```

**Step 2: Instantiate the service**

In `InitializeServices()` or wherever the other services are created, after playback service is ready, add:

```csharp
_externalControlService = new Services.ExternalControlService(_playbackService, _api);
```

**Step 3: Register the URI source in OnApplicationStarted**

At the end of `OnApplicationStarted()`, add:

```csharp
// Register URI handler for external playback control (playnite://uniplaysong/...)
_api.UriHandler.RegisterSource("uniplaysong", args => _externalControlService?.HandleCommand(args));
```

**Step 4: Unregister in OnApplicationStopped**

In `OnApplicationStopped()`, before the existing cleanup (around line ~967), add:

```csharp
_api.UriHandler.RemoveSource("uniplaysong");
_externalControlService = null;
```

**Step 5: Build to verify**

```bash
dotnet clean -c Release && dotnet build -c Release
```
Expected: Build succeeded

**Step 6: Commit**

```bash
git add src/UniPlaySong.cs
git commit -m "register uniplaysong URI handler on startup"
```

---

### Task 3: Manual Testing

**Step 1: Package the extension**

```bash
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

**Step 2: Install and test each command**

Open Windows Run (Win+R) and test each URI:

- `playnite://uniplaysong/play` — should resume playback
- `playnite://uniplaysong/pause` — should pause playback
- `playnite://uniplaysong/playpausetoggle` — should toggle
- `playnite://uniplaysong/skip` — should skip to next song
- `playnite://uniplaysong/restart` — should restart current song
- `playnite://uniplaysong/stop` — should stop playback
- `playnite://uniplaysong/volume/50` — should set volume to 50
- `playnite://uniplaysong/volume/150` — should show error notification
- `playnite://uniplaysong/foo` — should show "Unknown command" notification
- `playnite://uniplaysong` — should show "No command specified" notification

---

### Task 4: Update Architecture Documentation

**Files:**
- Modify: `docs/dev_docs/ARCHITECTURE.md`

**Step 1: Add External Control section**

Add a new section documenting the URI handler system, the `ExternalControlService`, the command set, and the data flow. Include the URI format and all supported commands.

**Step 2: Commit**

```bash
git add docs/dev_docs/ARCHITECTURE.md
git commit -m "document external control URI handler in architecture docs"
```

---

### Task 5: Clean Build and Package

**Step 1: Full clean build and package**

```bash
dotnet clean -c Release && dotnet build -c Release && powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```
Expected: Build succeeded, package created in `pext/`
