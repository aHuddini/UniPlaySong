# Controller SDK Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace XInput polling loops with Playnite SDK 6.15 event-driven controller input across all 5 dialogs, DialogHelper modals, and login monitoring.

**Architecture:** Centralized `ControllerEventRouter` receives SDK events via `OnDesktopControllerButtonStateChanged` override and forwards to the currently active `IControllerInputReceiver`. Each dialog implements the interface, registers on Loaded, unregisters on Unloaded. Continuous repeat (hold-to-repeat) uses `DispatcherTimer` instead of polling-based hold detection.

**Tech Stack:** C# / .NET 4.6.2 / WPF / Playnite SDK 6.15 / `Playnite.SDK.Events` namespace

**Build/verify command (run after every task):**
```bash
dotnet clean -c Release && dotnet build -c Release && powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

**Reference docs:**
- Roadmap: `docs/dev_docs/roadmaps/CONTROLLER_SDK_MIGRATION_ROADMAP.md`
- Architecture: `docs/dev_docs/ARCHITECTURE.md`

---

## Task 1: Create IControllerInputReceiver Interface

**Files:**
- Create: `src/Services/Controller/IControllerInputReceiver.cs`

**Step 1: Create the interface file**

```csharp
using Playnite.SDK.Events;

namespace UniPlaySong.Services.Controller
{
    // Implemented by dialogs that receive controller input from the centralized event router.
    // Only one receiver is active at a time (last registered wins).
    public interface IControllerInputReceiver
    {
        void OnControllerButtonPressed(ControllerInput button);
        void OnControllerButtonReleased(ControllerInput button);
    }
}
```

**Step 2: Build to verify the SDK types resolve**

Run: `dotnet build -c Release`
Expected: Build succeeds. If `ControllerInput` is not found, check SDK version and `Playnite.SDK.Events` namespace.

**Step 3: Commit**

```
feat: Add IControllerInputReceiver interface for SDK controller events
```

---

## Task 2: Create ControllerEventRouter Service

**Files:**
- Create: `src/Services/Controller/ControllerEventRouter.cs`

**Step 1: Create the router**

```csharp
using System;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Events;

namespace UniPlaySong.Services.Controller
{
    // Routes Playnite SDK controller events to the currently active dialog.
    // Only one receiver at a time — last registered wins (handles nested modals).
    public class ControllerEventRouter
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private IControllerInputReceiver _activeReceiver;
        private readonly object _lock = new object();
        private Common.FileLogger _fileLogger;

        public ControllerEventRouter(Common.FileLogger fileLogger = null)
        {
            _fileLogger = fileLogger;
        }

        public void Register(IControllerInputReceiver receiver)
        {
            lock (_lock)
            {
                _activeReceiver = receiver;
                _fileLogger?.Debug($"[ControllerRouter] Registered: {receiver.GetType().Name}");
            }
        }

        public void Unregister(IControllerInputReceiver receiver)
        {
            lock (_lock)
            {
                if (_activeReceiver == receiver)
                {
                    _fileLogger?.Debug($"[ControllerRouter] Unregistered: {receiver.GetType().Name}");
                    _activeReceiver = null;
                }
            }
        }

