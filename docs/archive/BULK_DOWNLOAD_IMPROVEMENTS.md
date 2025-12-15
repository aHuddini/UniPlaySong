# Bulk Download Improvements

## Overview

This document describes the improvements made to the UniPSong bulk download feature to address issues with auto-mode downloading irrelevant songs and add retry/correction capabilities for failed downloads.

## Improvements Made

### 1. YouTube Channel Whitelist System

**Problem**: Auto-mode was downloading random, irrelevant songs from YouTube playlists that weren't actual game soundtracks.

**Solution**: Implemented a YouTube channel whitelist system that filters playlists to only trusted sources.

#### Features

- **Channel Whitelisting**: Only playlists from pre-approved channels are considered for auto-selection
- **Pre-populated Whitelist**: Includes well-known game soundtrack channels:
  - GilvaSunner - High-quality game music rips
  - BrawlBRSTMs3 - Extensive video game music library
  - GiIvaSunner - Alternative variant
  - OST Composure - Video game soundtrack uploads

- **Configurable**: Can be enabled/disabled in settings
- **Manual Mode Unaffected**: Whitelist and strict filtering ONLY apply to auto-download mode
  - Manual searches (retry feature, single-game download) show ALL results without filtering
  - No channel whitelisting in manual mode
  - No strict word matching in manual mode
  - Users have full control to select any playlist manually

#### Technical Implementation

