# Audio Player Library Comparison: WPF MediaPlayer vs SDL2 vs CSCore

## Problem Statement
WPF MediaPlayer has persistent fade-in issues in fullscreen mode, likely due to:
- Clock creation resetting volume
- WPF threading/dispatcher complexity
- Fullscreen mode volume control inconsistencies

## Library Comparison

### 1. WPF MediaPlayer (Current Implementation)

**Pros:**
- ✅ Built into WPF (no external dependencies)
- ✅ Simple API
- ✅ Works in desktop mode
- ✅ No native DLLs required

**Cons:**
- ❌ **Volume control issues in fullscreen** (current problem)
- ❌ Clock creation may reset volume unexpectedly
- ❌ Threading/dispatcher complexity
- ❌ Limited control over playback
- ❌ Windows-only

**Volume Control:**
```csharp
// Direct but unreliable in fullscreen
_mediaPlayer.Volume = value;  // May be reset by Clock creation
```

**Implementation Complexity:** Low (but unreliable)

---

### 2. SDL2 (PlayniteSound's Primary Choice)

**Pros:**
- ✅ **Native library - no WPF threading issues**
- ✅ **Reliable volume control** (direct C API)
- ✅ **Works consistently in fullscreen**
- ✅ Cross-platform (Windows, Linux, macOS)
- ✅ Battle-tested (used by PlayniteSound)
- ✅ Good performance
- ✅ Preloading support (dual-player system)

**Cons:**
- ❌ Requires native DLLs (`SDL2.dll`, `SDL2_mixer.dll`)
- ❌ P/Invoke complexity
- ❌ Need to bundle DLLs with extension
- ❌ More setup required

**Volume Control:**
```csharp
// Direct, reliable, synchronous
_volume = Math.Max(0.0, Math.Min(1.0, value));
int mixerVolume = (int)(_volume * 128);
SDL2Mixer.Mix_VolumeMusic(mixerVolume);  // Immediate, no threading issues
```

**Implementation Complexity:** Medium (P/Invoke wrapper needed)

**Dependencies Required:**
- `SDL2.dll` (core library)
- `SDL2_mixer.dll` (audio mixer)
- P/Invoke wrapper classes (`SDL.cs`, `SDL_mixer.cs`)

**PlayniteSound Implementation:**
- Uses `SDL2MusicPlayer` as primary player
- Falls back to WMP only if WMP Legacy App is installed
- Volume control is direct and synchronous
- No dispatcher/threading issues

---

### 3. CSCore

**Pros:**
- ✅ Pure .NET library (no native dependencies)
- ✅ More control over audio processing
- ✅ Supports many formats (MP3, WAVE, FLAC, AAC, etc.)
- ✅ Real-time audio processing capabilities
- ✅ Extensible architecture
- ✅ Active development

**Cons:**
- ❌ Larger dependency (more code)
- ❌ More complex API
- ❌ Might be overkill for simple playback
- ❌ Primarily Windows-focused (experimental Linux/macOS)
- ❌ Less battle-tested in Playnite context

**Volume Control:**
```csharp
// More control, but more complex
// Uses audio stream processing
// Can apply effects, filters, etc.
```

**Implementation Complexity:** Medium-High (more features = more complexity)

**Dependencies Required:**
- NuGet package: `CSCore` (pure .NET, no DLLs)

---

## Detailed Comparison Table

| Feature | WPF MediaPlayer | SDL2 | CSCore |
|---------|---------------|------|--------|
| **Dependencies** | None (built-in) | Native DLLs | NuGet package |
| **Volume Control Reliability** | ❌ Unreliable (fullscreen) | ✅ Reliable | ✅ Reliable |
| **Threading Issues** | ❌ Yes (dispatcher) | ✅ No | ✅ No |
| **Fullscreen Compatibility** | ❌ Issues | ✅ Works | ✅ Works |
| **Preloading Support** | ⚠️ Complex | ✅ Built-in | ⚠️ Manual |
| **Cross-Platform** | ❌ Windows only | ✅ Yes | ⚠️ Windows primary |
| **Performance** | ⚠️ Good | ✅ Excellent | ✅ Excellent |
| **API Complexity** | ✅ Simple | ⚠️ Medium | ❌ Complex |
| **Battle-Tested in Playnite** | ⚠️ Partial | ✅ Yes (PNS) | ❌ No |
| **Setup Complexity** | ✅ None | ⚠️ Medium | ⚠️ Medium |
| **File Format Support** | ⚠️ Limited | ✅ Good | ✅ Excellent |

