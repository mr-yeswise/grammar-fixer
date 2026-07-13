# Architecture Research: LanguageTool + WPF Desktop Grammar Correction

> Based on known patterns from 2022-2024 open-source projects (LanguageTool, LanguageTool-Desktop, LanguageTool.NET, Grammarly-clones, WPF text correction tools)

---

## 1. LanguageTool C# Client Patterns

### LanguageTool.NET (Official-ish wrapper)
```csharp
// Typical usage pattern
using LanguageTool.Http;

var client = new LanguageToolClient("http://localhost:8081");
var result = await client.CheckAsync(new CheckRequest {
    Text = "This is a test.",
    Language = "en-US",
    EnabledOnly = false
});

foreach (var match in result.Matches) {
    // match.Offset, match.Length, match.Message, match.Replacements
}
```

### Key Libraries (NuGet)
| Package | Purpose | Status |
|---------|---------|--------|
| `LanguageTool.Http` | Official HTTP client wrapper | Maintained |
| `LanguageTool.Core` | Models + parsing | Maintained |
| `languagetool-server` | Not on NuGet - download JAR | Manual |

### Common Patterns from Recent Repos
- **Process management**: `Process.Start()` with `RedirectStandardOutput/Error`, health check polling `/v2/languages`
- **Single-file publish**: Include JAR as `Content` with `CopyToOutputDirectory=PreserveNewest`
- **Port handling**: Random port or fixed (8081) with conflict detection
- **Graceful degradation**: Try-catch around LT calls, fallback to local rules

---

## 2. WPF Floating Window Architecture (2022-2024)

### Topmost Modeless Window Pattern
```xml
<!-- CorrectionWindow.xaml -->
<Window x:Class="GrammarFixer.UI.CorrectionWindow"
        WindowStyle="None"
        AllowsTransparency="True"
        Topmost="True"
        ShowInTaskbar="False"
        SizeToContent="WidthAndHeight"
        MinWidth="400" MinHeight="300"
        MaxWidth="800" MaxHeight="600">
    <WindowChrome.WindowChrome>
        <WindowChrome GlassFrameThickness="0" CornerRadius="8" />
    </WindowChrome.WindowChrome>
</Window>
```

```csharp
// Positioning at caret
public void ShowAtCaret()
{
    var pos = UiaHelper.GetCaretScreenPosition();
    Left = pos.X;
    Top = pos.Y + 20; // Below caret
    Show();
    Activate();
    TextBox.Focus();
}
```

### Debounced Auto-Correct in TextBox
```csharp
private readonly System.Timers.Timer _correctionDebounce = new(300) { AutoReset = false };

private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
{
    _correctionDebounce.Stop();
    _correctionDebounce.Start();
}

private async void OnDebounceElapsed(object sender, ElapsedEventArgs e)
{
    var text = Dispatcher.Invoke(() => TextBox.Text);
    if (string.IsNullOrWhiteSpace(text)) return;
    
    var result = await _ltClient.CheckAsync(text);
    Dispatcher.Invoke(() => UpdateDiffView(result));
}
```

---

## 3. Rolling Diff View with DiffPlex

### Inline Diff Rendering (WPF)
```xml
<!-- DiffView.xaml -->
<ItemsControl ItemsSource="{Binding DiffLines}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <TextBlock>
                <Run Text="{Binding Text}" 
                     Foreground="{Binding Type, Converter={StaticResource DiffColorConverter}}"
                     TextDecorations="{Binding Type, Converter={StaticResource DiffDecorationConverter}}"/>
            </TextBlock>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

```csharp
// Using DiffPlex
var diff = new Differ();
var result = diff.CreateDiffs(original, corrected, false); // false = char-level

public List<DiffLine> DiffLines { get; } = new();

