#!/usr/bin/env python3
"""
Log Analysis Tool for UniPlaySong vs PlayniteSound Comparison

This script analyzes the extensions.log file from Playnite to compare
the behavior of UniPlaySong and PlayniteSound extensions, focusing on
the login screen music playback issue.

Usage:
    python analyze_logs.py [path_to_extensions.log]
    
If no path is provided, it will try to find the log file in:
    %APPDATA%/Playnite/extensions.log
"""

import re
import sys
import os
from datetime import datetime
from collections import defaultdict
from typing import List, Dict, Tuple, Optional
from dataclasses import dataclass, field
from enum import Enum


class ExtensionType(Enum):
    """Extension identifier"""
    UNIPLAYSONG = "UniPlaySong"
    PLAYNITESOUND = "PlayniteSound"
    UNKNOWN = "Unknown"


class EventType(Enum):
    """Types of events we track"""
    ON_GAME_SELECTED = "OnGameSelected"
    ON_APPLICATION_STARTED = "OnApplicationStarted"
    ON_SETTINGS_CHANGED = "OnSettingsChanged"
    VIDEO_IS_PLAYING_CHANGED = "VideoIsPlaying"
    SHOULD_PLAY_MUSIC = "ShouldPlayMusic"
    SHOULD_PLAY_AUDIO = "ShouldPlayAudio"
    PLAY_MUSIC_BASED_ON_SELECTED = "PlayMusicBasedOnSelected"
    PAUSE_MUSIC = "PauseMusic"
    RESUME_MUSIC = "ResumeMusic"
    FIRST_SELECT_STATE = "_firstSelect"
    MEDIA_ELEMENTS_MONITOR = "MediaElementsMonitor"
    ON_MAIN_MODEL_CHANGED = "OnMainModelChanged"
    OTHER = "Other"


@dataclass
class LogEvent:
    """Represents a single log event"""
    timestamp: datetime
    extension: ExtensionType
    event_type: EventType
    message: str
    raw_line: str
    metadata: Dict[str, any] = field(default_factory=dict)