**Settings** ([UniPlaySongSettings.cs:295-318](UniPlaySongSettings.cs#L295-L318)):
```csharp
public bool EnableYouTubeChannelWhitelist { get; set; } = true;
public List<string> WhitelistedYouTubeChannelIds { get; set; }
```

**Album Model** ([Models/Album.cs:40-50](Models/Album.cs#L40-L50)):
- Added `ChannelId` and `ChannelName` properties to track YouTube playlist source

**YouTube Client** ([Downloaders/YouTubeClient.cs:30-31](Downloaders/YouTubeClient.cs#L30-L31)):
- Extracts channel information from YouTube API responses
- Parses channel ID and name from playlist metadata

**Download Manager** ([Downloaders/DownloadManager.cs:338-363](Downloaders/DownloadManager.cs#L338-L363)):
- Filters albums by channel ID during auto-selection
- Logs whitelist checks for debugging

### 2. Improved Auto-Mode Filtering

**Problem**: 50% word matching was too lenient, allowing unrelated playlists through.

**Solution**: Increased word matching threshold and improved filtering logic.

#### Changes

**Stricter Word Matching** ([Downloaders/DownloadManager.cs:365-384](Downloaders/DownloadManager.cs#L365-L384)):
- Increased from 50% to 66% word match requirement for multi-word games
- Single-word games still require 100% match
- Better detection of irrelevant content (trailers, gameplay videos, etc.)

**Enhanced Rejection Keywords**:
```csharp
var rejectKeywords = new[]
{
    "[eng sub]", "[sub]", "episode", "drama", "movie", "film",
    "trailer", "review", "gameplay", "walkthrough", "let's play",
    "reaction", "cover", "remix", "fan made", "fanmade"
};
```

### 3. Failed Download Retry System

**Problem**: No way to correct failed downloads - users had to manually search again.

**Solution**: Implemented a comprehensive failed download tracking and retry system.

#### Features

**Automatic Tracking**:
- All failed batch downloads are automatically tracked
- Records game, failure reason, and timestamp
- Removes successfully resolved entries

**Retry Interface** ([Menus/GameMenuHandler.cs:842-961](Menus/GameMenuHandler.cs#L842-L961)):
- `RetryFailedDownloads()` - Shows list of failed downloads and allows retry
- Interactive retry with manual source/album/song selection
- Summary report after retry completion
- `ClearFailedDownloads()` - Clears the failed downloads list

**User Flow**:
1. Batch download runs and tracks any failures
2. User calls "Retry Failed Downloads" menu item
3. System shows list of failed games
4. User manually selects source (KHInsider/YouTube) for each game
5. User manually selects album from search results
6. User manually selects songs to download
7. System tracks resolved downloads and shows summary

#### Technical Implementation

**FailedDownload Model** ([Models/FailedDownload.cs](Models/FailedDownload.cs)):
```csharp
public class FailedDownload
{
    public Game Game { get; set; }
    public string FailureReason { get; set; }
    public DateTime FailedAt { get; set; }
    public bool Resolved { get; set; }
}
```

**Tracking in GameMenuHandler** ([Menus/GameMenuHandler.cs:818-836](Menus/GameMenuHandler.cs#L818-L836)):
- `TrackFailedDownload()` - Records failures
- Automatic cleanup of resolved entries
- Integration with batch download process

## Usage Guide

### Configuring YouTube Whitelist

1. Open UniPSong settings in Playnite
2. Navigate to YouTube channel whitelist section
3. Enable/disable whitelist as needed
4. Add/remove channel IDs from the list

**Finding Channel IDs**:
1. Open a YouTube playlist from a reliable channel
2. Right-click the channel name → "Copy channel URL"
3. Extract the ID from the URL (e.g., `UC9l8PCqbv1x7qwU_1oiSR3A`)
4. Add to whitelist in settings

### Using the Retry Feature

The retry feature is available in multiple ways:

**Automatic Prompt After Batch Download** (Recommended):
1. Run a batch download that results in failures
2. After the summary dialog, you'll be prompted: "Would you like to retry the X failed download(s) now?"
3. Click "Yes" to immediately start the retry process
4. Click "No" to defer retry for later

**Via Main Menu**:
1. Open Playnite's main menu (Extensions or top menu bar)
2. Look for "Retry Failed Downloads (X)" - only appears when failures exist
3. Click to start the retry process

**Via Game Context Menu** (when multiple games are selected):
1. Select multiple games in Playnite
2. Right-click → UniPSong → "Retry Failed Downloads (X)"
3. This appears below the Download All options

**Retry Process**:
1. Review the list of failed games shown in the dialog
2. Click "Yes" to start retry process
3. For each failed game:
   - Dialog shows: "Find Music for: [Game Name]"
   - Unified search automatically runs on both KHInsider AND YouTube
   - All results shown together with source indicated (e.g., "[YouTube]" or "[KHInsider]")
   - Select the correct album from the combined results
   - Select songs to download
4. Review summary after completion

**Notes**:
- The menu item shows the count of unresolved failures in parentheses
- Menu item only appears when there are failed downloads to retry
- Failed downloads are automatically cleared when successfully resolved
- Failures persist only during the current session (not saved between restarts)

### Auto-Download Best Practices

**For Best Results**:
1. Enable YouTube channel whitelist
2. Populate whitelist with trusted channels
3. Run batch download with auto-mode
4. Review failed downloads
5. Use retry feature to manually correct failures

**Whitelist Management**:
- Start with the default whitelist
- Add channels as you discover reliable sources
- Remove channels that produce poor results
- Whitelist only applies to auto-mode (manual mode shows all)

## Configuration Examples

### Strict Filtering (Recommended)
```csharp
EnableYouTubeChannelWhitelist = true
WhitelistedYouTubeChannelIds = [
    "UCfSN UCFt4IRa-lKRYUhvEg",  // GilvaSunner
    "UC9l8PCqbv1x7qwU_1oiSR3A",  // BrawlBRSTMs3
    // ... more channels
]
```

### Lenient Filtering (More Results, Less Accurate)
```csharp
EnableYouTubeChannelWhitelist = false
```

## Architecture Changes

### Modified Files

1. **UniPlaySongSettings.cs**
   - Added `EnableYouTubeChannelWhitelist` property
   - Added `WhitelistedYouTubeChannelIds` list with defaults

2. **Models/Album.cs**
   - Added `ChannelId` property
   - Added `ChannelName` property

3. **Models/FailedDownload.cs** (NEW)
   - New model for tracking failed downloads

4. **Downloaders/YouTubeClient.cs**
   - Added channel ID/name parsing from API responses
   - Updated `YouTubeItem` model with channel properties

5. **Downloaders/YouTubeDownloader.cs**
   - Maps channel data to Album objects

6. **Downloaders/DownloadManager.cs**
   - Added `_settings` field for whitelist access
   - Updated `IsLikelyGameMusic()` with channel whitelist check
   - Increased word matching threshold from 50% to 66%

7. **Menus/GameMenuHandler.cs**
   - Added `_failedDownloads` tracking list
   - Added `TrackFailedDownload()` method
   - Added `RetryFailedDownloads()` method
   - Added `ClearFailedDownloads()` method
   - Integrated tracking into batch download flow

8. **UniPlaySong.cs**
   - Updated DownloadManager initialization to pass settings

### Data Flow

```
User initiates batch download
    ↓
DownloadManager.GetAlbumsForGame()
    ↓
YouTubeClient.Search() → extracts channel data
    ↓
DownloadManager.IsLikelyGameMusic()
    ├─ Check music keywords
    ├─ Check rejection keywords
    └─ For YouTube:
        ├─ Check channel whitelist (if enabled)
        └─ Check word matching (66% threshold)
    ↓
DownloadManager.BestAlbumPick()
    ↓
[Download process]
    ↓
If failure → TrackFailedDownload()
    ↓
User calls RetryFailedDownloads()
    ↓
Manual selection dialogs
    ↓
Track resolution or new failure
```

## Benefits

### For Users
- **More Accurate Results**: Whitelist ensures only reliable sources
- **Fewer False Positives**: Stricter filtering reduces irrelevant downloads
- **Easy Correction**: Retry feature allows manual correction of mistakes
- **No Repeated Work**: Failed downloads are tracked and can be batch-retried

### For Developers
- **Extensible Whitelist**: Easy to add new trusted channels
- **Clear Separation**: Auto-mode vs manual-mode filtering
- **Debugging Support**: Comprehensive logging of filter decisions
- **Failure Tracking**: Built-in retry infrastructure

## Future Enhancements

### Potential Improvements
1. **Automatic Channel Discovery**: Learn from user manual selections
2. **Whitelist Sharing**: Export/import whitelists between users
3. **Channel Reputation**: Track success rates per channel
4. **Advanced Retry Options**: Batch retry with filters
5. **Persistent Failure History**: Save across sessions
6. **Smart Suggestions**: Recommend similar albums on failure

### API Considerations
- YouTube API may change - monitor for breakage
- Channel ID extraction depends on JSON structure
- Consider adding fallback parsing strategies

## Testing

### Manual Testing Scenarios

**Test 1: Channel Whitelist Filtering**
1. Enable whitelist with one channel
2. Run auto-download for a game
3. Verify only playlists from that channel are considered
4. Check logs for rejection messages

**Test 2: Stricter Word Matching**
1. Search for a game with multiple words (e.g., "Final Fantasy VII")
2. Verify playlists with <66% word match are rejected
3. Check that relevant playlists still pass

**Test 3: Failed Download Retry**
1. Run batch download with intentionally failing games
2. Verify failures are tracked
3. Call RetryFailedDownloads()
4. Manually correct each failure
5. Verify tracking is cleared on success

**Test 4: Whitelist Disabled**
1. Disable whitelist in settings
2. Run auto-download
3. Verify all playlists are shown (no channel filtering)

## Troubleshooting

### Issue: No Albums Found with Whitelist Enabled

**Cause**: Channel not in whitelist

**Solution**:
1. Temporarily disable whitelist
2. Search manually and find good playlist
3. Note the channel ID
4. Add to whitelist
5. Re-enable whitelist

### Issue: Still Getting Irrelevant Results

**Possible Causes**:
- Whitelist includes unreliable channel
- Game name doesn't match album name well
- Rejection keywords need updating

**Solutions**:
- Review and prune whitelist
- Use manual mode for problematic games
- Check logs for filter decisions
- Consider adding more rejection keywords

### Issue: Retry Feature Not Showing Failures

**Cause**: Failures not tracked or already resolved

**Solution**:
- Verify batch download actually failed (check logs)
- Ensure GameMenuHandler is properly initialized
- Check if failures were auto-resolved on subsequent download

## Conclusion

These improvements significantly enhance the reliability and usability of the bulk download feature by:
1. Filtering out low-quality YouTube playlists via channel whitelist
2. Applying stricter word matching to reduce false positives
3. Providing an easy way to manually correct failed downloads
4. Tracking failures for better user awareness

The system now provides a good balance between automation and user control, with the auto-mode being more accurate and the retry feature allowing easy manual correction when needed.
