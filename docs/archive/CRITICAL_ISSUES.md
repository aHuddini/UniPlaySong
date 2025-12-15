# Critical Issues and Development Guidance

## ⚠️ FOR FUTURE AGENTS: READ THIS FIRST

This document contains critical information about unresolved issues and development principles that must be followed.

---

## Development Philosophy

### PlayniteSound is the Superior Source Code

**THE GOLDEN RULE**: Before implementing ANY feature, fix, or solution:

1. **Check PlayniteSound first** (`src/PlayniteSound/`) - it is the **SUPERIOR** and **BATTLE-TESTED** reference implementation
2. **If PlayniteSound doesn't add unnecessary logic, neither should we**
3. **If PlayniteSound uses a simple approach, use the same simple approach**
4. **If PlayniteSound doesn't check for something, don't add that check unless absolutely necessary**

**Why This Matters**: PlayniteSound has been refined over years of real-world use. Our codebase should follow its proven patterns, not reinvent the wheel with unnecessary complexity.

---

## Code Review Approach

**Review the codebase like a senior software developer and engineer with great success in fixing bugs:**

### Step-by-Step Debugging Process

1. **Understand PlayniteSound's Solution**
   - Read PlayniteSound's implementation of the feature/problem
   - Understand WHY it works
   - Note any edge cases it handles

2. **Compare Our Implementation**
   - What are we doing differently?
   - Why are we doing it differently?
   - Is our difference necessary or just adding complexity?

3. **Identify Root Causes**
   - Don't just fix symptoms
   - Understand the underlying issue
   - Check if PlayniteSound has the same issue (if not, why?)

4. **Simplify Before Complicating**
   - Can we use PlayniteSound's simpler approach?
   - What's the minimum change needed?
   - Remove unnecessary code before adding new code

5. **Test Incrementally**
   - Make small, focused changes
   - Test after each change
   - Don't rewrite large sections at once

---

## Critical Unresolved Issue: Login Screen Music

### The Problem

**Status**: ⚠️ **UNRESOLVED** - Highest Priority

Music plays during the login screen transition in fullscreen mode (especially with ANIKI REMAKE theme), when it should only play after the user reaches the library/grid view.

**Expected Behavior**:
- User switches to fullscreen mode
- Login screen appears (no music should play)
- User passes login screen
- Library/grid view appears
- First game is selected → music plays

**Actual Behavior**:
- User switches to fullscreen mode
- Login screen appears
- **Music plays during login screen** ❌
- User passes login screen
- Library/grid view appears
- Music may or may not play for first game

### Why This Matters

This defeats the purpose of a "seamless" console-like experience. Users should see the login screen without music, then music should naturally start when they reach the library view.

### Attempted Solutions (All Failed)

1. **Time-based delays** - Added delays after mode switch
   - **Why it failed**: Timing is unpredictable, delays were either too short or too long

2. **View name detection** - Tried to detect login screens by view name
   - **Why it failed**: `GetMainModel()` returns "unknown" during login screen, can't detect view

3. **Skip first selection setting** - Added setting to skip first selection
   - **Why it failed**: Confusing UX, doesn't solve root cause, still plays during login

4. **ActiveView property change monitoring** - Subscribed to `OnMainModelPropertyChanged`
   - **Why it failed**: View changes happen but timing is unpredictable, can't reliably detect when library view is ready

5. **Library view detection** - Check for "Grid", "Details", "List" views
   - **Why it failed**: `GetMainModel()` fails during login screen, returns "unknown", so we can't detect if we're in a library view

### Current Implementation

**Location**: `UniPlaySong.cs` - `OnGameSelected()` method

**Current Logic**:
```csharp
// Check if we're in a library view
var isLibraryView = false;
if (isFullscreen)
{
    var fullscreenLibraryViews = new[] { "Grid", "Details", "List" };
    isLibraryView = fullscreenLibraryViews.Contains(activeViewName);
}
else
{
    isLibraryView = activeViewName == "Library";
}

// Only play if in library view
var shouldPlayMusic = !skipFirstSelect && isLibraryView;
```