class LogAnalyzer:
    """Analyzes extension logs and creates comparison reports"""
    
    def __init__(self, log_file_paths: List[str] = None):
        self.log_file_paths = log_file_paths or []
        self.events: List[LogEvent] = []
        self.uniplaysong_events: List[LogEvent] = []
        self.playnitesound_events: List[LogEvent] = []
        
    def parse_log_files(self) -> bool:
        """Parse all log files and extract events"""
        if not self.log_file_paths:
            print("Error: No log files provided")
            return False
        
        total_events = 0
        for log_file_path in self.log_file_paths:
            if os.path.exists(log_file_path):
                print(f"Reading log file: {log_file_path}")
                events_found = self._parse_single_log_file(log_file_path)
                total_events += events_found
                print(f"  Found {events_found} events")
            else:
                print(f"Warning: Log file not found: {log_file_path}")
        
        print(f"\nTotal events parsed: {total_events}")
        print(f"  - UniPlaySong: {len(self.uniplaysong_events)} events")
        print(f"  - PlayniteSound: {len(self.playnitesound_events)} events")
        
        return total_events > 0
    
    def _parse_single_log_file(self, log_file_path: str) -> int:
        """Parse a single log file and extract events"""
        events_before = len(self.events)
        
        # Pattern to match log lines
        # Format 1: YYYY-MM-DD HH:MM:SS.mmm | LEVEL | Extension | Message (Playnite extensions.log)
        log_pattern = re.compile(
            r'(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\s*\|\s*(\w+)\s*\|\s*(.*?)\s*\|\s*(.*)'
        )
        
        # Format 2: [YYYY-MM-DD HH:MM:SS.mmm] [LEVEL] Message (FileLogger format)
        filelogger_pattern = re.compile(
            r'\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\]\s+\[(\w+)\]\s+(.*)'
        )
        
        # Alternative pattern for simpler log formats
        simple_pattern = re.compile(
            r'(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\s+(.*)'
        )
        
        try:
            with open(log_file_path, 'r', encoding='utf-8', errors='ignore') as f:
                lines = f.readlines()
            
            for line_num, line in enumerate(lines, 1):
                line = line.strip()
                if not line:
                    continue
                    
                # Try standard Playnite log format first
                match = log_pattern.match(line)
                if match:
                    timestamp_str, level, extension_name, message = match.groups()
                else:
                    # Try FileLogger format ([timestamp] [LEVEL] message)
                    match = filelogger_pattern.match(line)
                    if match:
                        timestamp_str, level, message = match.groups()
                        # Extract extension name from message
                        if '[UniPlaySong]' in message or 'UniPlaySong' in message:
                            extension_name = 'UniPlaySong'
                        elif '[PlayniteSound]' in message or 'PlayniteSound' in message:
                            extension_name = 'PlayniteSound'
                        else:
                            extension_name = 'Unknown'
                    else:
                        # Try simple format
                        match = simple_pattern.match(line)
                        if match:
                            timestamp_str, rest = match.groups()
                            # Try to extract extension name from message
                            if '[UniPlaySong]' in rest or 'UniPlaySong' in rest:
                                extension_name = 'UniPlaySong'
                                message = rest
                            elif '[PlayniteSound]' in rest or 'PlayniteSound' in rest:
                                extension_name = 'PlayniteSound'
                                message = rest
                            else:
                                extension_name = 'Unknown'
                                message = rest
                            level = 'INFO'
                        else:
                            # Skip lines that don't match any pattern
                            continue
                
                try:
                    timestamp = datetime.strptime(timestamp_str, '%Y-%m-%d %H:%M:%S.%f')
                except ValueError:
                    # Try without milliseconds
                    try:
                        timestamp = datetime.strptime(timestamp_str, '%Y-%m-%d %H:%M:%S')
                    except ValueError:
                        continue
                
                # Determine extension type
                if 'UniPlaySong' in extension_name or '[UniPlaySong]' in message:
                    extension = ExtensionType.UNIPLAYSONG
                elif 'PlayniteSound' in extension_name or '[PlayniteSound]' in message:
                    extension = ExtensionType.PLAYNITESOUND
                else:
                    extension = ExtensionType.UNKNOWN
                
                # Skip unknown extensions (unless they're relevant)
                if extension == ExtensionType.UNKNOWN:
                    continue
                
                # Classify event type
                event_type = self._classify_event(message)
                
                # Extract metadata
                metadata = self._extract_metadata(message, event_type)
                
                event = LogEvent(
                    timestamp=timestamp,
                    extension=extension,
                    event_type=event_type,
                    message=message,
                    raw_line=line,
                    metadata=metadata
                )
                
                self.events.append(event)
                
                if extension == ExtensionType.UNIPLAYSONG:
                    self.uniplaysong_events.append(event)
                elif extension == ExtensionType.PLAYNITESOUND:
                    self.playnitesound_events.append(event)
            
            events_after = len(self.events)
            return events_after - events_before
            
        except Exception as e:
            print(f"Error parsing log file {log_file_path}: {e}")
            import traceback
            traceback.print_exc()
            return 0
    
    def _classify_event(self, message: str) -> EventType:
        """Classify the event type based on message content"""
        message_lower = message.lower()
        
        if 'ongameselected' in message_lower or 'on game selected' in message_lower:
            return EventType.ON_GAME_SELECTED
        elif 'onapplicationstarted' in message_lower or 'application started' in message_lower:
            return EventType.ON_APPLICATION_STARTED
        elif 'onsettingschanged' in message_lower or 'on settings changed' in message_lower:
            return EventType.ON_SETTINGS_CHANGED
        elif 'videoisplaying' in message_lower or 'video is playing' in message_lower:
            return EventType.VIDEO_IS_PLAYING_CHANGED
        elif 'shouldplaymusic' in message_lower or 'should play music' in message_lower:
            return EventType.SHOULD_PLAY_MUSIC
        elif 'shouldplayaudio' in message_lower or 'should play audio' in message_lower:
            return EventType.SHOULD_PLAY_AUDIO
        elif 'playmusicbasedonselected' in message_lower:
            return EventType.PLAY_MUSIC_BASED_ON_SELECTED
        elif 'pausemusic' in message_lower or 'pause music' in message_lower:
            return EventType.PAUSE_MUSIC
        elif 'resumemusic' in message_lower or 'resume music' in message_lower:
            return EventType.RESUME_MUSIC
        elif 'firstselect' in message_lower or '_firstselect' in message_lower or 'first select' in message_lower:
            return EventType.FIRST_SELECT_STATE
        elif 'mediaelementsmonitor' in message_lower or 'media elements monitor' in message_lower:
            return EventType.MEDIA_ELEMENTS_MONITOR
        elif 'onmainmodelchanged' in message_lower or 'on main model changed' in message_lower:
            return EventType.ON_MAIN_MODEL_CHANGED
        else:
            return EventType.OTHER
    
    def _extract_metadata(self, message: str, event_type: EventType) -> Dict[str, any]:
        """Extract relevant metadata from the message"""
        metadata = {}
        
        # Extract FirstSelect state
        first_select_match = re.search(r'FirstSelect[:\s]+(true|false)', message, re.IGNORECASE)
        if first_select_match:
            metadata['firstSelect'] = first_select_match.group(1).lower() == 'true'
        
        # Extract SkipMusic/SkipMusic value
        skip_match = re.search(r'SkipMusic[:\s]+(true|false)', message, re.IGNORECASE)
        if skip_match:
            metadata['skipMusic'] = skip_match.group(1).lower() == 'true'
        
        # Extract VideoIsPlaying value
        video_match = re.search(r'VideoIsPlaying[:\s]+(true|false|changing from \w+ to \w+)', message, re.IGNORECASE)
        if video_match:
            metadata['videoIsPlaying'] = video_match.group(1)
        
        # Extract Game name
        game_match = re.search(r'Game[:\s]+([^,\(]+)', message, re.IGNORECASE)
        if game_match:
            metadata['game'] = game_match.group(1).strip()
        
        # Extract ActiveView
        view_match = re.search(r'ActiveView[:\s]+([^\s,]+)', message, re.IGNORECASE)
        if view_match:
            metadata['activeView'] = view_match.group(1)
        
        # Extract Mode
        mode_match = re.search(r'Mode[:\s]+(Desktop|Fullscreen)', message, re.IGNORECASE)
        if mode_match:
            metadata['mode'] = mode_match.group(1)
        
        # Extract ShouldPlayMusic result
        should_play_match = re.search(r'ShouldPlayMusic[:\s]+(returned|returned:)\s*(true|false)', message, re.IGNORECASE)
        if should_play_match:
            metadata['shouldPlayMusic'] = should_play_match.group(2).lower() == 'true'
        
        # Extract ShouldPlayAudio result
        should_audio_match = re.search(r'ShouldPlayAudio[:\s]+(returned|returned:)\s*(true|false)', message, re.IGNORECASE)
        if should_audio_match:
            metadata['shouldPlayAudio'] = should_audio_match.group(2).lower() == 'true'
        
        return metadata
    
    def create_timeline_report(self) -> str:
        """Create a timeline comparison report"""
        if not self.events:
            return "No events found in log file."
        
        # Sort events by timestamp
        sorted_events = sorted(self.events, key=lambda e: e.timestamp)
        
        # Find the first event timestamp as baseline
        if not sorted_events:
            return "No events to analyze."
        
        baseline = sorted_events[0].timestamp
        
        report = []
        report.append("=" * 100)
        report.append("TIMELINE COMPARISON REPORT")
        report.append("=" * 100)
        report.append(f"\nBaseline timestamp: {baseline}")
        report.append(f"Total events: {len(sorted_events)}")
        report.append(f"UniPlaySong events: {len(self.uniplaysong_events)}")
        report.append(f"PlayniteSound events: {len(self.playnitesound_events)}")
        report.append("\n" + "=" * 100)
        report.append("\nEVENT TIMELINE:\n")
        
        # Group events by time windows (1 second windows)
        time_windows = defaultdict(list)
        for event in sorted_events:
            delta = (event.timestamp - baseline).total_seconds()
            window = int(delta)
            time_windows[window].append(event)
        
        for window in sorted(time_windows.keys()):
            events_in_window = time_windows[window]
            report.append(f"\n--- Time Window: +{window}s to +{window+1}s ---")
            
            for event in sorted(events_in_window, key=lambda e: e.timestamp):
                delta_ms = (event.timestamp - baseline).total_seconds() * 1000
                ext_name = event.extension.value
                event_name = event.event_type.value
                
                report.append(f"  [{delta_ms:8.1f}ms] [{ext_name:15s}] {event_name:30s} | {event.message[:60]}")
        
        return "\n".join(report)
    
    def create_critical_events_report(self) -> str:
        """Create a report focusing on critical events"""
        report = []
        report.append("=" * 100)
        report.append("CRITICAL EVENTS ANALYSIS")
        report.append("=" * 100)
        
        # Focus on key event types
        critical_types = [
            EventType.ON_APPLICATION_STARTED,
            EventType.ON_GAME_SELECTED,
            EventType.VIDEO_IS_PLAYING_CHANGED,
            EventType.FIRST_SELECT_STATE,
            EventType.SHOULD_PLAY_MUSIC,
            EventType.SHOULD_PLAY_AUDIO,
            EventType.PLAY_MUSIC_BASED_ON_SELECTED,
            EventType.PAUSE_MUSIC,
            EventType.RESUME_MUSIC,
        ]
        
        # Filter critical events
        critical_events = [e for e in self.events if e.event_type in critical_types]
        critical_events.sort(key=lambda e: e.timestamp)
        
        if not critical_events:
            return "No critical events found."
        
        baseline = critical_events[0].timestamp
        
        report.append(f"\nFound {len(critical_events)} critical events\n")
        report.append("=" * 100)
        
        # Group by extension and event type
        for ext_type in [ExtensionType.UNIPLAYSONG, ExtensionType.PLAYNITESOUND]:
            ext_events = [e for e in critical_events if e.extension == ext_type]
            if not ext_events:
                continue
                
            report.append(f"\n{ext_type.value} Critical Events:")
            report.append("-" * 100)
            
            for event in ext_events:
                delta = (event.timestamp - baseline).total_seconds() * 1000
                report.append(f"\n[{delta:8.1f}ms] {event.event_type.value}")
                report.append(f"  Message: {event.message}")
                if event.metadata:
                    report.append(f"  Metadata: {event.metadata}")
        
        return "\n".join(report)
    
    def create_comparison_report(self) -> str:
        """Create a side-by-side comparison report"""
        report = []
        report.append("=" * 100)
        report.append("SIDE-BY-SIDE COMPARISON")
        report.append("=" * 100)
        
        # Find matching events between extensions
        # Group by event type and find closest matches
        
        # Focus on OnGameSelected events
        ups_selections = [e for e in self.uniplaysong_events if e.event_type == EventType.ON_GAME_SELECTED]
        ps_selections = [e for e in self.playnitesound_events if e.event_type == EventType.ON_GAME_SELECTED]
        
        report.append(f"\nOnGameSelected Events:")
        report.append(f"  UniPlaySong: {len(ups_selections)}")
        report.append(f"  PlayniteSound: {len(ps_selections)}")
        report.append("\n" + "-" * 100)
        
        # Compare first selections
        if ups_selections and ps_selections:
            ups_first = ups_selections[0]
            ps_first = ps_selections[0]
            
            report.append("\nFirst OnGameSelected Comparison:")
            report.append(f"\nUniPlaySong (at {ups_first.timestamp}):")
            report.append(f"  {ups_first.message}")
            if ups_first.metadata:
                report.append(f"  Metadata: {ups_first.metadata}")
            
            report.append(f"\nPlayniteSound (at {ps_first.timestamp}):")
            report.append(f"  {ps_first.message}")
            if ps_first.metadata:
                report.append(f"  Metadata: {ps_first.metadata}")
            
            # Compare timing
            time_diff = (ups_first.timestamp - ps_first.timestamp).total_seconds() * 1000
            report.append(f"\nTime difference: {time_diff:.1f}ms")
            if abs(time_diff) > 100:
                report.append(f"  ⚠️  Significant timing difference!")
        
        # Compare VideoIsPlaying changes
        ups_video = [e for e in self.uniplaysong_events if e.event_type == EventType.VIDEO_IS_PLAYING_CHANGED]
        ps_video = [e for e in self.playnitesound_events if e.event_type == EventType.VIDEO_IS_PLAYING_CHANGED]
        
        report.append(f"\n\nVideoIsPlaying Changes:")
        report.append(f"  UniPlaySong: {len(ups_video)}")
        report.append(f"  PlayniteSound: {len(ps_video)}")
        
        if ups_video and ps_video:
            report.append("\nFirst VideoIsPlaying Change:")
            report.append(f"\nUniPlaySong: {ups_video[0].message}")
            report.append(f"PlayniteSound: {ps_video[0].message}")
        
        # Compare _firstSelect state changes
        ups_first = [e for e in self.uniplaysong_events if e.event_type == EventType.FIRST_SELECT_STATE]
        ps_first = [e for e in self.playnitesound_events if e.event_type == EventType.FIRST_SELECT_STATE]
        
        report.append(f"\n\n_firstSelect State Changes:")
        report.append(f"  UniPlaySong: {len(ups_first)}")
        report.append(f"  PlayniteSound: {len(ps_first)}")
        
        if ups_first:
            report.append("\nUniPlaySong _firstSelect changes:")
            for event in ups_first[:5]:  # First 5
                delta = (event.timestamp - self.events[0].timestamp).total_seconds() * 1000
                report.append(f"  [{delta:8.1f}ms] {event.message}")
        
        if ps_first:
            report.append("\nPlayniteSound _firstSelect changes:")
            for event in ps_first[:5]:  # First 5
                delta = (event.timestamp - self.events[0].timestamp).total_seconds() * 1000
                report.append(f"  [{delta:8.1f}ms] {event.message}")
        
        return "\n".join(report)
    
    def create_summary_report(self) -> str:
        """Create a summary report with key findings"""
        report = []
        report.append("=" * 100)
        report.append("SUMMARY REPORT")
        report.append("=" * 100)
        
        if not self.events:
            return "No events to analyze."
        
        baseline = min(e.timestamp for e in self.events)
        
        # Count events by type for each extension
        ups_counts = defaultdict(int)
        ps_counts = defaultdict(int)
        
        for event in self.uniplaysong_events:
            ups_counts[event.event_type] += 1
        
        for event in self.playnitesound_events:
            ps_counts[event.event_type] += 1
        
        report.append("\nEvent Counts by Type:")
        report.append(f"{'Event Type':<40} {'UniPlaySong':<15} {'PlayniteSound':<15}")
        report.append("-" * 100)
        
        all_types = set(ups_counts.keys()) | set(ps_counts.keys())
        for event_type in sorted(all_types, key=lambda x: x.value):
            ups_count = ups_counts.get(event_type, 0)
            ps_count = ps_counts.get(event_type, 0)
            diff = ups_count - ps_count
            diff_str = f"({diff:+d})" if diff != 0 else ""
            report.append(f"{event_type.value:<40} {ups_count:<15} {ps_count:<15} {diff_str}")
        
        # Key findings
        report.append("\n\nKEY FINDINGS:")
        report.append("-" * 100)
        
        # Check if OnGameSelected fires before VideoIsPlaying is set
        ups_selections = [e for e in self.uniplaysong_events if e.event_type == EventType.ON_GAME_SELECTED]
        ups_video = [e for e in self.uniplaysong_events if e.event_type == EventType.VIDEO_IS_PLAYING_CHANGED]
        
        if ups_selections and ups_video:
            first_selection = ups_selections[0]
            first_video = ups_video[0]
            
            if first_selection.timestamp < first_video.timestamp:
                report.append("⚠️  UniPlaySong: OnGameSelected fires BEFORE VideoIsPlaying is set!")
                report.append(f"   OnGameSelected: {first_selection.timestamp}")
                report.append(f"   VideoIsPlaying: {first_video.timestamp}")
                report.append(f"   Difference: {(first_video.timestamp - first_selection.timestamp).total_seconds() * 1000:.1f}ms")
            else:
                report.append("✓  UniPlaySong: VideoIsPlaying is set before OnGameSelected")
        
        # Check _firstSelect clearing timing
        ups_first_changes = [e for e in self.uniplaysong_events 
                            if e.event_type == EventType.FIRST_SELECT_STATE and 'false' in e.message.lower()]
        
        if ups_selections and ups_first_changes:
            first_selection = ups_selections[0]
            first_clear = ups_first_changes[0]
            
            if abs((first_clear.timestamp - first_selection.timestamp).total_seconds()) < 0.1:
                report.append("✓  UniPlaySong: _firstSelect cleared immediately after OnGameSelected")
            else:
                report.append("⚠️  UniPlaySong: _firstSelect clearing timing differs from OnGameSelected")
        
        return "\n".join(report)
    
    def generate_all_reports(self, output_dir: str = None) -> Dict[str, str]:
        """Generate all reports"""
        reports = {
            'timeline': self.create_timeline_report(),
            'critical': self.create_critical_events_report(),
            'comparison': self.create_comparison_report(),
            'summary': self.create_summary_report(),
        }
        
        if output_dir:
            os.makedirs(output_dir, exist_ok=True)
            for name, content in reports.items():
                output_path = os.path.join(output_dir, f'log_analysis_{name}.txt')
                with open(output_path, 'w', encoding='utf-8') as f:
                    f.write(content)
                print(f"Written {name} report to: {output_path}")
        
        return reports


