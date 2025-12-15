# URI Handler Support - Implementation Analysis & Benefits

**Date**: 2025-12-14  
**Status**: Analysis Complete - Ready for Implementation  
**Feature Request**: URI scheme support for external integration

## 🎯 **Overview**

Implement `playnite://UniPlaySong/...` URI scheme support to enable external applications, themes, and scripts to trigger extension actions programmatically.

## 🔍 **What is a URI Handler?**

A URI handler allows external applications, web pages, or scripts to trigger actions within Playnite extensions using a standardized URL format.

**Format**: `playnite://ExtensionName/Command?Parameter1=Value1&Parameter2=Value2`

**Example**: `playnite://UniPlaySong/DownloadMusic?gameId=abc123`

## 💡 **Benefits & Use Cases**

### **1. Theme Author Integration** 🎨

**Benefit**: Themes can add buttons/links that trigger extension actions without embedding extension code.

**Use Cases**:
- **"Download Music" button** in game details view
- **"Set Primary Song" button** in game card hover menu
- **"Open Music Folder" link** in game information panel
- **Music player controls** (play/pause/next) in theme UI

**Example Theme Code**:
```xaml
<!-- In theme XAML -->
<Button Click="DownloadMusic_Click">
    <TextBlock Text="Download Music" />
</Button>

<!-- Code-behind -->
private void DownloadMusic_Click(object sender, RoutedEventArgs e)
{
    var gameId = SelectedGame.Id;
    Process.Start($"playnite://UniPlaySong/DownloadMusic?gameId={gameId}");
}
```

**Advantage**: Themes don't need to reference extension DLLs directly, reducing coupling and compatibility issues.

---

### **2. Extension-to-Extension Integration** 🔌

**Benefit**: Other extensions can trigger UniPSong actions programmatically.

**Use Cases**:
- **Extension Loader**: Pause music when trailers play
- **Game Activity Tracker**: Download music for newly added games automatically
- **Playlist Extensions**: Set primary songs based on playlists
- **Theme Switcher**: Update music settings when switching themes

**Example Integration**:
```csharp
// In another extension
public void OnGameAdded(Game newGame)
{
    // Automatically download music for new games
    Process.Start($"playnite://UniPlaySong/DownloadMusic?gameId={newGame.Id}");
}
```

**Advantage**: Enables ecosystem integration without direct DLL dependencies.

---

### **3. External Scripts & Automation** 🤖

**Benefit**: Power users can automate music management tasks.

**Use Cases**:
- **Batch scripts**: Normalize all music files via PowerShell
- **Auto-download**: Download music for newly acquired games
- **Synchronization**: Sync music settings across multiple Playnite installations
- **Backup scripts**: Export music library metadata

**Example PowerShell Script**:
```powershell
# Download music for all games matching a filter
$games = Get-PlayniteGames -Filter "Platform:Steam"
foreach ($game in $games) {
    Start-Process "playnite://UniPlaySong/DownloadMusic?gameId=$($game.Id)"
}
```

**Advantage**: Enables advanced automation workflows.

---

### **4. Web Browser Integration** 🌐

**Benefit**: Web pages or browser extensions can trigger actions.

**Use Cases**:
- **Browser extension**: "Add to Playnite Music Library" button on music sites
- **Web UI**: Web-based music management dashboard
- **Remote control**: Control music from web interface

**Example HTML**:
```html
<a href="playnite://UniPlaySong/DownloadMusic?url=https://youtube.com/watch?v=...&gameId=abc123">
    Download Music to Playnite
</a>
```

**Advantage**: Enables web-based workflows and remote access.

---

### **5. External Applications** 💻

**Benefit**: Desktop applications can integrate with Playnite music management.

**Use Cases**:
- **Music managers**: Sync playlists to Playnite
- **Game launchers**: Download music when launching games
- **Media players**: Trigger music download from external player
- **Automation tools**: Task schedulers for music management

**Example C# Application**:
```csharp
// External app triggers music download
Process.Start($"playnite://UniPlaySong/DownloadMusic?gameId={gameId}&source=YouTube");
```

**Advantage**: Enables desktop application ecosystem integration.

---

## 🎯 **Proposed URI Commands**