        // Called from UniPlaySong.OnDesktopControllerButtonStateChanged (may fire from any thread)
        public void HandleButtonPressed(ControllerInput button)
        {
            IControllerInputReceiver receiver;
            lock (_lock)
            {
                receiver = _activeReceiver;
            }

            if (receiver == null) return;

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                receiver.OnControllerButtonPressed(button);
            }
            else
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    receiver.OnControllerButtonPressed(button);
                }));
            }
        }

        public void HandleButtonReleased(ControllerInput button)
        {
            IControllerInputReceiver receiver;
            lock (_lock)
            {
                receiver = _activeReceiver;
            }

            if (receiver == null) return;

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                receiver.OnControllerButtonReleased(button);
            }
            else
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    receiver.OnControllerButtonReleased(button);
                }));
            }
        }
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build -c Release`
Expected: Build succeeds.

**Step 3: Commit**

```
feat: Add ControllerEventRouter — centralized SDK event forwarding to active dialog
```

---

## Task 3: Wire Router into UniPlaySong.cs

**Files:**
- Modify: `src/UniPlaySong.cs`
  - Add field (~line 110): `private Services.Controller.ControllerEventRouter _controllerEventRouter;`
  - Add public accessor: `public Services.Controller.ControllerEventRouter ControllerEventRouter => _controllerEventRouter;`
  - In `OnLoaded()` (~line 270): `_controllerEventRouter = new Services.Controller.ControllerEventRouter(_fileLogger);`
  - In `OnApplicationStopped()` (~line 927): `_controllerEventRouter = null;`
  - Add SDK override methods (after existing overrides)

**Step 1: Add field, accessor, and initialization**

Add field near other service fields (~line 110):
```csharp
private Services.Controller.ControllerEventRouter _controllerEventRouter;
```

Add public accessor (near other public service accessors):
```csharp
public Services.Controller.ControllerEventRouter ControllerEventRouter => _controllerEventRouter;
```

Initialize in `OnLoaded()` after other service initialization:
```csharp
_controllerEventRouter = new Services.Controller.ControllerEventRouter(_fileLogger);
```

Null out in `OnApplicationStopped()`:
```csharp
_controllerEventRouter = null;
```

**Step 2: Add SDK controller event overrides**

Add after existing override methods (after `OnApplicationStopped`):
```csharp
public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
{
    if (args == null) return;

    // Login bypass — check before routing to active dialog
    if (_isControllerLoginMonitoring && args.State == ControllerInputState.Pressed
        && (args.Button == ControllerInput.A || args.Button == ControllerInput.Start))
    {
        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            _coordinator?.HandleLoginDismiss();
        }));
        StopControllerLoginMonitoring();
        return;
    }

    if (args.State == ControllerInputState.Pressed)
        _controllerEventRouter?.HandleButtonPressed(args.Button);
    else if (args.State == ControllerInputState.Released)
        _controllerEventRouter?.HandleButtonReleased(args.Button);
}
```

**Step 3: Build and verify**

Run: `dotnet clean -c Release && dotnet build -c Release`
Expected: Build succeeds. No functional change yet — no dialogs are registered as receivers.

**Step 4: Package and quick smoke test**

Run: `powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1`
Install in Playnite, verify:
- Playnite launches normally
- Music plays when selecting games
- No errors in extension.log

**Step 5: Commit**

```
feat: Wire ControllerEventRouter into UniPlaySong — SDK controller events now routed
```

---

## Task 4: Migrate ControllerFilePickerDialog (Phase 2)

**Files:**
- Modify: `src/Views/ControllerFilePickerDialog.xaml.cs`

This is the simplest dialog — D-pad navigation, A/B/X/Y buttons, LB/RB page nav, triggers for jump. No continuous repeat.

**Step 1: Add IControllerInputReceiver implementation**

Add to class declaration:
```csharp
public partial class ControllerFilePickerDialog : Window, IControllerInputReceiver
```

Add `using UniPlaySong.Services.Controller;` and `using Playnite.SDK.Events;` imports.

**Step 2: Add register/unregister in Loaded/Closing events**

In the constructor or Loaded event, after existing initialization:
```csharp
_plugin.ControllerEventRouter?.Register(this);
```

In the Closing event handler:
```csharp
_plugin.ControllerEventRouter?.Unregister(this);
```

(The `_plugin` reference — check how the dialog currently gets the UniPlaySong instance. It may be passed via constructor or accessed statically.)

**Step 3: Implement OnControllerButtonPressed**

Map all existing button handling from `HandleControllerInput()` to the new method:
```csharp
public void OnControllerButtonPressed(ControllerInput button)
{
    switch (button)
    {
        case ControllerInput.A:
            ConfirmButton_Click(null, null);
            break;
        case ControllerInput.B:
            CancelButton_Click(null, null);
            break;
        case ControllerInput.X:
        case ControllerInput.Y:
            PreviewSelectedFile();
            break;
        case ControllerInput.DPadUp:
            if (TryDpadNavigation()) NavigateList(-1);
            break;
        case ControllerInput.DPadDown:
            if (TryDpadNavigation()) NavigateList(1);
            break;
        case ControllerInput.LeftShoulder:
            if (TryDpadNavigation()) NavigateList(-5);
            break;
        case ControllerInput.RightShoulder:
            if (TryDpadNavigation()) NavigateList(5);
            break;
        case ControllerInput.LeftTrigger:
            JumpToTop();
            break;
        case ControllerInput.RightTrigger:
            JumpToBottom();
            break;
    }
}

