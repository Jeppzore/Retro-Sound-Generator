# Retro Sounds Generator 1.2.1 — Nightmares Studio

A local Windows desktop sound generator for the 2D game **Nightmares**, written in C# and WPF on .NET 10. Create stylized retro and textured effects, evolving cues, and procedural ambience loops, then export **signed 16-bit PCM WAV** files.

The app starts silently and writes no audio files until you choose **Export WAV**. It needs no account or network connection after installation. Version 1.2.1 adds up to three medieval instrument voices and strengthens slider refresh and interrupted-gesture handling. The dark studio interface retains its educational colors, with bundled CC0 acoustic recordings for four instruments.

See [1.2.1 release notes](RELEASE-NOTES-1.2.1.md) for verified fixes, test results, and remaining verification limits.

## Educational color guide

The charcoal interface uses subtle texture around solid editing surfaces. A labeled color guide above the sound controls explains the accents:

- **Orange — Pitch & music:** pitch, sweep, key, scale, octave, phrases, and note offsets.
- **Purple — Timbre & effects:** sound sources, instrument styles, brightness, filters, noise, distortion, and effect depth/mix.
- **Blue — Timing & envelopes:** duration, tempo, envelopes, modulation speeds, delay time, and looping.
- **Teal — Levels & output:** gain, pan, normalization, output format, and the waveform.

Numeric controls carry matching row markers and concept names in their help tooltips. Brightness and Low-pass cutoff share a color because they edit the same setting. Error messages and invalid fields use red; clipping warnings use yellow. Labels and warning text retain meaning without color. Windows High Contrast uses the system palette instead of decorative colors and texture. File dialogs and the window frame follow Windows settings.

Version 1.2.1 preserves the three-column workflow, shortcuts, preview, synthesis, and WAV export behavior. New recipes use schema version 2; version 1 recipes open automatically.

A presentation bug found during accessibility testing was fixed: instrument labels now tolerate an empty binding value while WPF tears down or rebuilds a control template.

## Medieval instruments

Choose **Instruments · Medieval** in the category list, then select guitar, lute, harp, hammered dulcimer, recorder, fiddle, or frame drum. Every instrument has a distinct **Retro** synthesized voice. Harp, recorder, fiddle, and frame drum also offer **Acoustic** playback using bundled recordings. Guitar, lute, and dulcimer remain Retro-only; other instruments' recordings are never substituted.

Choose **Single note / hit** or **Phrase**. Phrases last two bars in 4/4: ascending/descending arpeggios for plucked strings, strummed chords for guitar/lute, modal melodies for recorder/fiddle, and march/dance rhythms for frame drum. Set key, scale (major, natural minor, Dorian, or Mixolydian), octave 3–5, and tempo 40–200 BPM. Defaults are D Dorian, octave 4, 90 BPM. Single-note mode plays the key's root; pitch and scale controls do not affect drum patterns.

Combine up to **three medieval instrument voices**. Voice 1 starts enabled; Voices 2 and 3 start disabled. Each voice has its own instrument, Retro/Acoustic style, compatible phrase, octave, level, tuning (−36 to +36 semitones), and stereo pan. Enable additional voices to blend instruments; all three can be muted. Changing a voice's instrument preserves compatible settings and selects a supported style/phrase when necessary. Selecting another starting configuration resets the ensemble to its single-voice default.

Key, scale, mode, tempo, master pitch, noise, effects, normalization, output, and WAV settings are shared. Phrase choices follow each instrument; all phrases start together on the same two-bar grid. Voices mix before shared effects and normalization. Pan has no effect in mono. Oscillator layers remain available in synthesis configurations. Duration protects the longest enabled voice's release. Instrument phrase loops repeat exactly two bars, wrapping overlapping releases across the boundary. Preview and export use identical PCM bytes. Randomize varies seed, attack texture, and brightness while preserving every voice's composition, musical settings, level, and pan.

Acoustic playback uses the nearest recorded pitch and permits at most four semitones of transposition per sample. Supported sounding ranges, including master pitch, voice tuning, and vibrato, are C3–C6 for harp, C4–G♯5 for recorder, and C4–C♯6 for fiddle. The drum permits ±4 semitones of tuning. Master pitch/vibrato sliders respect every enabled acoustic voice; each voice's tuning slider respects its own recordings. An incompatible musical edit shows a validation message and retains the previous recipe; select Retro for a wider range. The acoustic recorder uses a bass recorder, the harp a folk harp, and the fiddle a bowed solo violin. These are medieval-inspired sounds, not claims of historical reconstruction.

