# Dialog Window Alternatives for Playnite Extensions

## Current Approach
- **Custom WPF UserControl** with XAML
- **MVVM Pattern** (ViewModel + View)
- **Multiple dialogs** (Source → Album → Songs)
- **Complex XAML** with DataTemplates, Triggers, etc.

## Framework Constraints

**Playnite Extension Requirements:**
- **.NET Framework 4.6.2** (not .NET Core/.NET 5+)
- **WPF-based** (Windows Presentation Foundation)
- **Runs within Playnite's host process** (WPF application)
- **Must be compatible with Playnite SDK** (6.11.0.0)

**What This Means:**
- ❌ **Cannot use**: .NET Core/5+ frameworks (Avalonia, MAUI, etc.)
- ❌ **Cannot use**: Electron-based frameworks
- ⚠️ **Limited support**: Modern WPF libraries (some require .NET Core)
- ✅ **Can use**: WebView2 (with limitations)
- ✅ **Can use**: Modern WPF libraries compatible with .NET Framework 4.6.2

## Alternative Approaches

### 1. **Simplified WPF Dialogs** (Easiest - Minimal Changes)
**Pros:**
- Keep current architecture
- Reduce complexity
- Fewer rendering issues
- Faster to implement

**Cons:**
- Still WPF-based
- Less visual polish

**Implementation:**
- Remove complex DataTemplates
- Use simple ListBox with basic ItemTemplate
- Remove conditional visibility triggers
- Use simpler binding patterns

**Example:**
```xml
<ListBox ItemsSource="{Binding SearchResults}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel>
                <TextBlock Text="{Binding Name}" FontWeight="Bold"/>
                <TextBlock Text="{Binding Description}" Foreground="Gray"/>
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

---

### 2. **Playnite SDK Built-in Dialogs** (Limited but Reliable)
**Available Methods:**
- `Dialogs.ShowMessage()` - Simple message boxes
- `Dialogs.ShowErrorMessage()` - Error dialogs
- `Dialogs.SelectFile()` - File picker (already used for settings)
- `Dialogs.CreateWindow()` - Custom windows (current approach)

**Limitations:**
- No built-in list selection dialogs
- No multi-select dialogs
- No search/filter dialogs

**Use Case:**
- Good for simple source selection (2 options)
- Could use `ShowMessage` with buttons for source selection

**Example for Source Selection:**
```csharp
// Instead of custom dialog, use simple message with options
var result = _playniteApi.Dialogs.ShowMessage(
    "Select download source:\n\n1. KHInsider\n2. YouTube",
    "UniPSound - Select Source"
);
// Parse result or use different approach
```

---

### 3. **Native Windows Dialogs** (Most Reliable, Limited Features)
**Available:**
- `OpenFileDialog` - File selection (PNS uses this)
- `SaveFileDialog` - Save dialogs
- `FolderBrowserDialog` - Folder selection

**Pros:**
- Most reliable (native Windows)
- Always works in fullscreen
- No rendering issues
- Familiar to users

**Cons:**
- Limited to file/folder selection
- Can't show custom data (albums, songs)
- No search functionality
- No preview capability

**Use Case:**
- Not suitable for album/song selection
- Good for file path selection (already used)

---

### 4. **Combined Single Dialog** (Better UX, More Complex)
**Concept:**
- One dialog with tabs or sections
- Source → Album → Songs all in one window
- Navigation between steps

**Pros:**
- Better user experience
- Fewer window transitions
- More intuitive flow
- Can show progress across steps

**Cons:**
- More complex state management
- Larger dialog window
- More code to maintain

**Implementation:**
```xml
<TabControl>
    <TabItem Header="Source">
        <!-- Source selection -->
    </TabItem>
    <TabItem Header="Album" IsEnabled="{Binding SourceSelected}">
        <!-- Album selection -->
    </TabItem>
    <TabItem Header="Songs" IsEnabled="{Binding AlbumSelected}">
        <!-- Song selection -->
    </TabItem>
</TabControl>
```

---

### 5. **Simplified List-Based Dialog** (Balance of Features/Simplicity)
**Concept:**
- Use Playnite's `GenericItemOption` pattern
- Simple ListBox with minimal styling
- Remove preview buttons (or make optional)
- Focus on core functionality

**Pros:**
- Simpler than current
- Still flexible
- Easier to debug
- Less rendering issues

**Cons:**
- Less visual polish
- No preview (or simplified preview)

**Implementation:**
- Match PNS's simpler XAML structure
- Use DockPanel instead of Grid
- Remove complex visibility triggers
- Use simpler binding patterns

---

### 6. **Code-Only Dialog (No XAML)** (Most Reliable, Least Flexible)
**Concept:**
- Create dialog entirely in C# code
- No XAML files
- Programmatic UI creation

**Pros:**
- No XAML parsing issues
- Full control
- Easier to debug
- More reliable

**Cons:**
- More verbose code
- Harder to maintain
- Less visual design flexibility

**Example:**
```csharp
var listBox = new ListBox();
listBox.ItemsSource = albums;
listBox.SelectionMode = SelectionMode.Single;