---

## Recommendation: SDL2

### Why SDL2?

1. **Proven in Playnite Context**
   - PlayniteSound uses SDL2 as primary player
   - Already battle-tested with Playnite extensions
   - Known to work reliably in fullscreen

2. **Reliable Volume Control**
   - Direct C API calls (no WPF threading)
   - Synchronous volume changes
   - No Clock creation issues

3. **Simple Integration**
   - PlayniteSound's implementation can be adapted
   - P/Invoke wrappers already exist
   - Clear, straightforward API

4. **Performance**
   - Native library = better performance
   - Lower overhead than WPF MediaPlayer
   - Efficient preloading system

### Implementation Plan for SDL2

1. **Copy SDL wrapper files from PlayniteSound:**
   - `SDL.cs` (P/Invoke declarations)
   - `SDL_mixer.cs` (Audio mixer P/Invoke)

2. **Copy SDL2MusicPlayer implementation:**
   - Adapt `SDL2MusicPlayer.cs` to our `IMusicPlayer` interface
   - Implement preloading system
   - Volume control is already reliable

3. **Bundle native DLLs:**
   - Include `SDL2.dll` and `SDL2_mixer.dll` in extension package
   - Place in extension root or `lib/` folder
   - Ensure proper loading path

4. **Update MusicPlayer factory:**
   - Create `SDL2MusicPlayer` instance
   - Keep WPF MediaPlayer as fallback (optional)

5. **Test:**
   - Verify fade-in works in fullscreen
   - Test volume control reliability
   - Test preloading system

### Why Not CSCore?

1. **Overkill for Our Needs**
   - We just need playback + volume control
   - CSCore offers much more (processing, effects, etc.)
   - More complexity than needed

2. **Less Proven**
   - Not used by PlayniteSound
   - Unknown compatibility with Playnite
   - Less community support for Playnite context

3. **Larger Dependency**
   - More code to maintain
   - Larger extension package
   - More potential issues

---

## Migration Path: WPF MediaPlayer → SDL2

### Step 1: Add SDL Wrapper Files
- Copy `SDL.cs` and `SDL_mixer.cs` from PlayniteSound
- Place in `UniPSong/Players/SDL/` directory

### Step 2: Create SDL2MusicPlayer
- Implement `IMusicPlayer` interface
- Copy/adapt PlayniteSound's `SDL2MusicPlayer.cs`
- Ensure volume control matches our fader needs

### Step 3: Bundle Native DLLs
- Download SDL2 binaries (x64 for Windows)
- Place `SDL2.dll` and `SDL2_mixer.dll` in extension package
- Update `package_extension.ps1` to include DLLs

### Step 4: Update MusicPlayer Factory
- Create `SDL2MusicPlayer` as primary
- Keep WPF MediaPlayer as fallback (optional)

### Step 5: Test & Verify
- Fade-in should work reliably in fullscreen
- Volume control should be immediate and consistent
- Preloading should work seamlessly

---

## Code Example: SDL2 Volume Control

```csharp
public double Volume
{
    get => _volume;
    set
    {
        _volume = Math.Max(0.0, Math.Min(1.0, value));
        int mixerVolume = (int)(_volume * 128);  // SDL2 uses 0-128 range
        SDL2Mixer.Mix_VolumeMusic(mixerVolume);  // Immediate, synchronous
    }
}
```

**Key Advantage:** No threading issues, no Clock creation problems, works reliably in fullscreen.

---

## Conclusion

**SDL2 is the recommended choice** because:
1. ✅ Proven to work in Playnite (PlayniteSound uses it)
2. ✅ Reliable volume control (no WPF threading issues)
3. ✅ Works consistently in fullscreen
4. ✅ Simple integration (can adapt PlayniteSound's code)
5. ✅ Better performance than WPF MediaPlayer

**Next Steps:**
1. Review PlayniteSound's SDL2 implementation
2. Copy/adapt SDL wrapper files
3. Create SDL2MusicPlayer class
4. Bundle native DLLs
5. Test fade-in in fullscreen

