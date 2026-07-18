# Tender — Claude context

Unity **6000.x / URP** game. Single-developer, narrative/point-&-click style project
with several self-built systems (character, UI, piano minigame, volumetric clouds) and
an upcoming rain/wetness simulation.

## Guiding philosophy (important)

- **Architecture must stay as simple as possible.** Minimum code, maximum clarity.
  The goal is that a system can be shown to a student as "look how simply you can build
  something complex". Prefer the obvious, readable solution over the clever one.
- Before adding abstraction, ask whether a beginner could follow it. If not, simplify.

## C# code style — Microsoft conventions

- Private/serialized fields: `_camelCase` (leading underscore). e.g. `private int _stepCount;`
- Public members, methods, types, properties: `PascalCase`.
- Locals & parameters: `camelCase`.
- `[SerializeField] private` over public fields for inspector-exposed data.
- Match the surrounding file's existing style when editing.

## Folder layout

```
Assets/
  _Tender/            ← ALL project-owned content
    Scenes/           Game, MainMenu, Piano, VolumetricClouds (+ per-scene lighting/navmesh)
    Scripts/
      Core/           G (service locator), AppBootstrap, SceneBootstrap,
                      InputController, AudioManager, HUDController,
                      InteractiveObject, GraphicsManager
      Input/          GameInputs.cs (generated) + GameInputs.inputactions
      SaveSystem/     SaveManager, SaveData, SavedVariableStorage
      UI/             UI Toolkit controllers (+ dead uGUI TabSystem/SettingRow — to delete)
      Dialogue/       Yarn-Spinner integration scripts
      Player/         PlayerController, HeadController, BakeSmoothedNormals
      Piano/          MIDI player, keyboard, keys, pools, RIG/ (hand+finger controllers)
      Clouds/         Editor/Noise3DGenerator.cs
    Art/
      Characters/Tena/  character fbx, textures, animator, materials
      Piano/            piano fbx/fbm, materials, Salamander sample source
      Clouds/           volumetric cloud shaders + materials + CloudNoise3D.asset
      Environment/      Plant, SoftChair, Terrain
      Shaders/          CelShader
      Materials/        shared (Skybox)
    Audio/            MainAudioMixer + SFX
    Resources/        Audio/ (piano samples + MIDI)  — SPECIAL FOLDER, keep structure
    Settings/         URP pipeline/renderer/volume assets
    UI/               UI Toolkit uxml/uss/themes, PanelSettings, menu image
    Prefabs/          Dialogue System
    Dialogue/         Yarn content (.yarn, .yarnproject, palette, strings)
  _ThirdParty/        MagicaCloth2, TextMesh Pro, Plugins (DryWetMidi.dll)
  HotReload/          ⚠ DO NOT MOVE — Singularity Hot Reload hardcodes this path
```

Docs live in **`Docs/`** at the repo root (outside `Assets/`, so Unity ignores them):
`ARCHITECTURE.md`, `TODO.md`, `INSPECTION.md`.

## Architecture notes

- **`G` (Core/G.cs)** is a static service locator — `G.Input`, `G.Save`, `G.Player`,
  `G.Graphics`, `G.Audio`, `G.Dialogue`, `G.HUDController`. Managers are spawned by
  `AppBootstrap` before the first scene; `SceneBootstrap` picks the input mode per scene.
- **Input is currently split**: menus use the new Input System (`GameInputs`), but the
  character/interaction still use legacy `Input`/`OnMouseDown`. Unifying on the new system
  is a known cleanup.
- Build scenes: `Game`, `MainMenu` (see `ProjectSettings/EditorBuildSettings.asset`).

## Path-sensitive gotchas (don't break these)

- **`HotReload/`** — hardcoded `"Assets/HotReload/Server"` / `"Assets/HotReload/Resources/..."`.
  Left at `Assets/` root on purpose.
- **`Noise3DGenerator.cs`** writes to a hardcoded `"Assets/CloudNoise3D.asset"`; the asset
  now lives at `Assets/_Tender/Art/Clouds/CloudNoise3D.asset`. Update that string before
  regenerating the noise (tracked in TODO).
- **`Resources.Load`** is used only for piano samples (`PianoKeyboard.cs`, path
  `Audio/SalamanderPiano/...`). Keep the internal structure of `_Tender/Resources/` intact.
- Everything else is referenced by GUID (`.meta`), so assets can move freely **with** their
  `.meta` file.
