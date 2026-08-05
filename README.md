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
- Spectre Database - A bundled, curated database of world-findable spectres (names, roles, abilities, and where to find them). Known spectres show a friendly name instead of their raw metadata path in the editor, recent-corpses list, and on the ground label.

### Visual Features
- Inline Color Pickers - Click-to-edit color selection for each spectre
- Corpse Highlighting - Customizable circles and text labels for corpses
- Render Name Display - Shows both metadata and in-game names in green
- Background Text - Improved readability with customizable background colors

### Performance Optimizations
- Thread-Safe Operations - Concurrent-safe corpse collection and rendering
- Smart Caching - Size-capped cache with limits to prevent memory issues
- Frame-Based Updates - Entity cache refreshed every 10 frames to reduce per-frame overhead
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
| Draw Distance | Maximum distance to detect corpses | 400 units |
| Update Interval | Corpse scanning frequency in milliseconds | 250ms |
| Max Recent Corpses | Limit for recently seen corpses list | 10 |

### Visual Settings
| Setting | Description | Default |
|---------|-------------|----------|
| Text Color | Default color for corpse labels | White |
| Background Color | Background for text labels | Black |
| Highlight Corpse | Toggle circle highlighting on/off | Enabled |
| Highlight Color | Color for corpse circles | Yellow |
| Highlight Radius | Size of highlight circles | 12 |
| Highlight Segments | Number of segments for circle smoothness | 12 |
| Highlight Z-Offset | Vertical offset for highlight circles | 0 |
| Text Offset | Vertical offset for text labels | 20 |

### Display Options
| Setting | Description | Default |
|---------|-------------|----------|
| Show All Nearby Corpse Metadata | Display all dead monsters (metadata) vs. only spectres | Disabled |
| Use Render Names | Show in-game names instead of metadata | Enabled |
| Spectre List | Comma-separated list of spectre metadata paths | Ships with 3 defaults (see below) |

> **Default Spectre List:** `Metadata/Monsters/KaomWarrior/KaomWarrior7`, `Metadata/Monsters/WickerMan/WickerMan`, `Metadata/Monsters/Miner/MinerLantern`

## Bundled Spectre Database

The plugin ships a small, curated database of **world-findable** spectres (embedded as `Data/spectre-data.json`). When a corpse's metadata matches a database entry, its friendly name is shown instead of the raw metadata path — in the editor list, the recently-seen corpses panel, and on the ground label.

Scope notes:
- **World-findable only.** Spectres that only come from itemized corpses (the Ritual / "King in the Mists" corpse *items*, e.g. the "Perfect *" family: Guardian Turtle, Forest Warrior, Spirit of Fortune, Hydra, etc.) are deliberately **excluded** — this plugin helps you find spectres as corpses in the world, and those can't be found that way.
- Each entry carries: role (Damage/Utility), tags, tier, progression phase, acquisition zone/mechanic, and a short note.
- **Confirmed** entries are established world spectres. **Untested** entries (new 3.29 `DeepwaterLeague` monsters) are flagged experimental — the league is new and the community hasn't evaluated them yet.

Examples of included spectres: Forged Frostbearer (Verisium Ore), Syndicate Operative (Betrayal Research), Carnage/Host Chieftain (Act 2), Undying Evangelist (Act 3), Spectral Leader (T17 / action-speed aura), Wretched Defiler (T17), Primal Crushclaw (Harvest), and the new 3.29 ocean monsters as Untested.

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
- Improved memory management with size-capped caching
- Enhanced UI with better color integration

### v1.0.0
- Initial release
- Basic spectre management
- Corpse detection and highlighting
- Settings configuration

---

Enjoy your enhanced spectre management experience!
