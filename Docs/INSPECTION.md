# Code inspection (2026-07-18)

Full pre-reorg review of the three main systems. File paths below use the **new** post-reorg
locations. Actionable items are tracked in `TODO.md`.

---

## Character controller & UI

**Character** (`_Tender/Scripts/Player`, `_Tender/Scripts/Core/InteractiveObject.cs`)
- `PlayerController`: `NavMeshAgent` owns position, rotation hand-driven with a fragile hysteresis
  (turn >30°, stop <5°) → jitter at intermediate angles. Raycasts have no `LayerMask`, no
  "over UI" guard; `Camera.main` fetched per frame; animator params stringly-typed.
- `HeadController`: legacy `Input.mousePosition`, per-frame unmasked raycast, an obfuscated
  SmoothDamp where `Time.deltaTime` cancels out.
- `InteractiveObject`: `.material` creates an instanced material that's **never destroyed** (leak);
  legacy `OnMouseEnter/Down`; additive highlight can exceed 1.0.

**UI** (`_Tender/Scripts/UI`, `_Tender/UI`) — this is the "overloaded" part:
- `MainMenuController` (~278) and `PauseMenuController` (~404) duplicate ~80% of their code.
- `PauseMenuController` is a god-class (~12 responsibilities); input split between event subscription
  and per-frame polling.
- No caching / no data binding: every panel switch re-queries the tree by magic string; settings state
  held twice (bind lambdas + a parallel refresh) → desync risk.
- Settings UXML subtree duplicated verbatim in MainMenu.uxml and PauseMenu.uxml.
- **Event-registration leaks**: `SettingsPanelController` re-`new`'d each `OnEnable`, registers control
  callbacks that are never removed → handlers stack up.
- **Bug**: "New Game" on an empty slot no-ops (inverted logic in `HandleSlotAction`).
- Dead uGUI: `TabSystem.cs`, `SettingRow.cs` (referenced nowhere).

**Rebuild question:** the pain is ~60% architecture (fixable in place) and ~40% UI-Toolkit-runtime
gamepad-navigation friction. Recommendation: **stay on UI Toolkit**, adopt Unity 6 runtime data binding
+ built-in navigation, extract a shared base class and a shared settings template. Only consider uGUI
Canvas if gamepad navigation stays intractable — it's the one axis where uGUI is clearly more mature,
but switching throws away the USS styling and clean procedural slot generation you already have.

---

## Piano — why the finger rig "doesn't work"

Stacked, independent defects (`_Tender/Scripts/Piano`):
1. **Only the index finger moves.** `PianoHandController.HandleHandPress` hardcodes `FingerType.Index`
   with the finger-selection left as a `// TODO`. No note→finger mapping exists; 8 of 10 fingers are dead.
2. **Cumulative bend.** `PianoFingerController.ApplyHandRotations` does `node.localRotation *= AngleAxis(...)`
   every LateUpdate without reading a base pose — stable only *by accident* because the animator's
   PianoLayer (A-pose + hand mask) resets fingers each frame. Change the layer/mask and fingers explode.
3. **Bend too small / wrong axis.** ~10–15° total; one shared `BendAxis` applied to all phalanges whose
   local flexion axes differ → splay instead of curl.
4. **Release keyed on one overwritten note** (`CurrentTargetNote`) → breaks on chords/legato; index finger
   latches down.
5. **IK/layer blend never wired.** `_pianoLayerIndex` resolved but `SetLayerWeight`/`ResetToIdle` never
   called; IK is always full-weight.

**Also "crooked" playback (separate from fingers):**
- MIDI scheduler defeats sample-accurate `PlayScheduled` (dispatch at `StartTime`, no look-ahead → replays
  at `now+10ms`, frame-quantized).
- Note-off no-op (`_currentSound` never stored) → every note rings the full sample.
- `NoteVisualizer` allocates a `CreatePrimitive` cube + `new Material` per note (GC hitching), ignoring the
  project's own `ObjectPool<T>`.

**To make fingers work:** note→finger allocator + idempotent per-bone bend + drive IK toward the assigned
finger's key + wire the layer/IK-weight blend.

---

## Volumetric clouds — why large clouds fail

Both shaders (`_Tender/Art/Clouds/HeroCloudVolume.shader`, `VolumetricCloudBox.shader`) raymarch
camera-to-backface through a unit cube. Small scale looks great; large fails structurally:
1. **Fixed step count** → step size scales with box size. A 2000 m cloud at 64 steps ≈ 30 m/step →
   banding, holes, thin result. (`_OpacityBoost`, `density*=3` are band-aids for this.) **#1 cause.**
2. **Shape bolted to the container** (box edge mask / object-space dome). No weather map / coverage field →
   a bigger box is one uniform blob, not a cloudscape.
3. **World-space noise Repeat-tiles** visibly at scale (Hero, `_MacroScale≈0.29` repeats every ~3.45 u);
   the old box shader instead sets near-constant object-space noise → mush.
4. **Light march is 4 steps to the box wall**, not through density → no self-shadowing at scale → flat,
   lit-through clouds.
5. **Perlin-only noise** (no Worley) → erosion rounds edges instead of carving cauliflower.

**Standard large-cloud pipeline** (Nubis/Horizon): weather map (coverage/type/height) → world height
gradient → low-freq Perlin-Worley base → high-freq Worley detail erosion → adaptive raymarch with LOD →
Beer-Powder + dual-lobe HG lighting. The existing march/lighting scaffolding can be kept; changes 1–3
(adaptive step, weather field, fixed light march) are the highest leverage.
