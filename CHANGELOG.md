# Changelog

All notable changes to this package are documented in this file.

## [1.0.1] - 2026-09-25

- Fixed the default Main AudioMixer remaining read-only inside the package.
- Added automatic creation of an editable mixer in
  `Assets/Settings/AudioSystem/Main.mixer`.
- Moved the editable settings asset to
  `Assets/Settings/AudioSystem/AudioSystemSettings.asset`.
- Added migration of settings, categories and Audio assets from the packaged
  mixer groups to their editable project equivalents.
- Added automatic category group matching when the Main AudioMixer changes,
  preferring the category name and then the previously assigned group name.

## [1.0.0] - 2026-09-19

- Added asset-driven audio playback with UnityEvent-friendly commands.
- Added pooled and positional playback with optional instance handles.
- Added mixer categories and centralized project settings.
- Added overlap, single-instance and group-exclusive playback policies.
- Added instance limits, looping modes, fades, pitch variation and spatial
  audio settings.
- Added custom inspectors, waveform previews, icon categories and icon colors.