public void OnControllerButtonReleased(ControllerInput button)
{
    // No continuous repeat in this dialog — no-op
}
```

Note: The exact method names (`ConfirmButton_Click`, `NavigateList`, `PreviewSelectedFile`, `JumpToTop`, `JumpToBottom`) must match the existing code. Read the current `HandleControllerInput` or `CheckButtonPresses` method to find exact names and adapt.

**Step 4: Remove XInput polling infrastructure**

Delete from this file:
- `StartControllerMonitoring()` method
- `StopControllerMonitoring()` method
- `CheckButtonPresses()` method
- `_cancellationTokenSource` field
- `_isMonitoring` field
- `_lastButtonState` field
- Any `XInputWrapper` references/imports
- The call to `StartControllerMonitoring()` in Loaded
- The call to `StopControllerMonitoring()` in Closing

Keep:
- `_lastDpadNavigationTime` and `TryDpadNavigation()` (300ms debounce — still needed)
- `PreviewKeyDown` handler (keyboard fallback)
- All existing UI/business logic

**Step 5: Build and verify**

Run: `dotnet clean -c Release && dotnet build -c Release && powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1`
Expected: Build succeeds.

**Step 6: Manual test**

In Playnite fullscreen with Xbox controller:
1. Right-click a game → Set Primary Song (or similar action that opens ControllerFilePickerDialog)
2. D-pad Up/Down → navigates the file list
3. A → confirms selection
4. B → cancels
5. X or Y → previews the selected file
6. LB/RB → pages up/down
7. Keyboard still works as fallback

**Step 7: Commit**

```
refactor: Migrate ControllerFilePickerDialog from XInput polling to SDK events
```

---

## Task 5: Migrate ControllerDeleteSongsDialog (Phase 3a)

**Files:**
- Modify: `src/Views/ControllerDeleteSongsDialog.xaml.cs`

Same pattern as Task 4 but also needs: modal cooldown replacement, button release waiting replacement, deletion state guards.

**Step 1: Implement IControllerInputReceiver**

Same pattern: add interface, register in Loaded, unregister in Closing.

**Step 2: Implement OnControllerButtonPressed**

Map buttons from existing `HandleControllerInput`. Add modal cooldown check at top:
```csharp
public void OnControllerButtonPressed(ControllerInput button)
{
    // Block input during modal cooldown
    if (DateTime.Now < _modalCooldownUntil) return;

    // Block during active deletion
    if (_isDeletionInProgress || _isShowingConfirmation) return;

    switch (button)
    {
        case ControllerInput.A:
            DeleteButton_Click(null, null);
            break;
        case ControllerInput.B:
            CancelButton_Click(null, null);
            break;
        // ... D-pad, LB/RB, triggers same as Task 4
    }
}
```

**Step 3: Replace RefreshControllerStateWithCooldown**

Replace with simple timestamp:
```csharp
private void ActivateModalCooldown()
{
    _modalCooldownUntil = DateTime.Now.AddMilliseconds(ModalCooldownMs);
}
```
Call this wherever `RefreshControllerStateWithCooldown()` was called.

**Step 4: Replace WaitForButtonReleaseBeforeClose**

No longer needed — SDK events are edge-detected. The modal cooldown (350ms) already prevents button leak. Simply close the dialog directly.

**Step 5: Remove XInput polling, build, test, commit**

Same cleanup as Task 4. Test: fullscreen → right-click game → Delete Songs → navigate, delete with A, confirm modal, cancel with B.

```
refactor: Migrate ControllerDeleteSongsDialog from XInput polling to SDK events
```

---

## Task 6: Migrate SimpleControllerDialog (Phase 3b)

**Files:**
- Modify: `src/Views/SimpleControllerDialog.xaml.cs`

Download dialog with wizard flow. Extra: preview rate limiting (2000ms), modal cooldown.

**Step 1-3: Same IControllerInputReceiver pattern**

Register/unregister, implement OnControllerButtonPressed with modal cooldown guard.

**Step 4: Keep preview rate limiting**

The `_lastPreviewTime` + `MinPreviewIntervalMs = 2000` check stays in the button handler — it's business logic, not polling infrastructure.

**Step 5: Remove XInput polling, build, test, commit**

Test: fullscreen → right-click game → Download → navigate sources/albums/songs with D-pad, select with A, back with B, preview with X/Y.

```
refactor: Migrate SimpleControllerDialog from XInput polling to SDK events
```

---

## Task 7: Migrate ControllerAmplifyDialog (Phase 3c)

**Files:**
- Modify: `src/Views/ControllerAmplifyDialog.xaml.cs`

Two-mode dialog (file selection + amplify editor). Editor mode needs continuous D-pad repeat for gain adjustment.

**Step 1-2: IControllerInputReceiver + register/unregister**

**Step 3: Implement with mode-aware input and repeat timer**

```csharp
private DispatcherTimer _repeatTimer;
private ControllerInput _heldButton;