public void UpdateDiffView(CorrectionResult result)
{
    var diff = _differ.CreateDiffs(result.Original, result.Corrected);
    DiffLines.Clear();
    foreach (var piece in diff)
    {
        switch (piece.Type)
        {
            case ChangeType.Inserted:
                DiffLines.Add(new DiffLine { Text = piece.Text, Type = DiffType.Insert });
                break;
            case ChangeType.Deleted:
                DiffLines.Add(new DiffLine { Text = piece.Text, Type = DiffType.Delete });
                break;
            case ChangeType.Unchanged:
                DiffLines.Add(new DiffLine { Text = piece.Text, Type = DiffType.None });
                break;
        }
    }
}
```

---

## 4. System Tray + Floating Window Integration

### Hardcodet.NotifyIcon.Wpf Pattern
```xml
<!-- App.xaml Resources -->
<tb:TaskbarIcon x:Key="TrayIcon" 
                IconSource="/Assets/tray_enabled.ico"
                ToolTipText="GrammarFixer">
    <tb:TaskbarIcon.ContextMenu>
        <ContextMenu>
            <MenuItem Header="Correction Window" 
                      Command="{Binding ToggleCorrectionWindowCommand}"/>
            <MenuItem Header="Settings" Command="{Binding OpenSettingsCommand}"/>
            <Separator/>
            <MenuItem Header="Exit" Command="{Binding ExitCommand}"/>
        </ContextMenu>
    </tb:TaskbarIcon.ContextMenu>
</tb:TaskbarIcon>
```

```csharp
// TrayIconManager.cs
public ICommand ToggleCorrectionWindowCommand { get; }

private CorrectionWindow? _correctionWindow;

private void ToggleCorrectionWindow()
{
    if (_correctionWindow == null || !_correctionWindow.IsLoaded)
    {
        _correctionWindow = new CorrectionWindow(_ltClient);
        _correctionWindow.Closed += (_, _) => _correctionWindow = null;
        _correctionWindow.Show();
    }
    else
    {
        _correctionWindow.Close();
    }
}
```

---

## 5. Hotkey Management (Global + Window-Specific)

### HotkeyManager Pattern
```csharp
// Global hotkey (Ctrl+Alt+G) - existing
// Window hotkey (Ctrl+Alt+Shift+G) - NEW for CorrectionWindow

public class HotkeyManager
{
    private readonly Dictionary<int, Action> _hotkeys = new();
    
