"""Rebuild the small, pinned CC0 acoustic bank. Python 3, standard library only.

Run from any directory: python scripts/bundle-instruments.py
No runtime download is needed by the application.
"""
import hashlib
import io
import json
from pathlib import Path
import struct
import urllib.parse
import urllib.request
import wave

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'src/NightmaresSound.Core/Samples'
VCSL = 'c1ea7bcc3c7309650ab0da9d15c9cd1fbc4a4c7e'
VSCO = '440300901dfe9275fd84e0b7763af1f8443ae62e'


def fetch(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'Retro-Sound-Generator'}), timeout=60) as response:
        return response.read()


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    licenses = [
        ('VCSL', VCSL, 'LICENSE'),
        ('VSCO-2-CE', VSCO, 'LICENSE'),
    ]
    for repo, commit, file in licenses:
        url = f'https://raw.githubusercontent.com/sgossner/{repo}/{commit}/{file}'
        data = fetch(url)
        if b'CC0' not in data and b'Creative Commons' not in data:
            raise ValueError(f'Unexpected license: {url}')
        (DEST / f'{repo}-LICENSE.txt').write_bytes(data)

    entries = []
    # VCSL uses C3 for sounding MIDI 60; VSCO uses C4 for MIDI 60.
    # These mappings were checked against the recorded fundamentals.
    for note, midi in [('C2', 48), ('E2', 52), ('G#2', 56), ('C3', 60), ('E3', 64), ('G#3', 68), ('C4', 72), ('E4', 76), ('G#4', 80)]:
        entries.append(('harp', midi, 'VCSL', VCSL, f'Chordophones/Composite Chordophones/Folk Harp/EWHarp_Normal_{note}_v3_RR1.wav', 48, 84))
    for note, midi in [('C3', 60), ('E3', 64), ('G#3', 68), ('C4', 72), ('E4', 76)]:
        entries.append(('recorder', midi, 'VCSL', VCSL, f'Aerophones/Edge-blown Aerophones/Baroque Bass Recorder/Sustain/BassRecorder_Sus_{note}_rr1_Main.wav', 60, 80))
    for note, midi in [('C4', 60), ('E4', 64), ('G4', 67), ('A4', 69), ('C5', 72), ('E5', 76), ('G5', 79), ('A5', 81)]:
        entries.append(('fiddle', midi, 'VSCO-2-CE', VSCO, f'Strings/Solo Violin/Arco Vib/LLVln_ArcoVib_{note}_f.wav', 60, 85))
    entries.append(('frame-drum', 48, 'VCSL', VCSL, 'Membranophones/Struck Membranophones/Frame Drum/HDrumL_Hit_v3_rr1_Sum.wav', 44, 52))
    manifest = []
    for instrument, midi, repo, commit, path, low, high in entries:
        url = f'https://raw.githubusercontent.com/sgossner/{repo}/{commit}/' + urllib.parse.quote(path, safe='/')
        raw = fetch(url)
        with wave.open(io.BytesIO(raw)) as wav:
            channels, width, rate = wav.getnchannels(), wav.getsampwidth(), wav.getframerate()
            if width not in (2, 3):
                raise ValueError(f'Unsupported PCM: {path}')
            data = wav.readframes(wav.getnframes())
        values = [int.from_bytes(data[i:i + width], 'little', signed=True) / 2 ** (width * 8 - 1) for i in range(0, len(data), width)]
        mono = [sum(values[i:i + channels]) / channels for i in range(0, len(values), channels)]
        peak = max(map(abs, mono))
        onset = next(i for i, value in enumerate(mono) if abs(value) > max(.0005, peak * .005))
        start = max(0, onset - int(rate * .003))
        mono = mono[start:start + rate * 4]
        dc = sum(mono) / len(mono)
        mono = [x - dc for x in mono]
        peak = max(map(abs, mono))
        fade = int(rate * .03)
        for i in range(len(mono)):
            gain = min(1, i / (rate * .001), (len(mono) - 1 - i) / fade)
            mono[i] = round(mono[i] / peak * .7 * max(0, gain) * 32767)
        filename = f'{instrument}-{midi}.wav'
        with wave.open(str(DEST / filename), 'wb') as wav:
            wav.setparams((1, 2, rate, len(mono), 'NONE', 'not compressed'))
            wav.writeframes(struct.pack(f'<{len(mono)}h', *mono))
        manifest.append(dict(InstrumentId=instrument, File=filename, RootMidi=midi, MinMidi=low, MaxMidi=high,
            SourceUrl=url, Creator='Versilian Studios / Sam Gossner', License='CC0-1.0',
            LicenseUrl=f'https://github.com/sgossner/{repo}/blob/{commit}/LICENSE',
            LicenseEvidence=f'{repo}-LICENSE.txt', SourceSha256=hashlib.sha256(raw).hexdigest(),
            BundledSha256=hashlib.sha256((DEST / filename).read_bytes()).hexdigest(),
            Processing=f'Stereo averaged to mono; leading silence trimmed ({start} frames); DC removed; maximum 4 s; peak -3.10 dBFS; 1 ms attack/30 ms tail fades; PCM16 at {rate} Hz. Nearest-root playback with linear interpolation, maximum 4-semitone shift. VCSL pitched filenames use C3=MIDI60; VSCO uses C4=MIDI60. Drum root is nominal/unpitched.'))
        print(filename, len(mono), 'frames', flush=True)
    (DEST / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