public void OnControllerButtonPressed(ControllerInput button)
{
    if (DateTime.Now < _modalCooldownUntil) return;

    if (_currentStep == DialogStep.FileSelection)
    {
        HandleFileSelectionInput(button);
    }
    else // AmplifyEditor
    {
        HandleEditorInput(button);

        // Start repeat for D-pad in editor mode
        if (IsDpadButton(button))
        {
            _heldButton = button;
            _repeatTimer.Interval = TimeSpan.FromMilliseconds(200); // InitialRepeatDelayMs
            _repeatTimer.Start();
        }
    }
}

public void OnControllerButtonReleased(ControllerInput button)
{
    if (button == _heldButton)
    {
        _repeatTimer?.Stop();
        _heldButton = default;
    }
}

private void RepeatTimer_Tick(object sender, EventArgs e)
{
    HandleEditorInput(_heldButton);
    _repeatTimer.Interval = TimeSpan.FromMilliseconds(50); // FastRepeatIntervalMs
}
```

Editor input handles: D-pad Up/Down for fine gain (0.5dB), LB/RB for coarse gain (3dB), A preview, B back, X apply, Y reset.

**Step 4: Initialize/dispose repeat timer**

Create in constructor, stop+null in Closing.

**Step 5: Remove XInput polling, build, test, commit**

Test: fullscreen → right-click game → Amplify → select file → adjust gain with D-pad (hold for continuous), preview with A, apply with X.

```
refactor: Migrate ControllerAmplifyDialog from XInput polling to SDK events
```

---

## Task 8: Migrate ControllerWaveformTrimDialog (Phase 3d)

**Files:**
- Modify: `src/Views/ControllerWaveformTrimDialog.xaml.cs`

Two-mode dialog. Editor mode: D-pad Left/Right for start marker, Up/Down for end marker, LB/RB for window contract/expand. All with continuous repeat.

**Step 1-2: IControllerInputReceiver + register/unregister**

**Step 3: Implement with repeat timer (same pattern as Task 7)**

Editor input mapping:
- D-pad Left/Right → move start marker (500ms increment, continuous repeat)
- D-pad Up/Down → move end marker (500ms increment, continuous repeat)
- LB → contract window (shrink both markers inward by 500ms)
- RB → expand window (expand both markers outward by 500ms)
- A → preview trim window
- B → back to file selection
- X → apply trim
- Y → reset markers to full duration

**Step 4: Remove HandleTriggerInput() stub** (empty method, not needed)

**Step 5: Remove XInput polling, build, test, commit**

Test: fullscreen → right-click game → Trim → select file → move markers with D-pad (hold for continuous), contract/expand with LB/RB, preview with A, apply with X.

```
refactor: Migrate ControllerWaveformTrimDialog from XInput polling to SDK events
```

---

## Task 9: Migrate DialogHelper Modals (Phase 4a)

**Files:**
- Modify: `src/Common/DialogHelper.cs`

Two methods with XInput polling: `ShowControllerMessage` (~32 lines polling) and `ShowControllerConfirmation` (~51 lines polling). Plus `WaitForButtonRelease` (~34 lines).

**Step 1: Create inline IControllerInputReceiver for ShowControllerMessage**

Replace the `Task.Run` XInput polling with a lightweight anonymous receiver:
```csharp
// Inside ShowControllerMessage, after window creation:
var router = pluginInstance?.ControllerEventRouter;
IControllerInputReceiver receiver = null;

if (router != null)
{
    receiver = new SimpleButtonReceiver(window, button =>
    {
        if (button == ControllerInput.A || button == ControllerInput.B)
            window?.Close();
    });
    router.Register(receiver);
}

// In window Closing handler:
if (receiver != null) router?.Unregister(receiver);
```

Create a small private inner class or helper:
```csharp
private class SimpleButtonReceiver : IControllerInputReceiver
{
    private readonly Action<ControllerInput> _onPressed;

    public SimpleButtonReceiver(Window owner, Action<ControllerInput> onPressed)
    {
        _onPressed = onPressed;
    }

