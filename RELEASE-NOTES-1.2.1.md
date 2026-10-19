# Retro Sounds Generator 1.2.1

## Changes

- Combine up to three medieval instrument voices with separate instrument, style, phrase, octave, tuning, level, and stereo pan. Key, scale, mode, tempo, effects, and output remain shared.
- Acoustic pitch, tuning, and vibrato limits account for all enabled sampled voices. Non-looping audio protects the longest release; phrase loops remain two bars.
- New schema 2 recipes store the ensemble. Schema 1 instrument recipes migrate to one enabled voice; synthesis recipes retain their behavior.

## Confirmed defects and fixes

- Losing slider mouse capture left an undo gesture open, grouping the next unrelated edit with it. A failing regression reproduced this; capture loss, drag completion, and keyboard focus loss now finish the gesture.
- Audio disposal exceptions escaped window closure. A failing device-teardown regression reproduced this crash path; closure now contains teardown failures, detaches callbacks, cancels rendering, and disables playback/export. Disposal is idempotent.
- Numeric editors now share a refresh guard so model-to-view range updates cannot write through another editor. A regression verifies that such writes cannot alter the recipe or history.
- Adding voices or changing sliders during an active render threw an expected `OperationCanceledException` from the render worker. Although caught by the coordinator, this could interrupt debugging. The editor now uses cooperative `TryRender` cancellation, returns no partial audio, and cancels debounce without awaiting a cancelled task. Tests detect the old throwing behavior and verify no first-chance cancellation exceptions when replacing or disposing a three-voice render. The synchronous `Render` API retains its existing throwing cancellation contract.
- Changing a looping acoustic phrase to an out-of-range note threw `InvalidDataException` and then `ArgumentException` through the binding setter; the dropdown could display a rejected note. Failing regressions reproduced both defects. Shared and per-voice musical edits now preflight acoustic coverage without throwing, identify the affected voice, restore the valid selection, and preserve the recipe, undo history, rendered buffer, and active playback. Core recipe validation still strictly rejects unsupported acoustic pitches.

## Verification

- `dotnet test NightmaresSound.slnx --no-restore -c Release`: **130 core + 35 desktop tests passed**, no failures or skips.
- Automated WPF coverage includes thumb drag events, track commands, keyboard endpoints, rapid edits, disjoint range/preset transitions, capture interruption, fractional acoustic tuning, undo/redo, and closure during pending renders.
- Audio coverage includes every instrument/style, deterministic multi-voice mixing, muted voices, pan, longest tails, exact phrase-loop length, save/open migration, malformed voices, and preview/export PCM equality.
- Rendered interface captures inspected at minimum window size and scaled DPI; no layered-control binding errors found.
- Self-contained Windows x64 executable published successfully. Startup reached input idle, the window responded with the 1.2.1 title, and normal shutdown returned exit code 0.
- `git diff --check` passed.

## Remaining verification limits

The subsequently reported three-voice cancellation exception and looping acoustic note-change exception were reproduced and corrected with first-chance exception regression coverage. Other slider crashes remain unreproduced. Physical mouse-driven interactive testing was unavailable because the computer-use runtime failed during sandbox helper initialization. WPF event tests and rendered layout inspections passed; this does not establish that every possible hardware, driver, or interaction-specific crash is eliminated. A live audio-device disconnect was simulated through the preview interface rather than performed on the user's hardware.

## Windows build

`artifacts/publish/win-x64/NightmaresSound.exe` — self-contained, version 1.2.1, 149,535,572 bytes.

SHA-256: `E15B33B4138DD3127BAEE6A33184FF87E283EA90F0C6E50D5D721F628AEA56C0`

Keep the accompanying license and third-party notices when distributing the build. Older app versions cannot open newly saved schema 2 recipes.
