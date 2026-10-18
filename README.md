# Nightmares Sound Generator

A local Windows desktop sound generator for the 2D game **Nightmares**, written in C# and WPF on .NET 10. Create stylized retro and textured effects, evolving cues, and procedural ambience loops, then export **signed 16-bit PCM WAV** files.

No samples or finished sounds are included. Starting configurations contain synthesis settings only. The app starts silently and writes no audio files until you choose **Export WAV**. It needs no account or network connection after installation.

## Run

Requires Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet restore NightmaresSound.slnx
dotnet run --project src/NightmaresSound.App
```

In VS Code, install the recommended C# extensions, open this folder, and press **F5** to build and debug. **Terminal → Run Task → run** starts the app without debugging; **Ctrl+Shift+B** builds the solution. A visual XAML designer is not required.

## Create a sound

1. Select a category and starting configuration, or search across all 26 configurations.
2. Press **Randomize** for a new variation in that configuration. It keeps the category, loop mode, sample rate, channels, normalization setting, and output level.
3. Press **Play**, then adjust Duration, Pitch, Brightness, Noise amount, and Output level. Nothing plays automatically. Changing settings stops the previous preview.
4. Use the expanded layer and effect panels for more control. Sliders have numeric fields beside them; hover for explanations. Numeric fields commit when focus leaves them. Invalid input has a red border and an explanatory tooltip; the previous valid setting remains in use.
5. Choose **44.1 or 48 kHz**, **mono or stereo**, then **Export WAV**. Bit depth is always 16-bit PCM. The waveform and preview use the same PCM bytes written into the WAV.

**Save recipe** stores the full settings and seed as JSON; **Open recipe** restores them. Undo/redo retains 100 edits during the current session. A slider drag is one edit. Save a recipe before closing to keep a sound you like. The same recipe and seed reproduce the same audio in this version.

| Category | Starting configurations |
| --- | --- |
| Loot and rewards | Material clink; Rare reward shimmer |
| Combat feedback | Weapon whoosh; Layered impact |
| Power attacks and boons | Electric burst; Charge and release |
| Enemy telegraphs | Warning pulse; Rising spell charge |
| Corruption and run phases | Ominous transition; Cleansing resolution |
| Weather and environment | Rain texture loop; Dark drone loop |
| Teleporters and traversal | Teleporter idle loop; Displacement and arrival |
| NPCs, dialogue and quests | Dialogue tick; Quest completion |
| Profession audio | Forge strike; Bubbling loop |
| Trading and crafting | Transaction chime; Crafting reveal |
| Inventory and equipment | Equipment movement; Slot click |
| UI and menus | Confirmation; Rejection |
| World interactions | Chest opening; Mineral break |

The Nightmares Sound Design Audit inspired these categories. They are stylized procedural interpretations, not realistic voices or environmental recordings.

## Sound controls

- Up to three independent layers: sine, triangle, square, sawtooth, white noise, or filtered noise. Square/saw use PolyBLEP edge correction to reduce aliasing.
- Each layer has level, start time, frequency, pitch sweep, ADSR envelope, gate, pan, and a sequence of up to 16 semitone offsets. For example, `0, 4, 7, 12` makes an ascending major arpeggio. Note length controls the spacing. Pan affects stereo only.
- Simple Pitch transposes all layers. Noise amount controls noise-layer volume and blends texture into tonal layers. Brightness and detailed Low-pass cutoff edit the same setting.
- Effects: low/high-pass filtering, vibrato, tremolo, saturation, 4–16-bit reduction, and feedback delay. Bit reduction is an effect; WAV export remains 16-bit.
- Duration is the complete file length, from **0.03 to 30 seconds**, including release and echoes. Envelopes scale to fit short notes. Layers starting beyond the duration are silent; long effect tails are bounded by the duration.
- Normalization sets the signal peak to **−1 dBFS**, then applies Output level (default 0 dB). Positive output gain can still clip. A warning appears when samples need limiting; reduce Output level if shown. Preview volume also depends on your Windows output device and system volume.

**Seamless loop** processes echo across the boundary and blends a 10 ms region on each side of the seam without adding silence or changing duration. Single-note loop layers form continuous beds and bypass their one-shot envelopes. Sequences repeat; use a duration that is a multiple of the full sequence length for a regular rhythmic cycle. Start times still delay layers within each cycle. All oscillator phases and movements are reconciled at the blended seam; this can slightly soften textures near the join.

Exported loops contain one cycle as ordinary PCM WAV data. **Enable looping in your game engine**; the file does not contain engine-specific loop flags. Set 2D positional/spatial playback in the game engine as appropriate.

## Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| Randomize | Ctrl+R |
| Play / Stop | F5 / Esc |
| Undo / Redo | Ctrl+Z / Ctrl+Y |
| Save / Open recipe | Ctrl+S / Ctrl+O |
| Export WAV | Ctrl+E |
| Search | Ctrl+F |

Tab navigates controls. Arrow keys adjust focused sliders; numeric fields support precise input. The toolbar remains visible while the three editing panels scroll independently. The window adapts to available desktop space and can be resized down to 880 × 520 logical pixels.

## Build, test, and publish

```powershell
dotnet build NightmaresSound.slnx
dotnet test NightmaresSound.slnx
dotnet publish src/NightmaresSound.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish/win-x64
```

Run `artifacts/publish/win-x64/NightmaresSound.exe`. The self-contained executable includes .NET; the destination PC does not need an SDK. Publishing requires the Windows runtime packages during restore. VS Code also includes a **publish Windows executable** task.

### Project structure

- `src/NightmaresSound.Core`: recipe model and validation, catalog/randomization, deterministic DSP, cancellable render coordinator, PCM playback cursor, WAV writer, and history. No UI or audio-device dependencies.
- `src/NightmaresSound.App`: WPF/MVVM editor, waveform, Windows file dialogs, and NAudio 3.1 playback.
- `tests/NightmaresSound.Tests`: synthesis, reproducibility, PCM/WAV, loops, persistence, cancellation, history, and failed-write tests. Rendered audio stays in memory.
- `tests/NightmaresSound.DesktopTests`: Windows/STA view-model, binding, layout, and unavailable-device tests. It renders interface PNGs under ignored `artifacts/ui-checks`; it does not play sounds.

The core tests can run independently using `dotnet test tests/NightmaresSound.Tests`. The desktop application and desktop tests require Windows.

## Files and troubleshooting

Recipes use schema version 1 and include the seed, configuration ID, all layer/effect controls, loop mode, and output settings. Invalid or unsupported files are rejected without replacing your current recipe. Saves and exports use a temporary file and replace the destination only after writing succeeds.

The last successful export folder is remembered in `%LOCALAPPDATA%/NightmaresSound/settings.json`. This preference is written only after a WAV export. No audio or recipe is saved there automatically.

If playback fails because an output device is missing or disconnected, the app displays an error and keeps **WAV export** available. Restore/select your Windows output device and press Play to retry. Shared-mode playback may be converted to the device's mix format by Windows; the exported WAV keeps your chosen rate and channels. If a file cannot be written, select a writable folder or close the program locking that file.

This version intentionally exports one sound at a time. It does not import samples, generate music arrangements, batch-export variations, or modify the game project.
