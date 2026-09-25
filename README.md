# Audio System

Audio System is a lightweight, asset-driven playback system for Unity. An
`Audio` asset contains its clips and playback rules, so common sounds can be
triggered directly without adding an `AudioSource` to every scene object.

## Requirements

- Unity 6 (`6000.0`) or newer.

## Installation

Install the package from a Git URL in Unity Package Manager:

```text
https://github.com/EmmanuelCouto/audio-system.git#v1.0.0
```

This URL assumes the dedicated repository contains `package.json` at its root.

## Setup

1. Open **Edit > Project Settings > Audio System**.
2. Configure the main `AudioMixer`, categories and fallback icon.
3. Configure the initial and maximum voice counts.
4. Create `Audio` assets from **Assets > Create > Audio** or from selected
   `AudioClip` assets.

The package stores editable project settings at
`Assets/Settings/AudioSystem/AudioSystemSettings.asset`. Package defaults
remain inside the package and are not modified.

The default mixer is copied to
`Assets/Settings/AudioSystem/Main.mixer`, where its groups and effects can be
edited normally. The mixer stored in the package is only a creation and
migration template.

## Basic usage

```csharp
using Emmanuel.AudioSystem;
using UnityEngine;

public sealed class PlayerAudio : MonoBehaviour
{
    [SerializeField] private Audio movementAudio;

    public void PlayMovement()
    {
        movementAudio.Play();
    }
}
```

The parameterless methods on `Audio` can also be selected in UnityEvents.

Use a handle only when gameplay needs to control one specific playback:

```csharp
AudioHandle handle = movementAudio.PlayTracked();
handle.Pause();
handle.Resume();
handle.FadeOut(0.25f);
```

Per-call overrides are available through `AudioPlayOptions`:

```csharp
AudioHandle handle = movementAudio.PlayTracked(
    new AudioPlayOptions(
        volumeMultiplier: 0.5f,
        pitch: 1.1f,
        fadeIn: 0.2f,
        normalizedStartTime: 0.4f));
```

## License

This package is available under the MIT License. See [LICENSE.md](LICENSE.md).

## Versioning

- Patch versions, such as `1.0.1`, contain bug fixes and small corrections.
- Minor versions, such as `1.1.0`, contain completed features or larger
  functional increments.
- Major versions are reserved for intentionally incompatible public API or
  serialization changes.