Changing a note, octave, mode, scale, or phrase while looping checks every enabled acoustic voice before applying the edit. An out-of-range selection restores the last valid selection, identifies the affected voice, and keeps the current loop playing. Expected acoustic range rejections do not throw through UI bindings or interrupt the debugger.

The bank contains 23 processed samples (mono PCM16, up to four seconds each). Original creators, pinned source URLs, source and bundled SHA-256 hashes, mappings, processing notes, and original CC0 licenses are retained in `src/NightmaresSound.Core/Samples`. Samples are embedded for offline and single-file operation. See [third-party notices](THIRD-PARTY-NOTICES.md). To regenerate the bank with Python 3 and network access, run `python scripts/bundle-instruments.py`. A custom sequencer and sample importer are outside this release.

## Run

Requires Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet restore NightmaresSound.slnx
dotnet run --project src/NightmaresSound.App
```

In VS Code, install the recommended C# extensions, open this folder, and press **F5** to build and debug. **Terminal → Run Task → run** starts the app without debugging; **Ctrl+Shift+B** builds the solution. A visual XAML designer is not required.

## Create a sound

1. Select a category and starting configuration, or search across all 33 configurations.
2. Press **Randomize** for a new variation in that configuration. It keeps the category, loop mode, sample rate, channels, normalization setting, and output level.
3. Press **Play**, then adjust Duration, Pitch, Brightness, Noise amount, and Output level. Nothing plays automatically. Changing settings stops the previous preview.
4. Use the expanded layer and effect panels for more control. Sliders have numeric fields beside them; hover for explanations. Numeric fields commit when focus leaves them. Invalid input has a red border and an explanatory tooltip; the previous valid setting remains in use.
5. Choose **44.1 or 48 kHz**, **mono or stereo**, then **Export WAV**. Bit depth is always 16-bit PCM. The waveform and preview use the same PCM bytes written into the WAV.

**Save recipe** stores the full settings and seed as JSON; **Open recipe** restores them. Undo/redo retains 100 edits during the current session. A slider drag is one edit. Save a recipe before closing to keep a sound you like. The same recipe and seed reproduce the same audio in this version.

Sliders clamp to exact accepted endpoints, including floating-point rounding near a limit. Instrument Duration reserves the release tail; phrase-loop Duration is fixed and its slider is disabled. Acoustic Pitch, Tuning, and Vibrato depth ranges update together to keep every note within the bundled recordings. Refreshing one control cannot write changes through another control. Losing mouse capture or keyboard focus ends the slider's undo gesture. Typed numeric input is validated without throwing from the binding setter, and accepts decimal points or your Windows locale's decimal separator.

Rapid edits cancel obsolete background renders cleanly without throwing cancellation exceptions. This also prevents debugger interruptions when adding voices or adjusting sliders while a previous render is still running. The core's synchronous `Render` API retains its throwing cancellation contract; the editor uses `TryRender`, which returns no result when cancelled.

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
| Instruments · Medieval | Guitar; Lute; Harp; Hammered dulcimer; Recorder; Fiddle; Frame drum |

The Nightmares Sound Design Audit inspired the original effects categories. Those configurations remain stylized procedural interpretations; the new Instruments category also includes acoustic recordings.

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

New recipes use schema version 2 and include the seed, configuration ID, all layer/effect controls, loop mode, output settings, shared musical settings, and an array of one to three instrument voices. Version 1 recipes migrate automatically; their instrument becomes one enabled voice with the original sound, and synthesis recipes retain their behavior. Older app versions cannot open schema 2 files. Invalid or unsupported files are rejected without replacing your current recipe. Saves and exports use a temporary file and replace the destination only after writing succeeds.

The last successful export folder is remembered in `%LOCALAPPDATA%/NightmaresSound/settings.json`. This preference is written only after a WAV export. No audio or recipe is saved there automatically.

If playback fails because an output device is missing or disconnected, the app displays an error and keeps **WAV export** available. Restore/select your Windows output device and press Play to retry. Shared-mode playback may be converted to the device's mix format by Windows; the exported WAV keeps your chosen rate and channels. If a file cannot be written, select a writable folder or close the program locking that file.

This version intentionally exports one sound or preset phrase at a time. It does not import samples, create full music arrangements, batch-export variations, or modify the game project.
