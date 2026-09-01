# Background music

## Exploration Theme

- Author: Cleyton Kauffman
- Source: https://opengameart.org/content/exploration-theme
- License: Creative Commons Zero (CC0 1.0)
- License deed: https://creativecommons.org/publicdomain/zero/1.0/
- Runtime file: `exploration_theme.ogg`
- Source duration: 2:14
- Source format: 16-bit stereo, 44.1 kHz
- Loop: the source author identifies the track as seamlessly loopable

Attribution is optional under CC0. The project may still display the following
credit as a courtesy:

> "Exploration Theme" by Cleyton Kauffman (CC0)

## Runtime treatment

`BackgroundMusicPlayer.cs` starts the OGG on the dedicated `Music` bus, keeps
the imported stream looping, and smoothly ducks it by 8 dB while Tutor speech
is active. This keeps the light synth/piano ambience beneath dialogue and UI
feedback instead of competing with them.
