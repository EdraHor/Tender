# Architecture

> Engine: Unity 6000.x, URP. Philosophy: **keep it simple and teachable** (see `CLAUDE.md`).

## Startup & global wiring

```
AppBootstrap  [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]
  └─ spawns DontDestroyOnLoad managers if missing:
       InputController, SaveManager, GraphicsManager, AudioManager
  └─ warms G.Save / G.Input / G.Dialogue / G.Player / G.HUDController

G  (static service locator, Core/G.cs)
  Input · Save · Player · Graphics · Audio · Dialogue · HUDController
  → each lazily FindObjectsByType<T>() and caches; ResetStatics() on domain reload

SceneBootstrap  (per scene)
  └─ sets initial input mode: Player | UI | Dialogue
```

## Systems and where they live

| System | Scripts | Assets |
|---|---|---|
| Core / bootstrap | `_Tender/Scripts/Core` | — |
| Input | `_Tender/Scripts/Input` (`GameInputs`) | `.inputactions` alongside |
| Save | `_Tender/Scripts/SaveSystem` | writes to `<project>/../Saves` (outside Assets) |
| Character | `_Tender/Scripts/Player` | `_Tender/Art/Characters/Tena` |
| UI | `_Tender/Scripts/UI` | `_Tender/UI` (uxml/uss/themes) |
| Dialogue (Yarn) | `_Tender/Scripts/Dialogue` | `_Tender/Dialogue` (.yarn) + `_Tender/Prefabs` |
| Piano minigame | `_Tender/Scripts/Piano` (+ `RIG`) | `_Tender/Art/Piano`, samples in `_Tender/Resources/Audio` |
| Volumetric clouds | `_Tender/Scripts/Clouds/Editor` | `_Tender/Art/Clouds` (shaders, materials, noise) |

## Input maps (new Input System)

`GameInputs` defines 4 maps: **Player**, **Dialogue**, **UI**, **Minigame**.
`InputController` exposes `EnablePlayer/EnableUI/EnableDialogue/EnableMinigame` (each disables
the rest). ⚠ The character controller & interactive objects currently bypass this and use
legacy `Input`/`OnMouseDown` — unify later.

## Piano data flow

```
.mid TextAsset → MidiPlayer (DryWetMidi parse, dspTime clock)
  → PianoKeyboard.PressKeyScheduled(note,vel,dsp,dur)
      ├─ PianoKey → AudioSourcePool (Salamander samples via Resources.Load)
      └─ static OnAnyKeyPressed/Released → PianoHandController → PianoFingerController
  → NoteVisualizer polls MidiPlayer.CurrentPlaybackTime (falling-note bars)
```

## Clouds

Camera-to-backface **raymarch through a unit cube** (`HeroCloudVolume` / `VolumetricCloudBox`),
density from a box/dome shape mask × 3D noise (`CloudNoise3D.asset`, 64³ R8 Perlin FBM baked by
`Noise3DGenerator`), 4-step light march. Works at small scale; see `INSPECTION.md` for why large
clouds fail.

## Third-party

`_ThirdParty/`: **MagicaCloth2** (cloth sim), **TextMesh Pro**, **DryWetMidi** (MIDI parsing, in
`Plugins/`). **HotReload** stays at `Assets/HotReload/` (path-locked).