**Problem**: When `GetMainModel()` returns null, `activeViewName` is "unknown", so `isLibraryView` is false, but `OnGameSelected` is still being called, and music may play anyway due to timing issues.

### Root Cause Analysis Needed

**Key Questions**:
1. Why does `GetMainModel()` return null/"unknown" during login screen?
2. Is `OnGameSelected` being called before the view is ready?
3. How does PlayniteSound actually prevent this issue?
4. Is there a different event or property we should be monitoring?
5. Does PlayniteSound have special handling for fullscreen mode transitions?
6. Is the issue that `OnGameSelected` fires too early, or that we're not detecting the view correctly?

### Investigation Steps for Future Agents

1. **Thoroughly review PlayniteSound's `OnGameSelected` implementation**
   - Read every line
   - Understand the exact flow
   - Note any conditions or checks

2. **Review PlayniteSound's `ShouldPlayAudio` method**
   - Understand all conditions that must be met
   - See if there are any view-related checks we're missing

3. **Check PlayniteSound's fullscreen mode handling**
   - Does it have special logic for fullscreen?
   - How does it handle mode switches?
   - Does it check anything we're not checking?

4. **Review PlayniteSound's `OnMainModelChanged` implementation**
   - How does it handle view changes?
   - Does it wait for something specific?
   - What triggers music playback?

5. **Review PlayniteSound's `_firstSelect` flag usage**
   - When is it set to true?
   - When is it set to false?
   - Is it ever reset?
   - How does it interact with view changes?

6. **Look for timing mechanisms in PlayniteSound**
   - Are there any delays, waits, or synchronization?
   - Does it use `Dispatcher.BeginInvoke` or similar?
   - Does it wait for specific events?

7. **Review ANIKI REMAKE theme source code**
   - When do views actually change?
   - What triggers the transition from login to library?
   - Is there an event we should be listening to?

8. **Consider alternative approaches**
   - Maybe we shouldn't use `OnGameSelected` for initial playback?
   - Maybe we need to wait for a different event?
   - Maybe we need to check something other than `ActiveView`?

### Success Criteria

The issue is fixed when:
- ✅ Music does NOT play during login screen transition
- ✅ Music DOES play when user reaches library view and selects first game
- ✅ Works reliably with ANIKI REMAKE theme
- ✅ Works reliably with default theme
- ✅ Works reliably when switching between desktop and fullscreen modes

---

## Documentation Structure

- **`README.md`** (root) - Project overview and quick start
- **`docs/IMPLEMENTATION_GUIDE.md`** - Technical implementation details, build process, common issues
- **`docs/DEVELOPMENT_NOTES.md`** - Development principles, known issues, debugging guidance
- **`docs/CRITICAL_ISSUES.md`** - This file - critical unresolved issues and investigation guidance
- **`docs/FILE_PATH_SELECTION_ISSUE.md`** - Detailed explanation of file path selection bug and fix

---

## Recent Changes Summary

### 2025-11-29: Settings Simplification
- Removed redundant "Auto-play music when selecting games" setting
- Simplified to single "Enable Music" master switch
- Updated all logic to only check `EnableMusic`

### 2025-11-29: Library View Detection
- Added check for library views before playing music
- Fullscreen: Checks for "Grid", "Details", "List"
- Desktop: Checks for "Library"
- **Note**: Still not working reliably - needs further investigation

### 2025-11-29: UI Cleanup
- Removed fullscreen-specific settings from UI
- Simplified settings interface
- Focus on core functionality

---

## Key Takeaways

1. **PlayniteSound is the reference** - Always check it first
2. **Keep it simple** - Don't add unnecessary complexity
3. **Login screen issue is unresolved** - Needs thorough investigation
4. **Review like a senior developer** - Understand root causes, not just symptoms
5. **Test incrementally** - Small changes, frequent testing

---

**Last Updated**: 2025-11-29
**Priority**: Fix login screen music issue is #1 priority