### **1. Download Music**
**URI**: `playnite://UniPlaySong/DownloadMusic?gameId={guid}`  
**Parameters**:
- `gameId` (required): Game GUID
- `source` (optional): "KHInsider" or "YouTube" (default: show source selection)
- `albumId` (optional): Specific album ID (skips album selection)
- `songIds` (optional): Comma-separated song IDs (skips song selection)

**Example**:
```
playnite://UniPlaySong/DownloadMusic?gameId=abc-123-def-456
playnite://UniPlaySong/DownloadMusic?gameId=abc-123&source=YouTube
playnite://UniPlaySong/DownloadMusic?gameId=abc-123&source=KHInsider&albumId=12345
```

**Action**: Opens download dialog for specified game (or auto-downloads if all params provided)

---

### **2. Set Primary Song**
**URI**: `playnite://UniPlaySong/SetPrimary?gameId={guid}&song={filename}`  
**Parameters**:
- `gameId` (required): Game GUID
- `song` (required): Song filename or full path

**Example**:
```
playnite://UniPlaySong/SetPrimary?gameId=abc-123&song=Main%20Theme.mp3
playnite://UniPlaySong/SetPrimary?gameId=abc-123&song=C%3A%5CMusic%5CTheme.mp3
```

**Action**: Sets specified song as primary for the game

---

### **3. Remove Primary Song**
**URI**: `playnite://UniPlaySong/RemovePrimary?gameId={guid}`  
**Parameters**:
- `gameId` (required): Game GUID

**Example**:
```
playnite://UniPlaySong/RemovePrimary?gameId=abc-123
```

**Action**: Removes primary song setting for the game

---

### **4. Open Music Folder**
**URI**: `playnite://UniPlaySong/OpenFolder?gameId={guid}`  
**Parameters**:
- `gameId` (required): Game GUID

**Example**:
```
playnite://UniPlaySong/OpenFolder?gameId=abc-123
```

**Action**: Opens Windows Explorer to game's music folder

---

### **5. Normalize Audio**
**URI**: `playnite://UniPlaySong/Normalize?gameId={guid}`  
**Parameters**:
- `gameId` (optional): Game GUID (normalize specific game, or all if omitted)
- `all` (optional): "true" to normalize all music files

**Example**:
```
playnite://UniPlaySong/Normalize?gameId=abc-123
playnite://UniPlaySong/Normalize?all=true
```

**Action**: Starts normalization process for specified scope

---

### **6. Play/Pause Control**
**URI**: `playnite://UniPlaySong/Control?action={play|pause|stop|next|previous}`  
**Parameters**:
- `action` (required): Control action

**Example**:
```
playnite://UniPlaySong/Control?action=play
playnite://UniPlaySong/Control?action=pause
playnite://UniPlaySong/Control?action=next
```

**Action**: Controls music playback (useful for theme music player controls)

---

### **7. Get Status (Future)**
**URI**: `playnite://UniPlaySong/Status?gameId={guid}`  
**Parameters**:
- `gameId` (optional): Game GUID

**Returns**: JSON status (song count, primary song, etc.) - would require callback mechanism

**Note**: Playnite URI handler doesn't directly support return values, would need file-based or event-based response.

---

## 🔧 **Implementation Details**

### **Registration**

```csharp
// In UniPlaySong.cs constructor or OnApplicationStarted
PlayniteApi.UriHandler.RegisterSource("UniPlaySong", HandleUriEvent);
```

### **Handler Implementation**

```csharp
private void HandleUriEvent(UriHandlerEventArgs args)
{
    try
    {
        // Parse URI: playnite://UniPlaySong/Command?params
        var uri = new Uri(args.Uri);
        var command = uri.Segments[uri.Segments.Length - 1]; // Last segment is command
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        
        switch (command.ToLower())
        {
            case "downloadmusic":
                HandleDownloadMusicUri(query);
                break;
                
            case "setprimary":
                HandleSetPrimaryUri(query);
                break;
                
            case "removeprimary":
                HandleRemovePrimaryUri(query);
                break;
                
            case "openfolder":
                HandleOpenFolderUri(query);
                break;
                
            case "normalize":
                HandleNormalizeUri(query);
                break;
                
            case "control":
                HandleControlUri(query);
                break;
                
            default:
                logger.Warn($"Unknown URI command: {command}");
                break;
        }
    }
    catch (Exception ex)
    {
        logger.Error(ex, "Error handling URI event");
    }
}

private void HandleDownloadMusicUri(NameValueCollection query)
{
    var gameIdStr = query["gameId"];
    if (Guid.TryParse(gameIdStr, out var gameId))
    {
        var game = PlayniteApi.Database.Games.Get(gameId);
        if (game != null)
        {
            var sourceStr = query["source"];
            if (Enum.TryParse<Source>(sourceStr, out var source))
            {
                // Auto-download with source
                _gameMenuHandler.DownloadMusicForGame(game, source);
            }
            else
            {
                // Show download dialog
                ShowControllerDownloadDialog(game);
            }
        }
    }
}
```

