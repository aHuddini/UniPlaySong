# Audio Normalization Verification Guide

## How to Verify Normalization is Working

Normalization may not always be perceptually obvious, especially if files were already close to the target loudness. Here are several ways to verify that normalization is actually working:

---

## 1. **Check the Log File**

The extension logs detailed normalization information for each file:

**Log Location**: `%APPDATA%\Playnite\Extensions\UniPlaySong*\UniPlaySong.log`

**What to Look For**:
- `Before normalization - File: [filename], Measured I: [value] LUFS, Target: -16.0 LUFS`
- `Normalization completed - File: [filename], Target achieved: -16.0 LUFS`

**Example Log Entry**:
```
Before normalization - File: song.mp3, Measured I: -23.456 LUFS, Target: -16.0 LUFS
Normalization completed - File: song.mp3, Target achieved: -16.0 LUFS
```

If you see a significant difference between the "Measured I" value and the target (-16.0 LUFS), normalization should have changed the file.

---

## 2. **Use FFmpeg to Check Loudness Values**

You can use FFmpeg directly to verify the loudness of a normalized file:

### Check Normalized File:
```bash
ffmpeg -i "song-normalized.mp3" -af loudnorm=I=-16:print_format=summary -f null -
```

Look for the `input_i` value in the output. It should be close to -16.0 LUFS (your target).

### Compare Before/After:
1. If you preserved originals, check the original:
   ```bash
   ffmpeg -i "PreservedOriginals/[GameName]/song.mp3" -af loudnorm=I=-16:print_format=summary -f null -
   ```
2. Check the normalized version:
   ```bash
   ffmpeg -i "[GameMusicFolder]/song-normalized.mp3" -af loudnorm=I=-16:print_format=summary -f null -
   ```
3. Compare the `input_i` values - they should be different if normalization worked.

---

## 3. **Perceptual Volume Comparison**

The easiest way to verify normalization is working:

1. **Play Original Files**: If preserved, play a few original files from different games
   - Note the volume differences between tracks
   
2. **Play Normalized Files**: Play the normalized versions
   - Volume should be more consistent across tracks
   - Quieter tracks should sound louder
   - Louder tracks should sound quieter

3. **A/B Comparison**: Switch between original and normalized versions of the same track
   - If the original was quieter, the normalized version should sound louder
   - If the original was louder, the normalized version should sound quieter

---

## 4. **File Size Comparison**

Sometimes normalization can change file sizes slightly:
- Different bitrate or encoding parameters
- Re-encoding process may optimize differently
- Small size changes are normal

This isn't a reliable indicator, but significant size changes suggest the file was processed.

---

## 5. **Verify with Audio Analysis Tools**

### Using Audacity (Free):
1. Open both original and normalized files
2. Analyze → Contrast Analysis
3. Compare the loudness measurements
4. Normalized file should show values closer to -16 LUFS

### Using FFmpeg EBU R128 Analysis:
```bash
# Get detailed loudness report
ffmpeg -i "song-normalized.mp3" -af loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json -f null - 2>&1 | findstr /C:"input_i"
```

Look for `"input_i": "-16.0"` or close to it in the JSON output.

---

## Expected Results

After normalization:
- ✅ All files should have similar perceived loudness
- ✅ No need to adjust volume when switching between games
- ✅ Quiet tracks are louder
- ✅ Loud tracks are quieter
- ✅ Consistent volume level across entire library

---

## Troubleshooting

### "Normalization completed but I don't hear a difference"

**Possible Reasons**:
1. **File was already close to target**: If original was -17 LUFS and target is -16 LUFS, the change is small
2. **Your speakers/headphones**: Some audio systems have auto-leveling that masks differences
3. **Perception**: Small changes in LUFS (< 3 LU) may not be immediately noticeable

**Solution**: Check the log file to see the actual before/after measurements.

### "Normalization failed"

**Check**:
1. FFmpeg path is correct in settings
2. FFmpeg is accessible and working (`ffmpeg -version` in command prompt)
3. File isn't corrupted or in use by another program
4. Check log file for specific error messages

---

## Technical Details

### EBU R128 Standard
- **Target Integrated Loudness**: -16 LUFS (industry standard)
- **True Peak Limit**: -1.5 dBTP (prevents clipping)
- **Loudness Range**: 11 LU (preserves dynamic range)

### What LUFS Means
- **LUFS** = Loudness Units relative to Full Scale
- Lower values = quieter
- Higher values = louder
- -16 LUFS is the broadcast standard for consistent volume

### Typical Changes
- Very quiet file (-30 LUFS) → Normalized (-16 LUFS): **+14 LU change** = Very noticeable
- Quiet file (-22 LUFS) → Normalized (-16 LUFS): **+6 LU change** = Noticeable  
- Average file (-18 LUFS) → Normalized (-16 LUFS): **+2 LU change** = Subtle
- Loud file (-12 LUFS) → Normalized (-16 LUFS): **-4 LU change** = Noticeable

---

## Quick Verification Checklist

- [ ] Check log file for before/after measurements
- [ ] Compare original vs normalized file loudness with FFmpeg
- [ ] Listen to normalized files - volume should be more consistent
- [ ] A/B test original vs normalized versions
- [ ] Verify multiple files from different games have similar loudness

---

**Note**: Normalization affects perceived loudness, not just peak volume. Two files can have the same peak volume but different perceived loudness. The loudnorm filter corrects for this by analyzing the entire audio content and adjusting to match the target integrated loudness (LUFS).