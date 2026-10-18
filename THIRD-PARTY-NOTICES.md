# Third-party notices

## Bundled acoustic instruments — CC0 1.0

Harp (folk harp), recorder (bass recorder), and frame drum recordings originate
from Versilian Studios / Sam Gossner's Versilian Community Sample Library:
https://github.com/sgossner/VCSL/tree/c1ea7bcc3c7309650ab0da9d15c9cd1fbc4a4c7e

Fiddle recordings are bowed solo violin recordings from Versilian Studios /
Sam Gossner's VSCO 2 Community Edition:
https://github.com/sgossner/VSCO-2-CE/tree/440300901dfe9275fd84e0b7763af1f8443ae62e

Both source repositories include the CC0 1.0 public-domain dedication. The
publisher also confirms VSCO 2 CE's CC0 license at:
https://versilian-studios.com/vsco-community/

Original license texts are included in Samples/VCSL-LICENSE.txt and
Samples/VSCO-2-CE-LICENSE.txt beside the application, and embedded in the core
assembly. Samples/manifest.json lists each original file's immutable URL,
creator, license URL, root MIDI pitch and supported range, SHA-256 hashes, and
processing notes. Recordings are averaged to mono, trimmed, DC corrected,
peak adjusted, faded, and converted to PCM16. Runtime playback uses the nearest
recorded root, with at most four semitones of resampling. The drum has nominal
MIDI root 48 for tuning; it is an unpitched percussion recording.

These are instrument recordings, not precomposed musical phrases. The app
generates the phrase patterns itself. No guitar, lute, or dulcimer recordings
are included.

## NAudio 3.1.0

This application uses NAudio.Core and NAudio.Wasapi for Windows playback.
Source and license: https://github.com/naudio/NAudio

Copyright 2020 Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## .NET

Self-contained builds include the Microsoft .NET runtime and WPF.
Runtime source, license, and dependency notices:

- https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
- https://github.com/dotnet/wpf/blob/main/LICENSE.TXT
- https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT

.NET is copyright .NET Foundation and Contributors and licensed under the MIT license.