    public void Register(string keyCombo, Action callback)
    {
        var id = _hotkeys.Count + 1;
        var key = ParseKeyCombo(keyCombo);
        if (RegisterHotKey(_hwnd, id, key.Modifiers, key.Key))
            _hotkeys[id] = callback;
    }
    
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && _hotkeys.TryGetValue(m.WParam.ToInt32(), out var action))
            action();
        base.WndProc(ref m);
    }
}
```

### AppSettings Addition
```csharp
public class AppSettings
{
    // ... existing ...
    public string CorrectionWindowHotkey { get; set; } = "Ctrl+Alt+Shift+G";
    public double CorrectionWindowLeft { get; set; } = -1; // -1 = center
    public double CorrectionWindowTop { get; set; } = -1;
}
```

---

## 6. Recent GitHub Repos to Study (2022-2024)

| Repo | Description | Key Patterns |
|------|-------------|--------------|
| `languagetool-org/languagetool` | Core LT | Server API, n-gram loading |
| `krasjet/languagetool-desktop` | Electron + LT | Tray, auto-start, settings |
| `LanguageTool.NET` | C# client | HttpClient wrapper, models |
| `WpfTextCorrection` | WPF overlay | UIA, caret positioning |
| `DiffPlex/Wpf` | WPF diff | Inline diff rendering |

---

## 7. Implementation Checklist for GrammarFixer

### Phase 1: LanguageTool Integration
- [ ] Download `languagetool-server.jar` → `tools/`
- [ ] Add to csproj as `Content` with `CopyToOutputDirectory=PreserveNewest`
- [ ] Create `LanguageToolService` (process management)
- [ ] Create `LanguageToolClient` (HTTP API)
- [ ] Update `CorrectionPipeline` → inject LT client
- [ ] Update `App.xaml.cs` → LT service lifecycle

### Phase 2: CorrectionWindow (Floating UI)
- [ ] Create `UI/CorrectionWindow.xaml/.cs`
- [ ] TextArea with debounced auto-correct
- [ ] DiffPlex inline diff view (side-by-side or inline)
- [ ] Buttons: Copy, Clipboard, Send to Field
- [ ] Window position persistence (AppSettings)
- [ ] Topmost, modeless, remembers size/position

### Phase 3: Tray + Hotkey Integration
- [ ] Add "Correction Window" to tray menu
- [ ] Add `CorrectionWindowHotkey` to AppSettings
- [ ] Register hotkey in `HotkeyManager`
- [ ] Manage window lifecycle in `AppController`

### Phase 4: Polish
- [ ] Rolling diff as you type (green/red highlights)
- [ ] Click diff chunk → accept/reject individual change
- [ ] "Accept All" → copy to clipboard or send to field
- [ ] Graceful fallback if LT server not ready
- [ ] Unit tests for LT client + diff logic

---

## 8. Known Gotchas & Solutions

| Issue | Solution |
|-------|----------|
| LT server takes 20s+ to start | Poll `/v2/languages` with 30s timeout, show progress in tray tooltip |
| Single-file publish loses JAR | Add `<Content Include="tools\languagetool-server.jar" CopyToOutputDirectory="PreserveNewest" />` |
| Port 8081 in use | Try 8081, if busy try 8082-8090, store chosen port |
| Java not installed | Check `java -version` on startup, show tray balloon with install link |
| UIA fails in admin apps | Catch COMException, show "Cannot access elevated app" toast |
| DiffPlex char-level too noisy | Use word-level (`CreateDiffs(..., true)`) for cleaner view |
| Window positioning on multi-monitor | Clamp to screen bounds using `SystemParameters.VirtualScreenWidth` |
| Memory leak from LT client | Static `HttpClient` is fine; dispose `LanguageToolService` on exit |

---

## 9. Minimal Viable CorrectionWindow (Start Here)

```xml
<Window x:Class="GrammarFixer.UI.CorrectionWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Topmost="True"
        ShowInTaskbar="False" Background="Transparent"
        MinWidth="450" MinHeight="350">
    <Border Background="#1E1E1E" CornerRadius="8" BorderBrush="#333" BorderThickness="1"
            Effect="{StaticResource DropShadowEffect}">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/> <!-- Toolbar -->
                <RowDefinition Height="*"/>    <!-- Original text -->
                <RowDefinition Height="Auto"/> <!-- Divider -->
                <RowDefinition Height="*"/>    <!-- Corrected text -->
                <RowDefinition Height="Auto"/> <!-- Buttons -->
            </Grid.RowDefinitions>
            
            <!-- Toolbar -->
            <Border Grid.Row="0" Background="#252526" CornerRadius="8,8,0,0" Padding="8">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="GrammarFixer — Paste & Correct" Foreground="#CCC" VerticalAlignment="Center"/>
                    <Button Content="✕" Width="24" Height="24" Margin="8,0,0,0"
                            Click="Close_Click" Style="{StaticResource FlatButton}"/>
                </StackPanel>
            </Border>
            
            <!-- Original -->
            <TextBox x:Name="OriginalBox" Grid.Row="1" Margin="12,8" 
                     AcceptsReturn="True" VerticalScrollBarVisibility="Auto"
                     FontFamily="Consolas" FontSize="13"
                     Background="#1E1E1E" Foreground="#D4D4D4"
                     BorderBrush="#333" BorderThickness="1"
                     TextChanged="OriginalBox_TextChanged"/>
            
            <!-- Divider -->
            <Separator Grid.Row="2" Margin="12,4" Background="#333"/>
            
            <!-- Corrected (read-only with diff highlights) -->
            <ScrollViewer Grid.Row="3" Margin="12,0,12,8" VerticalScrollBarVisibility="Auto">
                <ItemsControl x:Name="DiffView" ItemsSource="{Binding DiffLines}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock FontFamily="Consolas" FontSize="13" TextWrapping="Wrap">
                                <Run Text="{Binding Text}" 
                                     Foreground="{Binding Type, Converter={StaticResource DiffColorConverter}}"
                                     TextDecorations="{Binding Type, Converter={StaticResource DiffDecorationConverter}}"/>
                            </TextBlock>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
            
            <!-- Buttons -->
            <StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Right" Margin="12,8">
                <Button Content="Copy Corrected" Click="CopyCorrected_Click" Margin="4" Padding="12,6"/>
                <Button Content="Apply to Clipboard" Click="ApplyClipboard_Click" Margin="4" Padding="12,6"/>
                <Button Content="Send to Field" Click="SendToField_Click" Margin="4" Padding="12,6"
                        Background="#007ACC" Foreground="White"/>
            </StackPanel>
        </Grid>
    </Border>
</Window>
```

This architecture is battle-tested in multiple 2022-2024 WPF grammar tools. The key insight: **keep the floating pill for quick fixes, add a separate modeless window for paste-and-correct workflow**.