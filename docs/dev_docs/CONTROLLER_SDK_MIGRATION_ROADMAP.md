# Roadmap: Migrate Controller Input from XInput Polling to SDK 6.15 Events

**Date:** 2026-02-14
**Status:** Planned (future refactor)
**Priority:** Low — current XInput polling works, this is a quality/compatibility improvement

---

## Context

UniPlaySong's 5 controller dialogs each run their own 33ms XInput polling loops via `Task.Run` + `XInputWrapper` P/Invoke. This is:
- **XInput-only** — PS4/PS5/Switch Pro controllers don't work
- **CPU wasteful** — 5 independent polling loops when only 1 dialog is active
- **Duplicated** — same polling/edge-detection/debounce code copied across all 5 dialogs + DialogHelper + login monitoring

Playnite SDK 6.15 provides event-driven controller API (`OnDesktopControllerButtonStateChanged`) that:
- Uses SDL2 internally — supports Xbox, PlayStation, Switch, generic controllers
- Fires discrete Pressed/Released events (already edge-detected)
- Eliminates polling entirely — zero CPU when idle

ControlUp 2.0 (our other plugin) already successfully adopted this pattern.

---

## Architecture Overview

### Current: Per-Dialog XInput Polling
```
Dialog Loaded → Task.Run(polling loop) → XInputGetState every 33ms → edge detect → Dispatcher.BeginInvoke → HandleControllerInput
```

### New: Centralized SDK Event Forwarding
```
SDK fires OnDesktopControllerButtonStateChanged → UniPlaySong routes to active IControllerInputReceiver → dialog handles input directly on UI thread
```

---

## Phase 1: Infrastructure — Event Router + Interface

### New Interface: `IControllerInputReceiver`

**New file:** `Services/Controller/IControllerInputReceiver.cs`
```csharp
using Playnite.SDK.Events;

namespace UniPlaySong.Services.Controller
{
    public interface IControllerInputReceiver
    {
        void OnControllerButtonPressed(ControllerInput button);
        void OnControllerButtonReleased(ControllerInput button);
    }
}
```

### New Service: `ControllerEventRouter`

**New file:** `Services/Controller/ControllerEventRouter.cs`

Singleton that tracks the active receiver and forwards SDK events:
```csharp
public class ControllerEventRouter
{
    private IControllerInputReceiver _activeReceiver;
    private readonly object _lock = new object();

    public void Register(IControllerInputReceiver receiver) { ... }
    public void Unregister(IControllerInputReceiver receiver) { ... }

    // Called from UniPlaySong.OnDesktopControllerButtonStateChanged
    public void HandleButtonStateChanged(OnControllerButtonStateChangedArgs args) { ... }
}
```

Key behaviors:
- Only ONE receiver at a time (last registered wins — handles nested modals)
- `HandleButtonStateChanged` dispatches to WPF UI thread via `Application.Current.Dispatcher`
- Thread-safe registration (SDK events may fire from any thread)

### Modify: `UniPlaySong.cs`

Add field + override:
```csharp
private ControllerEventRouter _controllerEventRouter;

// In OnApplicationStarted:
_controllerEventRouter = new ControllerEventRouter();

public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
{
    _controllerEventRouter?.HandleButtonStateChanged(args);
}

public override void OnControllerConnected(OnControllerConnectedArgs args)
{
    // Update IsControllerMode state (replaces ControllerDetectionService polling)
}

public override void OnControllerDisconnected(OnControllerDisconnectedArgs args)
{
    // Update IsControllerMode state
}
```

Expose router for dialogs: `public ControllerEventRouter ControllerEventRouter => _controllerEventRouter;`

### Files to create/modify:
| File | Action |
|------|--------|
| `Services/Controller/IControllerInputReceiver.cs` | **NEW** — interface |
| `Services/Controller/ControllerEventRouter.cs` | **NEW** — event routing service |
| `UniPlaySong.cs` | Add overrides + router field + expose router |

### Test: Build succeeds, Playnite launches, no functional change yet.

---

## Phase 2: Migrate ControllerFilePickerDialog (simplest)

This dialog has the simplest input handling — D-pad navigation + A/B confirm/cancel. No continuous repeat.

### Modify: `Views/ControllerFilePickerDialog.xaml.cs`

