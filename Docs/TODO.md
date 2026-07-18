# TODO / Backlog

Legend: `[ ]` open · `[x]` done. Priorities: **P0** bug/blocker · **P1** cleanup · **P2** feature.

## Reorg follow-ups
- [x] Open the project in Unity, reimport — **0 compile errors**, scenes open, materials OK.
- [x] Fixed the chat/dialogue UI: `DialogueView.uxml` referenced its `.uss` by a path-only `src`
      (no guid), so the move broke it → dialogue rendered unstyled. Now points to the new path
      **with guid** (move-proof). Menu uxml paths also normalized to `_Tender/UI/`.
- [x] `Noise3DGenerator.cs`: output path updated to `"Assets/_Tender/Art/Clouds/CloudNoise3D.asset"`
      (so regenerating overwrites the in-use asset in place, keeping its guid).
- [ ] Dead uGUI `TabSystem.cs` / `SettingRow.cs` are referenced by **leftover GameObjects in
      `MainMenu.unity`** (old pre-UI-Toolkit settings screen). To remove cleanly: delete those
      GameObjects from MainMenu, THEN delete the two scripts — otherwise the scene gets "Missing Script".
- [ ] Consider deleting `_Tender/Art/Clouds/New Material.mat` (a stray Skybox/Procedural, not a cloud).

## P0 — correctness bugs (from inspection)
- [ ] MainMenu "New Game" on an empty slot no-ops — inverted logic in `MainMenuController.HandleSlotAction`.
- [ ] UI event-handler leaks: `SettingsPanelController` re-registers callbacks every `OnEnable`
      without unsubscribing (settings apply N times). Also the per-submit `BlurEvent` registration.
- [ ] `InteractiveObject` leaks an instanced material (`.material` clone never destroyed) — use
      `MaterialPropertyBlock` or cache+destroy.
- [ ] Character raycasts (`PlayerController`, `HeadController`) have no `LayerMask` and no
      "pointer over UI" guard — clicking menus issues move orders. Cache `Camera.main`.
- [ ] Piano MIDI scheduler defeats itself (`MidiPlayer`): notes dispatched at `StartTime` with no
      look-ahead → every note replays at `now+10ms`, quantized to the frame → "crooked" playback.
      Add look-ahead so `PlayScheduled` stays in the future.
- [ ] Piano note-off never fires for audio (`PianoKey._currentSound` never assigned) → notes ring
      full sample length regardless of MIDI duration.

## P1 — simplification / dedup
- [ ] Unify input on the new Input System; remove legacy `Input`/`OnMouseDown` from character & interaction.
- [ ] UI: extract a shared `MenuControllerBase` — `MainMenuController` and `PauseMenuController`
      duplicate ~80% (SetupInputHandling, dropdown isolation, ShowPanel, GoBack, InputModeTracker).
- [ ] UI: share the Settings panel UXML via `<Template>`/`<Instance>` (duplicated in MainMenu.uxml
      and PauseMenu.uxml). Data-drive the settings rows (single source of truth, drop the bind/refresh twins).
- [ ] UI decision: stay on UI Toolkit + adopt Unity 6 runtime data binding & built-in navigation
      (recommended), rather than rebuild on uGUI Canvas. Revisit only if gamepad nav stays intractable.

## P1 — Piano finger rig (currently "doesn't work")
Root causes (see INSPECTION.md):
- [ ] Only the index finger is actuated — `PianoHandController` hardcodes `FingerType.Index` (TODO stub);
      implement a note→finger allocator (track which finger holds which note; release the right one).
- [ ] `PianoFingerController.ApplyHandRotations` uses cumulative `localRotation *=` — make it idempotent
      (`localRotation = base * AngleAxis(angle, perBoneAxis)`), per-bone axis, ~40–60° curl.
- [ ] Release keyed on a single overwritten `CurrentTargetNote` — breaks on chords/legato.
- [ ] Implement the intended IK/layer-weight blend (fields exist, `SetLayerWeight`/`ResetToIdle` never called).
- [ ] Pool the `NoteVisualizer` cubes (reuse `ObjectPool<T>`, tint via `MaterialPropertyBlock`) instead of
      `CreatePrimitive` + `new Material` + `Destroy` per note.

## P1 — Volumetric clouds (large clouds fail)
- [ ] Decouple step *size* from box size: fixed world-space step + `_MaxSteps` cap + adaptive (coarse in
      empty space, fine in density). #1 fix for banding/holes at scale.
- [ ] Add a weather/coverage field (world XZ) + world-space height gradient; use the box only as render bounds.
- [ ] Fix the light march (march through density with fixed world step, not `distToWall/4`); add Beer-Powder
      + dual-lobe HG phase.
- [ ] Rebuild noise as channel-packed Perlin-Worley (use the unused G/B/A channels), sample detail with LOD.

## P2 — NEW: Rain & wetness simulation (next major feature)
Goal: rain drops land on clothing; cloth gradually **wets**, changing material properties (darker albedo,
higher smoothness/specular, maybe droplet normals), and dries over time.
- [ ] Design doc: what drives wetness (a 0..1 per-region "wetness" value?), how it maps to material params,
      how it interacts with MagicaCloth2 cloth.
- [ ] Prototype: global rain toggle + a wetness parameter on the character's clothing material.
- [ ] Decide sim granularity: per-material scalar vs. a wetness mask texture accumulated over time.
- [ ] Rain particle/VFX + splash/droplet decals; surface exposure (don't wet covered areas).
- [ ] Drying curve when out of rain.
Keep it SIMPLE first (single wetness scalar → shader), expand only if it reads well.
