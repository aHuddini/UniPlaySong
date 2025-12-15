# Testing Guide for Bulk Download Improvements

## Quick Test Checklist

### 1. YouTube Channel Whitelist

**Test: Whitelist Enabled (Default)**
- [ ] Run batch download with auto-mode on a game
- [ ] Check logs for "whitelist check passed" messages
- [ ] Verify only playlists from whitelisted channels are selected
- [ ] Check logs for "rejected: Not in whitelist" for non-whitelisted channels

**Test: Whitelist Disabled**
- [ ] Disable `EnableYouTubeChannelWhitelist` in settings
- [ ] Run batch download with auto-mode
- [ ] Verify more playlists are shown (no channel filtering)
- [ ] No whitelist rejection messages in logs

**Test: Manual Mode Not Affected**
- [ ] With whitelist enabled, use manual mode (right-click game → Download Music)
- [ ] Select YouTube as source
- [ ] Verify ALL playlists are shown (not filtered by whitelist)
- [ ] Whitelist should only affect auto-mode

### 2. Stricter Word Matching

**Test: 66% Threshold**
- [ ] Create a game with multi-word name (e.g., "Final Fantasy VII")
- [ ] Run auto-download from YouTube
- [ ] Verify albums with <66% word match are rejected
- [ ] Check logs for "Insufficient word match" messages
- [ ] Confirm relevant albums still pass

**Test: Single-Word Games**
- [ ] Test with single-word game name (e.g., "Skyrim")
- [ ] Verify 100% word match still required
- [ ] Albums without the game name are rejected

### 3. Failed Download Tracking

**Test: Failures Are Tracked**
- [ ] Run batch download on games that will fail (no music available)
- [ ] Check that failures are tracked in `_failedDownloads` list
- [ ] Verify each failure has:
  - Game reference
  - Failure reason
  - Timestamp
  - Resolved = false

**Test: Menu Visibility**
- [ ] After failed downloads, check main menu
- [ ] Verify "Retry Failed Downloads (X)" appears
- [ ] Count should match number of failures
- [ ] Menu item should NOT appear if no failures

**Test: Multi-Game Context Menu**
- [ ] Select multiple games (2+)
- [ ] Right-click → UniPSong
- [ ] If failures exist, verify "Retry Failed Downloads (X)" appears
- [ ] Menu item should be below the separator after Download All options

### 4. Retry Functionality

**Test: Basic Retry Flow**
- [ ] Trigger failed downloads (batch download on games with no music)
- [ ] Click "Retry Failed Downloads" from main menu or context menu
- [ ] Dialog shows list of failed games (up to 10, with "... and X more" if >10)
- [ ] Click "Yes" to start retry
- [ ] For each game:
  - [ ] Source selection dialog appears
  - [ ] Album selection dialog appears with search results
  - [ ] Song selection dialog appears
  - [ ] Download proceeds
- [ ] Summary dialog shows:
  - [ ] Attempted count
  - [ ] Succeeded count
  - [ ] Remaining failures count

**Test: Cancellation**
- [ ] Start retry process
- [ ] Click "No" in initial confirmation → retry aborts
- [ ] Start retry, cancel in source selection → retry stops, no changes
- [ ] Start retry, cancel in album selection → skips to next game

**Test: Resolution Tracking**
- [ ] Complete retry successfully for a game
- [ ] Verify that game is removed from failed downloads list
- [ ] Menu item count decreases
- [ ] Re-trigger batch download on same game → failure count doesn't increase

**Test: Partial Success**
- [ ] Retry multiple failures
- [ ] Successfully fix some, fail/skip others
- [ ] Summary should show accurate counts
- [ ] Only resolved failures should be removed from list

### 5. Integration Tests

**Test: Complete Workflow**
1. [ ] Run batch download (auto-mode) on 10 games
2. [ ] Some succeed, some fail
3. [ ] Verify succeeded games have music files
4. [ ] Verify failed games are tracked
5. [ ] Click "Retry Failed Downloads"
6. [ ] Manually correct failures using search dialogs
7. [ ] Verify all games now have music
8. [ ] Verify failed downloads list is empty
9. [ ] Menu item "Retry Failed Downloads" disappears

**Test: Whitelist Learning**
1. [ ] Run auto-download with whitelist enabled
2. [ ] Note which channels are rejected in logs
3. [ ] Manually download from a good non-whitelisted channel
4. [ ] Copy that channel's ID from logs
5. [ ] Add to whitelist in settings
6. [ ] Re-run auto-download
7. [ ] Verify that channel's playlists now pass whitelist check

## Log Verification

Check logs for these key messages:

### Whitelist Messages
```
✓ "Album 'X' from whitelisted channel 'Y' - whitelist check passed"
✗ "Album 'X' from channel 'Y' (ID: Z) rejected: Not in whitelist"
```

### Word Matching Messages
```
✗ "Album 'X' rejected: Insufficient word match for YouTube (50% < 66%)"
✓ Word match threshold met (no rejection message)
```

### Failure Tracking
```
✓ "Tracked failed download for 'Game Name': Download failed - no suitable music found"
✓ "Successfully retried download for: 'Game Name'"
```