1. Implement `IControllerInputReceiver`
2. In `Loaded` event: register with router (get via `UniPlaySong` instance or pass in)
3. In `Unloaded` event: unregister from router
4. Move `HandleControllerInput(pressedButtons)` logic to `OnControllerButtonPressed(ControllerInput button)`
   - Map `ControllerInput.DPadUp` → existing up navigation
   - Map `ControllerInput.A` → confirm
   - Map `ControllerInput.B` → cancel
   - Keep 300ms debounce for D-pad (timestamp check stays)
5. Remove: `StartControllerMonitoring()`, `StopControllerMonitoring()`, `CheckButtonPresses()`, XInput state fields
6. `OnControllerButtonReleased` — no-op for this dialog (no continuous repeat)

### Button mapping translation:
| XInputWrapper constant | SDK ControllerInput |
|----------------------|-------------------|
| `XINPUT_GAMEPAD_A` | `ControllerInput.A` |
| `XINPUT_GAMEPAD_B` | `ControllerInput.B` |
| `XINPUT_GAMEPAD_DPAD_UP` | `ControllerInput.DPadUp` |
| `XINPUT_GAMEPAD_DPAD_DOWN` | `ControllerInput.DPadDown` |
| `XINPUT_GAMEPAD_DPAD_LEFT` | `ControllerInput.DPadLeft` |
| `XINPUT_GAMEPAD_DPAD_RIGHT` | `ControllerInput.DPadRight` |
| `XINPUT_GAMEPAD_LEFT_SHOULDER` | `ControllerInput.LeftShoulder` |
| `XINPUT_GAMEPAD_RIGHT_SHOULDER` | `ControllerInput.RightShoulder` |
| `XINPUT_GAMEPAD_START` | `ControllerInput.Start` |
| `XINPUT_GAMEPAD_BACK` | `ControllerInput.Back` |
| `XINPUT_GAMEPAD_X` | `ControllerInput.X` |
| `XINPUT_GAMEPAD_Y` | `ControllerInput.Y` |

### Test: Open ControllerFilePickerDialog → navigate with D-pad, confirm with A, cancel with B. Test with Xbox and (if available) PlayStation controller.

---

## Phase 3: Migrate Dialogs with Continuous Repeat

### Continuous repeat design (replaces polling-based hold detection)

Since SDK only gives Pressed/Released events (no "still held"), use a `DispatcherTimer` for repeat:

```csharp
private DispatcherTimer _repeatTimer;
private ControllerInput _heldButton;

void OnControllerButtonPressed(ControllerInput button)
{
    if (IsDpadButton(button))
    {
        // Immediate action
        HandleDpadAction(button);

        // Start repeat timer
        _heldButton = button;
        _repeatTimer.Interval = TimeSpan.FromMilliseconds(200); // InitialRepeatDelayMs
        _repeatTimer.Start();
    }
}

void OnControllerButtonReleased(ControllerInput button)
{
    if (button == _heldButton)
    {
        _repeatTimer.Stop();
        _heldButton = ControllerInput.None;
    }
}

void RepeatTimer_Tick(object sender, EventArgs e)
{
    HandleDpadAction(_heldButton);
    _repeatTimer.Interval = TimeSpan.FromMilliseconds(50); // FastRepeatIntervalMs
}
```

Benefits: Runs on UI thread already (DispatcherTimer), cleaner than background polling with shared state.

### 3a: ControllerDeleteSongsDialog.xaml.cs
- Implement `IControllerInputReceiver`, register/unregister
- Replace polling with event handler
- Keep debounce (300ms) and modal cooldown (350ms)
- Replace `WaitForButtonReleaseBeforeClose()` — track Released events via interface instead of Thread.Sleep polling

### 3b: SimpleControllerDialog.xaml.cs (Download Dialog)
- Same pattern
- Keep preview rate limiting (2000ms MinPreviewIntervalMs)
- Keep modal cooldown

### 3c: ControllerAmplifyDialog.xaml.cs
- Same pattern + continuous repeat for gain adjustment
- Keep step-based state machine (`_currentStep`)
- D-pad repeat for gain changes uses DispatcherTimer pattern above

### 3d: ControllerWaveformTrimDialog.xaml.cs
- Same pattern + continuous repeat for trim marker movement
- Keep marker increment logic (500ms TimeSpan per press)

### Files to modify:
| File | Changes |
|------|---------|
| `Views/ControllerFilePickerDialog.xaml.cs` | Phase 2 |
| `Views/ControllerDeleteSongsDialog.xaml.cs` | Phase 3a |
| `Views/SimpleControllerDialog.xaml.cs` | Phase 3b |
| `Views/ControllerAmplifyDialog.xaml.cs` | Phase 3c |
| `Views/ControllerWaveformTrimDialog.xaml.cs` | Phase 3d |