### **Error Handling**
- Invalid game IDs: Log warning, show user message
- Missing parameters: Log error, show helpful message
- Invalid commands: Log warning, ignore
- Extension not ready: Queue command for later execution

---

## 📋 **Developer Documentation**

### **For Theme Authors**

**Integration Guide**:
```xaml
<!-- Add button to game details view -->
<Button Click="DownloadMusic_Click" Content="Download Music" />

<!-- Code-behind -->
private void DownloadMusic_Click(object sender, RoutedEventArgs e)
{
    if (SelectedGame != null)
    {
        var uri = $"playnite://UniPlaySong/DownloadMusic?gameId={SelectedGame.Id}";
        Process.Start(uri);
    }
}
```

**Available Commands**:
- Download Music: `playnite://UniPlaySong/DownloadMusic?gameId={guid}`
- Set Primary Song: `playnite://UniPlaySong/SetPrimary?gameId={guid}&song={filename}`
- Remove Primary Song: `playnite://UniPlaySong/RemovePrimary?gameId={guid}`
- Open Music Folder: `playnite://UniPlaySong/OpenFolder?gameId={guid}`
- Control Playback: `playnite://UniPlaySong/Control?action={play|pause|stop|next|previous}`

### **For Extension Developers**

**Example Integration**:
```csharp
// In another extension
public void OnGameSelected(Game game)
{
    // Automatically download music if missing
    var musicFiles = GetMusicFiles(game);
    if (musicFiles.Count == 0)
    {
        Process.Start($"playnite://UniPlaySong/DownloadMusic?gameId={game.Id}");
    }
}
```

### **For Power Users**

**Automation Scripts**:
```powershell
# PowerShell: Download music for all Steam games
$steamGames = Get-PlayniteGames | Where-Object { $_.Platform.Name -eq "Steam" }
foreach ($game in $steamGames) {
    Start-Process "playnite://UniPlaySong/DownloadMusic?gameId=$($game.Id)"
    Start-Sleep -Seconds 2 # Rate limiting
}
```

---

## 🎯 **Benefits Summary**

### **Immediate Benefits** ✅
1. **Theme Integration** - Themes can add music management buttons
2. **Extension Compatibility** - Other extensions can trigger actions
3. **Automation Support** - Scripts can automate music management
4. **User Convenience** - Quick links from external applications

### **Long-term Benefits** 🚀
1. **Ecosystem Growth** - Enables extension ecosystem integration
2. **User Adoption** - Power users can create custom workflows
3. **Theme Adoption** - Themes can offer integrated music features
4. **Community Tools** - Community can build tools around URI scheme

### **Competitive Advantage** 🏆
- PNS has URI handler support - We should match this feature
- Enables unique integrations (e.g., controller-based automation)
- Shows professional extension development

---

## 📊 **Implementation Priority**

**Priority**: ⭐⭐ Medium (High Value, Low Effort)

**Effort**: Low (few hours) - Playnite SDK provides URI handler support  
**Impact**: High - Enables ecosystem integration and power user workflows  
**Risk**: Low - Additive feature, doesn't affect existing functionality

---

## 🏁 **Conclusion**

URI handler support is a **high-value, low-effort feature** that:
- ✅ Enables theme authors to integrate music controls
- ✅ Allows extension-to-extension communication
- ✅ Supports power user automation
- ✅ Matches PNS feature parity
- ✅ Enhances extension ecosystem

**Recommendation**: Implement as part of next release cycle. It's a relatively simple addition that significantly expands the extension's integration capabilities and user value.

---

**Status**: Ready for implementation. This feature provides significant ecosystem value with minimal development effort, making it an excellent addition to UniPSong's feature set.