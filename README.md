# SpectreBodies Plugin

A Path of Exile plugin for managing and highlighting spectre corpses with customizable colors and real-time detection.

## Screenshots

### Spectre Editor Interface
<img width="819" height="544" alt="image" src="https://github.com/user-attachments/assets/65d4d3c4-7251-4671-aa76-1ce14877476d" />


### In-Game Corpse Highlighting
<img width="309" height="157" alt="image" src="https://github.com/user-attachments/assets/11055ba9-5df6-4386-9761-fc461a299e3a" />

## Features

### Core Functionality
- Real-time Corpse Detection - Automatically detects and tracks dead monsters in your vicinity
- Spectre Management - Add, remove, and organize your spectre list with ease
- Custom Color Coding - Assign unique colors to individual spectres for visual identification
- Persistent Settings - All configurations and colors are saved between sessions

### Visual Features
- Inline Color Pickers - Click-to-edit color selection for each spectre
- Corpse Highlighting - Customizable circles and text labels for corpses
- Render Name Display - Shows both metadata and in-game names in green
- Background Text - Improved readability with customizable background colors

### Performance Optimizations
- Thread-Safe Operations - Concurrent-safe corpse collection and rendering
- Smart Caching - LRU cache system with size limits to prevent memory issues
- Frame-Based Updates - Optimized rendering with reduced per-frame overhead
- Efficient Filtering - Pre-filtered entity lists to minimize iteration costs

## Installation

1. Download the latest `SpectreBodies.dll` from the Releases page
2. Place the file in your `ExileApi/Plugins/Source` directory
3. Restart ExileApi (or Path of Exile if using standalone)
4. Configure settings in the ExileApi settings panel

## Configuration

### Basic Settings
| Setting | Description | Default |
|---------|-------------|----------|
| Enable Plugin | Toggle the entire plugin on/off | Enabled |
| Draw Distance | Maximum distance to detect corpses | 100 units |
| Update Interval | Corpse scanning frequency in milliseconds | 250ms |
| Max Recent Corpses | Limit for recently seen corpses list | 50 |

### Visual Settings
| Setting | Description | Default |
|---------|-------------|----------|
| Text Color | Default color for corpse labels | White |
| Background Color | Background for text labels | Black |
| Highlight Corpse | Toggle circle highlighting on/off | Enabled |
| Highlight Color | Color for corpse circles | Red |
| Highlight Radius | Size of highlight circles | 20 |
| Text Offset | Vertical offset for text labels | 50 |

### Display Options
| Setting | Description | Default |
|---------|-------------|----------|
| Show All Corpses | Display all dead monsters vs. only spectres | Disabled |
| Use Render Names | Show in-game names instead of metadata | Enabled |
| Spectre List | Comma-separated list of spectre metadata paths | Empty |

## Usage

### Opening the Editor

The Spectre Editor can be opened in two ways:

1. Hotkey Method (Recommended):
   - Press the configured hotkey (default: F6)
   - Hotkey can be customized in ExileAPI settings

### Managing Spectres

#### Adding Spectres

Manual Entry:
1. Type metadata path in the input field
2. Click "Add" button
3. Example: `Metadata/Monsters/Zombie/Zombie`

From Recent Corpses:
1. View "Recently Seen Corpses" section
2. Click the `+` button next to any corpse
3. Automatically adds to your spectre list

#### Removing Spectres
- Click the "Delete" button next to any spectre in your list

#### Customizing Colors
1. Click the color square next to any spectre
2. Use the inline color picker to adjust RGB values
3. Changes apply instantly to both text and highlights
4. Colors are automatically saved

### In-Game Features

#### Visual Indicators
- Text Labels: Shows spectre names above corpses
- Highlight Circles: Colored circles mark corpse locations
- Custom Colors: Your chosen colors override defaults
- Render Names: In-game names shown in green parentheses

## Changelog

### v2.0.0
- Added inline color pickers for each spectre
- Major performance optimizations and thread safety
- Fixed "Recently Seen Corpses" population issue
- Improved memory management with LRU caching
- Enhanced UI with better color integration

### v1.0.0
- Initial release
- Basic spectre management
- Corpse detection and highlighting
- Settings configuration

---

Enjoy your enhanced spectre management experience!
