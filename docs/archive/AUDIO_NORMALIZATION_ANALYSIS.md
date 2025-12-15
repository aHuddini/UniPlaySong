# Audio Normalization - Implementation Analysis

**Date**: 2025-12-14  
**Status**: Analysis Complete - Ready for Implementation  
**Feature Request**: Bulk audio normalization in settings menu

## 🎯 **Overview**

Users want a bulk audio normalization feature accessible from the add-on settings menu. This would normalize all audio files to consistent volume levels, preventing jarring volume jumps when switching between games.

## 🔍 **Technical Options Analysis**

### **Option 1: FFmpeg Loudnorm Filter (Recommended)** ✅

**How It Works**:
- Uses `ffmpeg` with the `loudnorm` filter
- EBU R128 loudness standard compliance
- Two-pass process for accuracy:
  1. **First pass**: Analyzes audio to measure integrated loudness (IL), loudness range (LRA), true peak (TP)
  2. **Second pass**: Applies normalization using measurements from first pass

**Advantages**:
- ✅ **No additional dependencies** - We already require ffmpeg for YouTube downloads
- ✅ **Industry standard** - EBU R128 compliant
- ✅ **High quality** - Professional-grade normalization
- ✅ **Direct integration** - Call ffmpeg process directly from C#
- ✅ **Configurable** - Can customize target loudness, true peak limits, etc.

**Disadvantages**:
- ⚠️ **Two-pass required** - Slower (each file processed twice)
- ⚠️ **Complex implementation** - Need to parse first-pass output and feed to second pass

**FFmpeg Command**:
```bash
# First pass (analysis)
ffmpeg -i input.mp3 -af loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json -f null -

# Second pass (apply normalization using first pass measurements)
ffmpeg -i input.mp3 -af loudnorm=I=-16:TP=-1.5:LRA=11:measured_I=-15.2:measured_LRA=9.5:measured_TP=-1.1:measured_thresh=-25.0:offset=0.8:linear=true -ar 44100 output.mp3
```

**Implementation Complexity**: Medium (need to parse JSON output from first pass)

---

### **Option 2: FFmpeg-Normalize Python Tool** 

**How It Works**:
- Python wrapper around ffmpeg's loudnorm filter
- Automates the two-pass process
- Handles measurement extraction and application internally

**Advantages**:
- ✅ **Simpler to use** - Handles two-pass automatically
- ✅ **User-friendly** - Cleaner API than raw ffmpeg
- ✅ **Battle-tested** - Used by PlayniteSound

**Disadvantages**:
- ❌ **Python required** - Users need Python installed
- ❌ **Additional dependency** - Need to check for ffmpeg-normalize tool
- ❌ **Installation complexity** - Users must install Python + ffmpeg-normalize
- ❌ **Path management** - Need to configure ffmpeg-normalize path in settings

**Installation Requirements**:
```bash
pip install ffmpeg-normalize
```

**FFmpeg-Normalize Command**:
```bash
ffmpeg-normalize input.mp3 -o output.mp3 -f -c:a libmp3lame
```

**PNS Default Args**: `-lrt 20 -c:a libmp3lame` (target -20 LUFS, libmp3lame codec)

**Implementation Complexity**: Low (but requires Python dependency)

---

### **Option 3: YT-DLP Normalization** ❌

**Why Not Suitable**:
- ❌ **Only works during download** - Cannot normalize existing files
- ❌ **Not for bulk operations** - Only applies to new downloads
- ✅ Can be used for future downloads: `--postprocessor-args "-af loudnorm=I=-16:TP=-1"`

**Use Case**: Good for normalizing new downloads, but doesn't solve the bulk normalization need for existing files.

---

### **Option 4: Simpler FFmpeg Normalization**