### Test per dialog: Navigate, confirm, cancel, hold-to-repeat, modal confirmation.

---

## Phase 4: Migrate DialogHelper Modals + Login Monitoring

### 4a: DialogHelper.cs — ShowControllerMessage / ShowControllerConfirmation

These currently have their own polling loops. Two options:
- **Option A:** Make them implement `IControllerInputReceiver` too (register while modal is shown)
- **Option B:** Keep lightweight polling here since these are simple A/B handlers and refactoring would be complex

**Recommend Option A** for consistency. The modal dialog registers itself, handles A/B/D-pad, unregisters on close.

Replace `WaitForButtonRelease()` — Instead of Thread.Sleep polling for button release, use the modal cooldown (350ms) which already prevents button leak. The Released event from SDK tells us when button is released without needing to poll.

### 4b: UniPlaySong.cs — Login Controller Monitoring

Replace the login monitoring polling loop (`StartControllerLoginMonitoring` / `CheckLoginBypassButtonPresses`) with a simple check in `OnDesktopControllerButtonStateChanged`:

```csharp
public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
{
    // Login bypass — check before routing to active dialog
    if (_isLoginSkipActive && args.State == ControllerInputState.Pressed
        && (args.Button == ControllerInput.A || args.Button == ControllerInput.Start))
    {
        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            _coordinator?.HandleLoginDismiss();
        }));
        _isLoginSkipActive = false;
        return;
    }

    _controllerEventRouter?.HandleButtonStateChanged(args);
}
```

Remove: `_controllerLoginMonitoringCancellation`, `StartControllerLoginMonitoring()`, `StopControllerLoginMonitoring()`, `CheckLoginBypassButtonPresses()`, login XInput state fields.

### Files to modify:
| File | Changes |
|------|---------|
| `Common/DialogHelper.cs` | Refactor ShowControllerMessage/Confirmation to use IControllerInputReceiver |
| `UniPlaySong.cs` | Replace login monitoring with SDK event check |

---

## Phase 5: Cleanup — Remove XInput Polling Infrastructure

After all dialogs migrated:

1. **Remove ControllerDetectionService.cs** — replaced by `OnControllerConnected`/`Disconnected`
2. **Remove or minimize XInputWrapper.cs** — only keep if ControllerOverlay (DownloadDialogView) still needs it
3. **Clean up ControllerOverlay.cs** — update to use SDK events or mark for future removal
4. **Remove unused imports** (`XInputWrapper`, `XINPUT_STATE`, etc.) from all migrated files

### Files to modify/delete:
| File | Action |
|------|--------|
| `Services/Controller/ControllerDetectionService.cs` | Delete or gut |
| `Common/XInputWrapper.cs` | Delete if unused, or keep minimal for ControllerOverlay |
| `Services/Controller/ControllerOverlay.cs` | Update or deprecate |
| All migrated dialog files | Remove XInput imports |

---

## What Stays the Same

These are app-specific and remain unchanged:
- **D-pad debounce** (300ms) — rate limits navigation speed
- **Modal cooldown** (350ms) — prevents button leak between dialogs
- **Initial repeat delay** (200ms) / **fast repeat** (50ms) — hold-to-repeat UX
- **Preview rate limiting** (2000ms in download dialog)
- **Dialog state machines** (step-based flow in amplify/trim dialogs)

---

## Key Benefits

1. **PS/Switch controller support** — SDK uses SDL2, handles all controller types
2. **Zero CPU when idle** — no polling loops, events only fire on input
3. **~500 lines of polling code removed** across 5 dialogs + DialogHelper + login
4. **Simpler threading** — DispatcherTimer for repeat runs on UI thread (no cross-thread shared state)
5. **Single event source** — one SDK override routes to active dialog

---

## Verification

1. `dotnet clean -c Release && dotnet build -c Release` — clean build
2. `powershell -ExecutionPolicy Bypass -File package_extension.ps1` — package
3. **Xbox controller**: Test all 5 dialogs — navigate, confirm, cancel, hold-to-repeat
4. **PS controller (if available)**: Same tests — verify SDK SDL2 passthrough works
5. **No controller**: Keyboard navigation still works (unchanged)
6. **Login bypass**: Launch in fullscreen with ThemeCompatibleSilentSkip → press A → music plays
7. **DialogHelper modals**: Delete confirmation → A confirms, B cancels, no double-input
8. **Controller hot-plug**: Plug/unplug during use → state updates correctly
