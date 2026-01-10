# Contributing to the UPS Search Hints Database

The `search_hints.json` file helps UniPlaySong find the correct music for games with problematic names or that are hard to get search results for. This guide explains how to add new entries to help the community.

## When to Add a Hint

Add a hint when a game's name causes search problems:
- **Special characters**: Games with colons, ampersands, or symbols (e.g., "Hitman: Absolution")
- **Ambiguous names**: Common words that return wrong results (e.g., "Control", "Prey")
- **Subtitle issues**: Games where the subtitle confuses search (e.g., "The Coma 2: Vicious Sisters")
- **Localized titles**: Games known by different names in different regions
- **Franchise confusion**: When searching returns music from the wrong game in a series

## Entry Format

Each entry is a JSON object with the game name as the key:

```json
{
  "Game Name": {
    "khinsiderAlbum": "album-url-slug",
    "youtubePlaylistId": "PLxxxxxxxxxx",
    "searchTerms": ["alternative", "search", "terms"],
    "notes": "Why this hint was added"
  }
}
```

### Fields

| Field | Required | Description |
|-------|----------|-------------|
| `khinsiderAlbum` | No | The KHInsider album URL slug (the part after `/game-soundtracks/album/`) |
| `youtubePlaylistId` | No | A YouTube playlist ID containing the game's soundtrack |
| `searchTerms` | No | Alternative search terms to try instead of the game name |
| `notes` | No | Documentation for why this hint exists |

**At least one of `khinsiderAlbum`, `youtubePlaylistId`, or `searchTerms` should be provided.**

## Examples

### KHInsider Album Hint
For games where you know the exact KHInsider album:

```json
"Hitman: Absolution": {
  "khinsiderAlbum": "hitman-absolution",
  "notes": "Colon in name causes search issues"
}
```

### YouTube Playlist Hint
For games not on KHInsider but available on YouTube:

```json
"Indie Game XYZ": {
  "youtubePlaylistId": "PLxxxxxxxxxxxxxxxxxxxx",
  "notes": "Not available on KHInsider"
}
```

### Search Terms Hint
For games that need different search queries:

```json
"DOOM (2016)": {
  "searchTerms": ["doom 2016 ost", "doom 2016 soundtrack"],
  "notes": "Disambiguate from classic DOOM"
}
```

### Combined Hint
You can specify multiple sources:

```json
"Problematic Game": {
  "khinsiderAlbum": "problematic-game-ost",
  "youtubePlaylistId": "PLxxxxxxxxxxxxxxxxxxxx",
  "searchTerms": ["problematic game soundtrack"],
  "notes": "Multiple sources for reliability"
}
```

## How to Find Album/Playlist IDs

### KHInsider Album Slug
1. Go to [KHInsider](https://downloads.khinsider.com/game-soundtracks)
2. Search for the game
3. The album slug is the last part of the URL:
   - URL: `https://downloads.khinsider.com/game-soundtracks/album/hitman-absolution`
   - Slug: `hitman-absolution`

### YouTube Playlist ID
1. Find or create a playlist with the game's soundtrack
2. The playlist ID is in the URL after `list=`:
   - URL: `https://www.youtube.com/playlist?list=PLxxxxxxxxxxx`
   - ID: `PLxxxxxxxxxxx`

## Submitting Your Contribution

### Option 1: GitHub Pull Request (Preferred)
1. Fork the [UniPlaySong repository](https://github.com/aHuddini/UniPlaySong)
2. Edit `AutoSearchDatabase/search_hints.json`
3. Add your entries in alphabetical order by game name
4. Submit a pull request with a description of what games you added

### Option 2: GitHub Issue
1. Open an [issue](https://github.com/aHuddini/UniPlaySong/issues/new)
2. Title: "Search Hint Request: [Game Name]"
3. Include:
   - Game name (exactly as it appears in Playnite)
   - The KHInsider album URL or YouTube playlist URL
   - Why the hint is needed

## Guidelines

1. **Use exact game names**: Match how the game appears in Playnite/your library
2. **Prefer KHInsider**: It's the primary source and usually has better quality
3. **Verify your hints**: Test that the album/playlist actually contains the correct music
4. **Keep notes brief**: Explain why the hint is needed in a few words
5. **Alphabetical order**: Add entries in alphabetical order for easier maintenance
6. **No duplicates**: Check if a hint already exists before adding

## JSON Validation

Before submitting, validate your JSON:
- Use a JSON validator like [jsonlint.com](https://jsonlint.com/)
- Ensure proper comma placement (no trailing commas)
- Check that all strings are properly quoted

## Questions?

Open an issue on GitHub if you have questions about contributing.

Thank you for helping improve UniPlaySong for everyone!