def find_log_files(appdata_path: str) -> List[str]:
    """Find all relevant log files"""
    log_files = []
    
    # 1. extensions.log (Playnite's extension log)
    extensions_log = os.path.join(appdata_path, 'Playnite', 'extensions.log')
    if os.path.exists(extensions_log):
        log_files.append(extensions_log)
    
    # 2. playnite.log (Playnite's main log)
    playnite_log = os.path.join(appdata_path, 'Playnite', 'playnite.log')
    if os.path.exists(playnite_log):
        log_files.append(playnite_log)
    
    # 3. Extension-specific log files
    extensions_dir = os.path.join(appdata_path, 'Playnite', 'Extensions')
    if os.path.exists(extensions_dir):
        # Find UniPlaySong.log and PlayniteSound.log
        for root, dirs, files in os.walk(extensions_dir):
            if 'UniPlaySong.log' in files:
                log_files.append(os.path.join(root, 'UniPlaySong.log'))
            if 'PlayniteSound.log' in files:
                log_files.append(os.path.join(root, 'PlayniteSound.log'))
    
    # Also check Playnite AppData root for extension logs
    playnite_root = os.path.join(appdata_path, 'Playnite')
    if os.path.exists(playnite_root):
        if os.path.exists(os.path.join(playnite_root, 'UniPlaySong.log')):
            log_files.append(os.path.join(playnite_root, 'UniPlaySong.log'))
        if os.path.exists(os.path.join(playnite_root, 'PlayniteSound.log')):
            log_files.append(os.path.join(playnite_root, 'PlayniteSound.log'))
    
    return log_files