**Alternatives**:
- **Volume normalization**: `-af "volume=0dB"` (simple, but doesn't account for perceived loudness)
- **Peak normalization**: `-af "volume=1.0"` (normalize to 0dB peak, but inconsistent perceived loudness)
- **Dynaudnorm**: `-af dynaudnorm` (dynamic normalization, single-pass, less accurate than loudnorm)

**Why Not Recommended**:
- ⚠️ Less accurate than loudnorm
- ⚠️ Doesn't follow industry standards
- ⚠️ May not produce consistent results across different audio types

---

## 🎯 **Recommendation: FFmpeg Loudnorm Direct Implementation**

### **Why This Approach**:
1. **No additional dependencies** - Users already have ffmpeg for downloads
2. **Professional quality** - Industry-standard EBU R128 compliance
3. **Consistent with downloads** - Same tool chain
4. **Full control** - Can customize normalization parameters

### **Implementation Strategy**

#### **Phase 1: Basic Normalization**
- Use simpler single-pass loudnorm initially
- `-af loudnorm=I=-16:TP=-1.5:LRA=11:linear=true`
- Fast, but less accurate than two-pass

#### **Phase 2: Advanced Two-Pass (Optional Enhancement)**
- Implement proper two-pass normalization
- Parse JSON output from first pass
- Feed measurements into second pass
- More accurate, but slower

### **Settings Menu Design**

**New Settings Tab**: "Audio Normalization"

**UI Elements**:
```
[Audio Normalization Tab]

[✓] Enable Audio Normalization
    Normalize all audio files to consistent volume levels
    (EBU R128 standard, target: -16 LUFS)

Normalization Settings:
  Target Loudness: [-16] LUFS (Industry Standard)
  True Peak Limit: [-1.5] dBTP
  Loudness Range: [11] LU

[Advanced Options ▼]
  Normalization Method: [●] Single-pass (Fast) [ ] Two-pass (Accurate)
  Audio Codec: [libmp3lame ▼]
  Preserve Original Files: [✓] (create .normalized backup)

[Actions]
  [Normalize All Music Files]  [Normalize Selected Games...]
  [Normalize by Source...]     [Test Normalization...]
```

### **Implementation Details**

#### **Core Normalization Service**
```csharp
public class AudioNormalizationService
{
    public bool NormalizeFile(string filePath, NormalizationSettings settings)
    {
        // Single-pass loudnorm
        var args = $"-i \"{filePath}\" -af loudnorm=I={settings.TargetLoudness}:TP={settings.TruePeak}:LRA={settings.LoudnessRange}:linear=true -ar 44100 -c:a {settings.Codec} \"{filePath}.tmp\"";
        
        // Run ffmpeg
        // Replace original if successful
        // Handle errors
    }
    
    public void NormalizeBulk(IEnumerable<string> files, IProgress<NormalizationProgress> progress)
    {
        // Process files in parallel or sequential
        // Update progress
        // Handle failures
    }
}
```

#### **Settings Integration**
- Add normalization settings to `UniPlaySongSettings`
- Add settings UI tab in `UniPlaySongSettingsView`
- Store normalization status per file (to avoid re-normalizing)

#### **Bulk Operations**
- Normalize all files: Process entire music library
- Normalize selected games: Right-click menu option
- Normalize by source: Filter by KHInsider/YouTube
- Progress dialog with cancellation support
- Controller-friendly progress dialog option

### **Default Settings**
- **Target Loudness**: -16 LUFS (EBU R128 standard)
- **True Peak**: -1.5 dBTP
- **Loudness Range**: 11 LU
- **Codec**: libmp3lame (maintains MP3 format)
- **Method**: Single-pass (can upgrade to two-pass later)

### **Error Handling**
- Missing ffmpeg: Clear error message with instructions
- Unsupported formats: Skip with warning
- File in use: Queue for retry
- Normalization failures: Log and continue
- Progress preservation: Save progress to resume if cancelled

### **Performance Considerations**
- **Sequential processing**: Safer, easier error handling
- **Parallel processing**: Faster, but more complex (optional enhancement)
- **Progress tracking**: Show current file, ETA, success/failure counts
- **Cancellation**: Support user cancellation mid-process

---

## 📊 **Comparison Matrix**

| Feature | FFmpeg Loudnorm | FFmpeg-Normalize | YT-DLP | Simple Volume |
|---------|----------------|------------------|--------|---------------|
| **Dependencies** | ✅ None (use existing ffmpeg) | ❌ Python + tool | ❌ Only for new downloads | ✅ None |
| **Quality** | ✅ Excellent (EBU R128) | ✅ Excellent (EBU R128) | ✅ Excellent | ⚠️ Poor |
| **Bulk Operations** | ✅ Yes | ✅ Yes | ❌ No | ✅ Yes |
| **Implementation** | Medium complexity | Low complexity | ❌ Not applicable | ✅ Simple |
| **User Setup** | ✅ Already have ffmpeg | ❌ Need Python | ❌ Not applicable | ✅ Already have ffmpeg |
| **Accuracy** | ✅ High (2-pass) | ✅ High (2-pass) | ✅ High | ⚠️ Low |
| **Speed** | Medium (2-pass) | Medium (2-pass) | Fast | ✅ Fast |

---

## 🎯 **Final Recommendation**

**Use FFmpeg Loudnorm Direct Implementation** (Option 1)

**Rationale**:
1. ✅ No additional user dependencies (they already have ffmpeg)
2. ✅ Professional quality normalization
3. ✅ Consistent with our existing download workflow
4. ✅ Full control over normalization parameters
5. ✅ Can start with single-pass, upgrade to two-pass later

**Implementation Plan**:
1. Create `AudioNormalizationService` class
2. Add normalization settings to `UniPlaySongSettings`
3. Create settings UI tab for normalization
4. Implement single-pass loudnorm normalization
5. Add bulk normalization operations
6. Add progress tracking and error handling
7. (Future) Optional two-pass implementation for maximum accuracy

**User Experience**:
- Settings menu has new "Audio Normalization" tab
- Simple toggle to enable/disable
- Bulk normalize button processes all files
- Progress dialog shows current status
- Can normalize specific games via menu
- Controller-friendly progress dialog option

---

## 🔧 **Technical Implementation Notes**

### **FFmpeg Integration**
- Reuse existing `_ffmpegPath` from settings
- Use `Process.StartInfo` to call ffmpeg
- Parse output for errors and progress
- Handle temporary files safely (create .tmp, replace on success)

### **File Management**
- Check file extension (MP3, WAV, FLAC, etc.)
- Preserve original files (optional setting)
- Handle file locking (ensure not in use)
- Update normalization status tracking

### **Settings Storage**
- Add `EnableNormalization` boolean
- Add `NormalizationTargetLoudness` (-16 default)
- Add `NormalizationTruePeak` (-1.5 default)
- Add `NormalizationLoudnessRange` (11 default)
- Add `PreserveOriginalFiles` boolean
- Store normalization metadata per file (JSON or database)

---

**Status**: Ready for implementation once approved. This approach provides professional-quality normalization without requiring additional user dependencies beyond ffmpeg, which is already required for YouTube downloads.