## Edge Cases

**Test: Empty Whitelist**
- [ ] Set `WhitelistedYouTubeChannelIds` to empty list
- [ ] Enable whitelist
- [ ] Auto-download should reject ALL YouTube playlists
- [ ] Logs should show "No channel ID available for whitelist check"

**Test: Invalid Channel IDs**
- [ ] Add invalid/non-existent channel ID to whitelist
- [ ] Run auto-download
- [ ] System should still work (just won't match any channels)

**Test: Retry with No Failures**
- [ ] Clear all failed downloads
- [ ] Try to access retry menu
- [ ] Menu item should not appear
- [ ] OR if accessed via API, should show "No failed downloads" message

**Test: Large Failure Count**
- [ ] Trigger 20+ failed downloads
- [ ] Verify retry dialog shows first 10 + "... and X more"
- [ ] Verify all are retried when confirmed

**Test: Settings Persistence**
- [ ] Add channels to whitelist
- [ ] Restart Playnite
- [ ] Verify whitelist settings are preserved
- [ ] Run auto-download to confirm whitelist still works

## Performance Tests

**Test: Large Batch Download**
- [ ] Select 50+ games
- [ ] Run batch download with auto-mode
- [ ] Verify whitelist filtering doesn't significantly slow down process
- [ ] Check memory usage (failures list shouldn't cause issues)

**Test: Retry with Many Failures**
- [ ] Trigger 30+ failed downloads
- [ ] Retry all of them
- [ ] System should handle gracefully without crashes
- [ ] Progress should be shown properly

## Expected Behavior Summary

| Scenario | Expected Result |
|----------|----------------|
| Auto-download with whitelist ON | Only whitelisted channels considered |
| Manual download with whitelist ON | ALL channels shown |
| Auto-download with whitelist OFF | ALL channels considered, but word matching still applies |
| Word match <66% (multi-word game) | Album rejected |
| Word match 100% (single-word game) | Album rejected if not exact |
| Batch download failure | Added to failed downloads list |
| Successful retry | Removed from failed downloads list |
| Menu with failures | Shows count in parentheses |
| Menu without failures | Item doesn't appear |

## Troubleshooting

### Issue: Whitelist Not Working

**Check**:
- [ ] `EnableYouTubeChannelWhitelist` is true
- [ ] `WhitelistedYouTubeChannelIds` is not empty
- [ ] Using auto-mode (not manual mode)
- [ ] Channel IDs are correct (copied from YouTube URLs)
- [ ] Logs show whitelist check messages

### Issue: Too Many Albums Rejected

**Check**:
- [ ] Whitelist may be too restrictive
- [ ] Try disabling whitelist temporarily
- [ ] Add more reliable channels to whitelist
- [ ] Check if word matching threshold is too high

### Issue: Retry Menu Not Appearing

**Check**:
- [ ] Actually have failed downloads in list
- [ ] Failures are marked as unresolved
- [ ] `_gameMenuHandler` is properly initialized
- [ ] Check logs for tracking messages

### Issue: Channel ID Not Extracted

**Check**:
- [ ] YouTube API response format may have changed
- [ ] Check JSON parsing selectors in `YouTubeClient.cs`
- [ ] Verify logs show channel information
- [ ] May need to update JSON path selectors

## Test Data

### Known Good YouTube Channels (Pre-whitelisted)
- `UCfSN UCFt4IRa-lKRYUhvEg` - GilvaSunner
- `UC9l8PCqbv1x7qwU_1oiSR3A` - BrawlBRSTMs3
- `UC9ecwl3FTG66jIKA9JRDtmg` - GiIvaSunner
- `UCWD1McJJZnMjJwBh2JY-xfg` - OST Composure

### Test Games for Different Scenarios
- **Multi-word games**: "Final Fantasy VII", "The Legend of Zelda", "Super Mario Bros"
- **Single-word games**: "Skyrim", "Minecraft", "Doom"
- **Obscure games** (likely to fail): Made-up game names
- **Popular games** (likely to succeed): "Halo", "Pokemon", "Sonic"

## Regression Testing

After implementing, verify these still work:

- [ ] Manual single-game download (KHInsider)
- [ ] Manual single-game download (YouTube)
- [ ] Batch download with manual selection
- [ ] Album selection dialog shows all results
- [ ] Song selection dialog downloads correctly
- [ ] Search cache still works
- [ ] KHInsider downloads unaffected by YouTube changes
- [ ] Existing music files not overwritten (unless option selected)
- [ ] Progress dialogs show correct information

## Success Criteria

All features pass when:
1. ✅ Whitelist correctly filters YouTube playlists in auto-mode only
2. ✅ Word matching threshold prevents irrelevant albums
3. ✅ Failed downloads are tracked automatically
4. ✅ Retry menu appears when failures exist
5. ✅ Retry process allows manual correction
6. ✅ Summary shows accurate statistics
7. ✅ Resolved failures are removed from tracking
8. ✅ No regressions in existing functionality
9. ✅ Logs provide clear debugging information
10. ✅ Performance is acceptable for large batches
