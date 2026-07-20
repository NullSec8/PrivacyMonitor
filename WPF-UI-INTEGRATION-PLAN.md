# WPF-UI Integration Plan

## Overview
Integrate WPF-UI (Fluent Design) into PrivacyMonitor to modernize the UI while preserving existing functionality.

## Phase 1: Foundation

### 1.1 Add NuGet Package
**File:** `wpf-browser/PrivacyMonitor.csproj`
```xml
<PackageReference Include="WPF-UI" Version="4.3.0" />
```

### 1.2 Update App.xaml
**File:** `wpf-browser/App.xaml`

Add WPF-UI namespaces and resource dictionaries:
```xml
<Application x:Class="PrivacyMonitor.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- WPF-UI Fluent system -->
                <ui:ThemesDictionary Theme="Dark" />
                <ui:ControlsDictionary />
                <!-- Your custom themes layer on top -->
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

### 1.3 Update App.xaml.cs
**File:** `wpf-browser/App.xaml.cs`

Replace manual theme swapping with WPF-UI's manager:
```csharp
using Wpf.Ui.Appearance;

private void ApplySystemTheme()
{
    bool isDark = SystemThemeDetector.IsDarkMode;
    ApplicationThemeManager.Apply(
        isDark ? ApplicationTheme.Dark : ApplicationTheme.Light
    );
    // Keep your custom theme overrides (Large accessibility, etc.)
}
```

## Phase 2: MainWindow Conversion

### 2.1 MainWindow.xaml
**File:** `wpf-browser/MainWindow.xaml`

Change root element:
```xml
<!-- FROM -->
<Window x:Class="PrivacyMonitor.MainWindow" ...>

<!-- TO -->
<ui:FluentWindow x:Class="PrivacyMonitor.MainWindow"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    ExtendsContentIntoTitleBar="False"
    WindowBackdropType="None"
    ...>
```

### 2.2 MainWindow.xaml.cs
**File:** `wpf-browser/MainWindow.xaml.cs`

Change base class:
```csharp
// FROM
public partial class MainWindow : Window

// TO
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
```

## Phase 3: Control Conversions

### 3.1 Button Conversions
Replace custom styles with WPF-UI buttons:
```xml
<!-- Primary button -->
<ui:Button Appearance="Primary" Click="Handler">Save</ui:Button>

<!-- Secondary button -->
<ui:Button Appearance="Secondary" Click="Handler">Cancel</ui:Button>
```

### 3.2 Card Conversions
Replace Border-based cards with ui:Card:
```xml
<!-- FROM -->
<Border Style="{StaticResource Card}" ...>
    <StackPanel>...</StackPanel>
</Border>

<!-- TO -->
<ui:Card ...>
    <StackPanel>...</StackPanel>
</ui:Card>
```

### 3.3 Other Controls
- `CheckBox` → automatic Fluent styling
- `ProgressBar` → `ui:ProgressBar`
- `Separator` → remove custom style, use default

## Phase 4: Notifications

### 4.1 Add Snackbar
For transient notifications:
```xml
<ui:SnackbarPresenter x:Name="SnackbarPresenter" />
```

```csharp
Snackbar.Show("Tab closed", "Undo", (sender, args) => { /* undo */ });
```

### 4.2 Replace MessageBox
```csharp
// FROM
MessageBox.Show("Are you sure?", "Confirm", MessageBoxButtons.YesNo);

// TO
var dialog = new ui.ContentDialog
{
    Title = "Confirm",
    Content = "Are you sure?",
    PrimaryButtonText = "Yes",
    SecondaryButtonText = "No"
};
await dialog.ShowAsync();
```

## Files Modified

| File | Changes |
|------|---------|
| `PrivacyMonitor.csproj` | Add WPF-UI package |
| `App.xaml` | Add Fluent themes |
| `App.xaml.cs` | Use ApplicationThemeManager |
| `MainWindow.xaml` | FluentWindow conversion |
| `MainWindow.xaml.cs` | Base class change |
| `Themes/*.xaml` | No changes needed (custom keys preserved) |

## What Stays the Same
- All 4 custom theme files (unique brush names, no collision)
- WebView2 integration (stays in plain Grid)
- Tab bar implementation (Chrome-style, too custom for WPF-UI)
- SystemThemeDetector (can coexist or replace later)
- All business logic and functionality

## Testing Checklist
- [ ] App builds without errors
- [ ] Window renders correctly
- [ ] Tabs work (create, close, switch)
- [ ] WebView2 loads pages
- [ ] Sidebar panels display
- [ ] Bookmarks popup works
- [ ] Task manager popup works
- [ ] Crash overlay works
- [ ] Theme switching works (light/dark)
- [ ] All buttons clickable
- [ ] Notifications appear correctly
