# Log Analysis Tool

This tool analyzes Playnite extension logs to compare the behavior of UniPlaySong and PlayniteSound, focusing on the login screen music playback issue.

## Purpose

The log analysis tool helps identify:
- Timing differences between UniPlaySong and PlayniteSound
- When `OnGameSelected` fires relative to video detection
- When `_firstSelect` flag is cleared
- When `VideoIsPlaying` changes
- Event sequence differences that might explain the login screen issue

## Usage

### Method 1: Direct Python Execution

```bash
python analyze_logs.py [path_to_extensions.log]
```

If no path is provided, it will try to find the log file at:
```
%APPDATA%\Playnite\extensions.log
```

### Method 2: Windows Batch File

```bash
analyze_logs.bat
```

This will automatically find the log file and run the analysis.

## Prerequisites

- Python 3.6 or higher
- No additional packages required (uses only standard library)

## Output

The tool generates four reports:

1. **Timeline Report** (`log_analysis_timeline.txt`)
   - Complete chronological timeline of all events
   - Shows events from both extensions side-by-side
   - Useful for understanding the overall flow

2. **Critical Events Report** (`log_analysis_critical.txt`)
   - Focuses only on critical events:
     - OnApplicationStarted
     - OnGameSelected
     - VideoIsPlaying changes
     - _firstSelect state changes
     - ShouldPlayMusic/ShouldPlayAudio calls
     - PauseMusic/ResumeMusic calls

3. **Comparison Report** (`log_analysis_comparison.txt`)
   - Side-by-side comparison of matching events
   - Highlights timing differences
   - Compares metadata (FirstSelect, SkipMusic, etc.)

4. **Summary Report** (`log_analysis_summary.txt`)
   - High-level findings
   - Event counts by type
   - Key timing issues identified
   - Recommendations

All reports are saved to:
```
%APPDATA%\Playnite\log_analysis\
```

## What to Look For

When analyzing the reports, focus on:

### 1. Timing of OnGameSelected
- Does it fire before or after VideoIsPlaying is set?
- How does the timing compare between extensions?

### 2. _firstSelect Flag State
- When is it cleared?
- Is it cleared at the same time relative to OnGameSelected in both extensions?

### 3. VideoIsPlaying Initialization
- When is it first set?
- What is its initial value?
- Does it change before OnGameSelected fires?

### 4. Event Sequence
- Compare the exact sequence of events between extensions
- Look for missing events or extra events
- Check if events fire in different orders

### 5. ShouldPlayMusic Results
- What does ShouldPlayMusic return in each case?
- Why does it return true/false?
- Compare the conditions that lead to different results

## Example Workflow

1. **Start Playnite in fullscreen mode** (with ANIKI REMAKE theme if available)
2. **Let it fully load** (pass login screen if applicable)
3. **Select a game** (this should trigger OnGameSelected)
4. **Wait a few seconds** for all events to complete
5. **Close Playnite**
6. **Run the analysis tool**:
   ```bash
   python analyze_logs.py
   ```
7. **Review the reports**, especially:
   - Comparison report for timing differences
   - Critical events report for event sequence
   - Summary report for key findings

## Interpreting Results

### If OnGameSelected fires before VideoIsPlaying is set:
- **Problem**: Video detection happens too late
- **Solution**: Need to initialize VideoIsPlaying earlier or delay OnGameSelected processing

### If _firstSelect is cleared too early:
- **Problem**: Flag is cleared before ShouldPlayAudio checks it
- **Solution**: Delay clearing the flag until after ShouldPlayAudio check

### If event sequences differ:
- **Problem**: Different initialization or event handling
- **Solution**: Match PlayniteSound's exact event sequence

### If ShouldPlayMusic returns different results:
- **Problem**: Different conditions being checked
- **Solution**: Compare the exact conditions in ShouldPlayMusic/ShouldPlayAudio

## Troubleshooting

### "Log file not found"
- Check that Playnite has been run at least once
- Verify the path: `%APPDATA%\Playnite\extensions.log`
- Make sure you have read permissions

### "No events found"
- Check that both extensions are installed and enabled
- Verify that logging is enabled in Playnite
- Make sure you've actually triggered events (selected games, etc.)

### "No critical events found"
- The log might not contain the events we're looking for
- Try running Playnite and selecting a game first
- Check that the log file contains `[UniPlaySong]` or `[PlayniteSound]` entries

## Tips

1. **Clear the log before testing**: Delete or rename `extensions.log` before starting a fresh test
2. **Test one scenario at a time**: Don't mix desktop and fullscreen tests in the same log
3. **Compare multiple runs**: Run the analysis on logs from multiple test sessions to identify patterns
4. **Focus on the first OnGameSelected**: The first selection is usually the problematic one
5. **Check the timeline**: The timeline report shows the exact sequence - use it to understand the flow

## Next Steps

After analyzing the logs:
1. Identify the root cause based on the findings
2. Compare with PlayniteSound's behavior
3. Implement fixes based on the identified issues
4. Re-test and re-analyze to verify the fix