    public void OnControllerButtonPressed(ControllerInput button) => _onPressed?.Invoke(button);
    public void OnControllerButtonReleased(ControllerInput button) { }
}
```

**Step 2: Same pattern for ShowControllerConfirmation**

Handler maps: D-pad Left/Right → toggle selection, A → confirm, B → cancel.

**Step 3: Remove WaitForButtonRelease**

No longer needed — SDK events are edge-detected. Remove all calls to `WaitForButtonRelease()` throughout DialogHelper. The 350ms modal cooldown in calling dialogs already prevents button leak.

**Step 4: Remove XInput polling code from both methods**

Delete the `Task.Run` polling blocks and associated `CancellationTokenSource` handling.

**Step 5: Build, test, commit**

Test: fullscreen → trigger a confirmation dialog (e.g., delete a song) → A confirms, B cancels, D-pad switches selection.

```
refactor: Migrate DialogHelper modals from XInput polling to SDK events
```

---

## Task 10: Migrate Login Monitoring (Phase 4b)

**Files:**
- Modify: `src/UniPlaySong.cs`

Login bypass is already handled in Task 3's `OnDesktopControllerButtonStateChanged` override. Now clean up the old polling code.

**Step 1: Remove old login monitoring methods**

Delete from UniPlaySong.cs:
- `StartControllerLoginMonitoring()` method
- `StopControllerLoginMonitoring()` method
- `CheckLoginBypassButtonPresses()` method
- `_controllerLoginMonitoringCancellation` field
- `_isControllerLoginMonitoring` field (keep if used by the SDK override check)
- `_lastControllerLoginState` / `_hasLastLoginState` fields

**Step 2: Update callers**

Find all calls to `StartControllerLoginMonitoring()` and replace with just setting a flag:
```csharp
_isControllerLoginMonitoring = true;
```

Find all calls to `StopControllerLoginMonitoring()` and replace with:
```csharp
_isControllerLoginMonitoring = false;
```

**Step 3: Build, test, commit**

Test: fullscreen → launch with SkipFirstSelectionAfterModeSwitch enabled → press A on controller → login dismissed, music plays.

```
refactor: Replace login controller polling with SDK event check
```

---

## Task 11: Cleanup — Remove XInput Polling Infrastructure (Phase 5)

**Files:**
- Potentially delete: `src/Services/Controller/ControllerDetectionService.cs`
- Potentially delete: `src/Common/XInputWrapper.cs`
- Modify: `src/Services/Controller/ControllerOverlay.cs` (if it references XInput)
- Modify: Any remaining files with XInput imports

**Step 1: Search for remaining XInput references**

```bash
grep -rn "XInputWrapper\|XInputGetState\|XINPUT_" src/ --include="*.cs"
```

If only `ControllerDetectionService.cs`, `XInputWrapper.cs`, and `ControllerOverlay.cs` remain — proceed with cleanup.

**Step 2: Evaluate ControllerOverlay.cs**

If it still uses `ControllerDetectionService` for detection, update it to use a simple flag set by `OnControllerConnected`/`OnControllerDisconnected` SDK events. If it's no longer needed, mark for future removal.

**Step 3: Delete or gut ControllerDetectionService.cs**

If all detection is now via SDK events, delete the file and remove references.

**Step 4: Delete XInputWrapper.cs if unused**

If no remaining code references `XInputWrapper`, delete the file.

**Step 5: Clean up unused imports across all migrated files**

Remove `using UniPlaySong.Common;` (for XInputWrapper) from all dialog files.

**Step 6: Build, full test, commit**

Run full clean/build/package. Test all 5 dialogs + DialogHelper modals + login bypass. Verify no XInput references remain.

```
refactor: Remove XInput polling infrastructure — migration complete
```

---

## Verification Checklist (after all tasks)

- [ ] Build: `dotnet clean -c Release && dotnet build -c Release` — 0 errors
- [ ] Package: `powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1` — success
- [ ] Xbox controller: All 5 dialogs navigate, confirm, cancel correctly
- [ ] Xbox controller: Hold D-pad for continuous repeat in Amplify and Trim dialogs
- [ ] Xbox controller: DialogHelper confirmation dialogs work (A/B/D-pad)
- [ ] Xbox controller: Login bypass works in fullscreen
- [ ] Keyboard: All dialogs still work with keyboard (PreviewKeyDown unchanged)
- [ ] No controller: Playnite operates normally, no errors
- [ ] PS/Switch controller (if available): Verify SDK SDL2 passthrough works
- [ ] No XInput references remain: `grep -rn "XInputWrapper" src/ --include="*.cs"` returns nothing (except possibly ControllerOverlay if kept)