var stackPanel = new StackPanel();
stackPanel.Children.Add(new TextBlock { Text = "Select Album" });
stackPanel.Children.Add(listBox);
stackPanel.Children.Add(new Button { Content = "OK" });

window.Content = stackPanel;
```

---

### 7. **Hybrid Approach** (Recommended)
**Concept:**
- Use Playnite's `ShowMessage` for simple choices (source selection)
- Use simplified WPF dialog for complex selection (albums/songs)
- Combine steps where possible

**Implementation:**
1. **Source Selection**: Use `ShowMessage` with numbered options or simple dialog
2. **Album/Song Selection**: Simplified WPF dialog (like PNS)
3. **Reduce complexity**: Remove preview buttons, simplify XAML

**Pros:**
- Best of both worlds
- More reliable for simple choices
- Still flexible for complex selection
- Better user experience

---

## Recommendations

### Short-term (Quick Wins):
1. **Simplify XAML** - Remove complex triggers, use simpler templates
2. **Match PNS structure** - Use DockPanel like PNS does
3. **Remove optional features** - Preview can be optional or removed

### Medium-term (Better UX):
1. **Combine source selection** - Use simple message or radio buttons
2. **Simplify album/song dialogs** - Match PNS's simpler XAML
3. **Better error handling** - More user-friendly messages

### Long-term (If Issues Persist):
1. **Code-only dialogs** - If XAML continues to cause issues
2. **Native dialogs where possible** - For simple selections
3. **Single combined dialog** - Better UX, fewer windows

---

## Comparison Table

| Approach | Reliability | Flexibility | Complexity | UX Quality | Modern Feel |
|----------|------------|-------------|------------|------------|-------------|
| Current (Complex WPF) | Medium | High | High | High | Medium |
| Simplified WPF | High | High | Medium | Medium-High | Medium |
| Modern WPF Libraries | High | High | Medium | High | High |
| WebView2 + React | Medium | Very High | Very High | Very High | Very High |
| Playnite SDK Built-in | Very High | Low | Low | Low | Low |
| Native Windows | Very High | Very Low | Low | Low | Low |
| Combined Dialog | Medium | High | High | Very High | Medium-High |
| Code-Only | Very High | Medium | Medium | Medium | Low |
| Hybrid | High | High | Medium | High | Medium |

---

## Recommendations by Priority

### **Immediate (If Current Works):**
- ✅ **Keep current approach** - It's working now
- ✅ **Monitor for issues** - Fix as they arise
- ✅ **Simplify incrementally** - Remove complexity where possible

### **Short-term (If Issues Persist):**
1. **Simplify XAML** - Match PNS's simpler structure (easiest win)
2. **Try Modern WPF Library** - MaterialDesignInXamlToolkit or ModernWPF
3. **Hybrid approach** - Simple dialogs for source, WPF for albums/songs

### **Medium-term (For Better UX):**
1. **Modern WPF Library** - Add MaterialDesign or ModernWPF for better styling
2. **Combined dialog** - Single dialog with navigation (better UX)
3. **Enhanced error handling** - Better user feedback

### **Long-term (If WebView2 Becomes Viable):**
1. **WebView2 + React** - Only if WebView2 runtime becomes standard
2. **Wait for Avalonia** - If Playnite migrates, rewrite for modern framework

---

## Practical Recommendation

**For Your Current Situation:**
Since dialogs are working now, I recommend:

1. **Keep current approach** - It's functional
2. **Simplify XAML gradually** - Remove unnecessary complexity
3. **Consider Modern WPF Library** - If you want better styling without major rewrite
4. **Avoid WebView2 for now** - Too complex, requires runtime, may not work in fullscreen

**If You Want Modern UI:**
- **Best option**: MaterialDesignInXamlToolkit or ModernWPF
- **Why**: Modern components, still WPF (reliable), no runtime dependencies
- **Effort**: Medium (add library, update XAML, restyle components)

**If You Want Web-Based UI:**
- **Wait**: Until WebView2 is more standard or Playnite adds support
- **Or**: Use WebView2 but accept complexity and potential issues
- **Consider**: Is the complexity worth it for dialogs?

---

## Next Steps

1. **Test current dialogs** - See if they're stable now ✅
2. **If issues persist** - Simplify XAML first (easiest win)
3. **If you want modern UI** - Try MaterialDesignInXamlToolkit (good balance)
4. **If you want web UI** - Consider WebView2, but be aware of limitations
5. **Monitor PNS** - They use similar approach, see if they have issues