def main():
    """Main entry point"""
    # Default log file locations
    appdata = os.getenv('APPDATA')
    if not appdata:
        print("Error: APPDATA environment variable not found")
        sys.exit(1)
    
    # Get log file paths from command line or find defaults
    if len(sys.argv) > 1:
        # User provided specific log files
        log_file_paths = [arg for arg in sys.argv[1:] if os.path.exists(arg)]
        if not log_file_paths:
            print(f"Error: None of the provided log files exist")
            sys.exit(1)
    else:
        # Find all relevant log files automatically
        log_file_paths = find_log_files(appdata)
        if not log_file_paths:
            print("No log files found. Searched:")
            print(f"  - {os.path.join(appdata, 'Playnite', 'extensions.log')}")
            print(f"  - {os.path.join(appdata, 'Playnite', 'playnite.log')}")
            print(f"  - Extension-specific logs in {os.path.join(appdata, 'Playnite', 'Extensions')}")
            print("\nPlease run Playnite and trigger some events first, or provide log file paths as arguments.")
            sys.exit(1)
    
    print("=" * 80)
    print("LOG ANALYSIS TOOL")
    print("=" * 80)
    print(f"\nFound {len(log_file_paths)} log file(s) to analyze:")
    for log_file in log_file_paths:
        print(f"  - {log_file}")
    print()
    
    # Create analyzer
    analyzer = LogAnalyzer(log_file_paths)
    
    # Parse log files
    if not analyzer.parse_log_files():
        print("\nError: No events found in log files.")
        print("Make sure both extensions are installed and have been used.")
        sys.exit(1)
    
    # Generate reports
    print("\nGenerating reports...")
    # Use first log file's directory for output, or Playnite AppData
    if log_file_paths:
        output_dir = os.path.join(os.path.dirname(log_file_paths[0]), 'log_analysis')
    else:
        output_dir = os.path.join(appdata, 'Playnite', 'log_analysis')
    reports = analyzer.generate_all_reports(output_dir)
    
    # Print summary to console
    print("\n" + "=" * 100)
    print("QUICK SUMMARY")
    print("=" * 100)
    print(reports['summary'])
    
    print("\n" + "=" * 100)
    print("Reports generated successfully!")
    print(f"Full reports saved to: {output_dir}")
    print("=" * 100)


if __name__ == '__main__':
    main()

