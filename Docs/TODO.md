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

## P1 — Volumetric clouds  (hero cloud + modular sky — BUILT)
New system: `CloudHero.shader` + `CloudHero.mat` + `CloudShape.cs` (component) + `CloudPresets.cs`.
Model: **density = smooth-union of a sphere list (SDF) − noise** (noise sculpts the surface, never fills the box).
A cloud is a LIST OF SPHERES fed per-instance via MaterialPropertyBlock, so one shader/material makes a hero
cumulus, a towering cumulonimbus, and a wall — see `HeroCloud_v1`, `Cloud_Tower`, `CloudWall` in the scene.
- [x] Adaptive raymarch: `baseStep = max(_StepSize, segLen/_MaxSteps)` (always crosses the box → no missing
      chunks on big/tall boxes) + empty-space skip (`_EmptyStep`). Light march with fixed world step + Beer-Powder.
- [x] Channel-packed Perlin-Worley noise (`CloudNoisePW`); renormalized in-shader (`_NoiseLow/_NoiseHigh`).
- [x] Real cauliflower form (blob smooth-union) + subtle "boil" animation (`_Evolve`, form ~still via `_BaseDrift`).
- [x] Modular instances: one material, per-instance blob lists; wall = overlapping `WallChunk` + `LowBank` back row.
- [x] Per-CLOUD detail LOD (box-centre distance, constant along ray) fades erosion + flattens billow far →
      kills grain/tiling at distance and the detail fetch. (Per-SAMPLE LOD caused moving concentric shells — don't.)
- [x] Refined adaptive march (coarse skip → back-step → fine) so the surface isn't quantized into moving bands.
- [x] Overlap sorting: shader writes per-pixel CORE DEPTH (`SV_Depth`) + `ZWrite On, ZTest LEqual` so overlapping
      cloud boxes occlude correctly regardless of transparent sort order (fixes chunks popping in front).
- [x] **Edge morphing root-caused**: the "cloud plays/morphs as you fly past" was a CAMERA-RELATIVE step
      (`baseStep = segLen/_MaxSteps`, segLen = camera→box distance → lattice slid with the camera). Now derived
      from the world-constant bounds (`boundsDepth/_MaxSteps`) → view-independent → edges stay put. (Not animation;
      restored gentle `_BaseDrift 0.03 / _Evolve 0.4`.)
- [x] **TAA (temporal denoise/stability)** — via URP's built-in camera TAA (RenderGraph is ON, so no heavy custom
      feature). Enabled on the scene cam (High) + `renderPostProcessing=true` (REQUIRED — TAA resolves in the post
      pass or silently no-ops). `CloudManager` feeds a per-frame Halton `_TemporalJitter` so frames differ and TAA
      averages the grain. Shared cloud math extracted to `CloudsCommon.hlsl` (one source of truth). Validated offline
      by averaging 16 jittered frames (`scratchpad/cloud_taa_sim.png`).
- [x] **Approach-morphing root cause #2**: the march START was still camera-anchored (`t = tstart`), so flying
      straight at a tendril swept the sample lattice through step boundaries → tendrils "played". Fixed by
      WORLD-ANCHORING the lattice (`CloudsCommon.hlsl`, `_CloudNoAnchor` A/B switch). Verified: −49% flicker
      (dolly A/B, temporal jitter isolated), holds at half-res too. 3 independent review agents confirmed.
- [x] **half-res raymarch + bilateral upsample** — built as stateless URP RenderGraph feature
      `HalfResCloudsFeature.cs` + `CloudsHalfRes.shader` (+ `CloudsHalfRes.mat`), wired on `PC_Renderer` (old
      built-in `VolumetricClouds` FullScreenPass disabled as fallback). Depth-aware upsample; stacks under URP TAA.
- [x] **Detail stipple fixed via MIPMAPPED noise**: `CloudNoisePW` already has mips (7); the shader forced mip 0.
      Now samples the detail (GBA) fetch at a constant `_DetailLod` (default 1.5) → pre-filtered, no interior aliasing,
      view-independent so no morphing. Billow (R) stays mip 0. The bottom-fringe "dissolve" was the start-dither and
      is removed by TAA live (verified via 16-frame TAA proxy).
- [ ] (Optional, if fringe still bugs live even with TAA) soften density ONLY at the thin edge (ramp by blob-field
      `shape`) so cores stay opaque (no blue see-through) while fringes anti-alias further. Not done — user chose mips.
- [ ] Verify URP TAA live in Game-view Play Mode (can't be seen in offline single-frame renders / Scene view).

### Volumetric clouds v2 — BAKED DENSITY VOLUME (major pivot, Phases 1–2 done)
The blob-stack + subtractive-noise model read as "disconnected noise" and every sampling variant of a coarse
march over that high-freq field caused morphing / zone-plate rings / stipple. Pivoted (per a 5-agent research
pass) to: a proper generator BAKES the cloud into a 3D density volume; the march does one trilinear fetch.
- [x] `CloudVolumeBaker.cs` — generator (flat base → macro lobes → height profile → ADDED rounded billows →
      wide coverage → shell crinkle) → `CloudDensity.asset` (128³ RHalf, mips, Clamp, zero border).
- [x] `CloudsCommon.hlsl` CloudDensity → one volume fetch; retired blob loop / world-anchoring / position dither.
- [x] `CloudManager.cs` → binds volume + world AABB. Wired in the scene; half-res + TAA reused unchanged.
- [x] Fixed: `_DensityMul` down to ~6 (was 24 → opaque-in-one-step onion bands); jitter the LIGHT-march start
      (self-shadow bands were view-locked so TAA couldn't average them otherwise).
- [ ] LOOK (with user, in-scene): it's coherent but SMOOTH stacked lobes, not crisp cauliflower like the
      reference photo. Push billow freq/amp/crinkle in the baker; stronger lit-top/dark-base; wispier edges.
- [ ] Phase 3: footprint (ray-cone) mip instead of constant `_VolMip`; adaptive step. Phase 4: more crinkle.
- [ ] Phase 5 (opt): baked sun-shadow channel; small runtime perturbation volume for slow motion; cloud bank
      = a few instanced volumes.
- [ ] Mild seam at cloud-cloud depth interface; residual jitter noise (both go away with TAA / the fullscreen pass).
- [ ] For a real full sky: move from box-per-cloud to ONE fullscreen raymarch of a coverage/weather field
      (boxes are right for a few hero clouds, not a whole sky).
- [ ] Sky transition: swap the blob shape for a weather/coverage field (world XZ) to get an overcast sky from the same shader.
- [ ] Ground shadows: directional-light cookie from a cloud-coverage texture (see LIGHTING.md Phase 2).
- [ ] Optional: dual-lobe HG phase; adaptive stepping (coarse in empty space).

## P1 — Grass (two systems, both BUILT)
Tall grass = `GrassField.cs` + `GrassPlacement.compute` (GPU placement, indirect draw, count never returns
to the CPU). Short grass = `ShellGrassPatch.cs` + `ShellGrass.shader` (stacked shells in one mesh). They
share `GrassCommon.hlsl` for wind and interactors so the seam between them does not show.
- [x] Editor flicker / grass vanishing when the editor loses focus **root-caused**: `RenderMeshIndirect` is
      immediate-mode, and it was issued from `Update`, which in edit mode is not a per-frame callback (the
      editor ticks it when it feels like it and nearly stops when unfocused) while the scene view keeps
      repainting. Moved to `RenderPipelineManager.beginCameraRendering` → one dispatch+draw per camera that
      actually renders. Also drops `_camera`: every camera is now culled for itself, so scene view and game
      view can look different ways and both be correct.
- [x] Shell grass collapsing to a flat green plane a few metres out **root-caused**: two separate mistakes.
      `ShellFootprint` took `max()` of the two screen derivatives, and looking along the ground the
      along-view axis runs to tens of cells per pixel, so blades were declared unresolvable at ~2-3 m →
      now the footprint is measured by AREA (`sqrt(dx*dy)`). And past the limit blade height was scaled to
      zero, which is what produced the plane → now blades grow to FULL height and widen until they close
      over, so the far field is a solid carpet at grass height with its silhouette intact.
      Cost note: at full fade the top shell is opaque, so the 15 shells under it are depth-rejected.
- [x] Shell grass read as a uniform carpet — fixed as one pass, built around a rule worth keeping:
      **the field has two scales, and only the small one is allowed to fade.** Single blades (2 cm) exist
      only while a pixel is smaller than they are; tufts (~30 cm) and patches (metres) are many pixels
      wide at any distance, so they NEVER fade and become the only variation left far out. Concretely:
      `ShellClump` (2-octave value noise) sets the local height ceiling and never fades; per-blade height
      share, lean, ribbon flattening and dryness all fade INTO it; and at full fade the skipped
      nine-cell search pays for a clump-gradient normal tilt, so the far field is lit by its own
      undulation instead of as one flat plane. That last part is what the "flat green sheet" actually was.
- [x] Per-blade lean + per-blade wind, inside `ShellSample` (stem offset by `along²`, cantilever shape).
      Wind comes down from the vertex stage in an interpolator: `GrassForce` is two noise fields plus the
      interactor loop, far too expensive per pixel, and the wind field is metres across so interpolating
      it is indistinguishable. Per-blade gust phase, so the field does not flutter in lockstep.
      **Trap found while building it:** lean must be capped (0.85 cells) — a tip that leans further walks
      out of the nine cells its neighbours search, so the end of the blade is never drawn and the field
      looks clipped along an invisible line in the wind. Allowing more means a 25-cell search.
      Ribbon flattening and lean must BOTH fade out with distance, or they hold gaps open right where
      coverage should be closing up, drawing a ring of holes.
- [ ] Enabling clumping lowers the average height (the clump only ever shortens, max stays at 1, because
      letting it exceed 1 would push blades past the top shell and flat-top them). Raise blade height to
      compensate if a patch now looks mown.
- [x] Visible "line where the grass stops" on a small clearing: `ShellFade` was a straight ramp, and a
      ramp has a CORNER at both ends — the rate of change jumps to zero within a pixel and the eye reads
      that as a drawn line. Now `smoothstep`, which has nowhere to put the line. Defaults also pushed out
      (detail limit 1.2 → 4.5 cells per pixel, was 1 → 3).
- [x] Tall shell grass (knee height) now viable, via `_GrazeFill`: blades fatten as the view flattens,
      which closes the seams between shells. Costs nothing true — at a grazing angle single blades are
      unresolvable anyway. Shell cap raised 48 → 64, blade height cap 0.5 → 0.6 m.

### Where shell grass works, and where it does not (settled while making it knee-high)
The shells are parallel sheets `height / (shells - 1)` apart. You see BETWEEN them when that gap
approaches the blade spacing, so the rule is **shells ≳ 3 × height × blades-per-metre**:
ankle-high at 45 blades/m needs ~16 shells, knee-high at 45 needs ~60. **Going taller therefore means
going sparser** — that is the real trade, not the height itself. Three patches now sit in `GrassLab` at
z = 78 / 58 / 38 to compare: `ShellGrass_Lawn` (0.09 m, 16 shells, 45/m), the original `ShellGrass_Patch`
(0.437 m, 48 shells, 41/m — deliberately left at the edge of the rule, ratio 0.39, so the artefact is
visible for comparison), and `ShellGrass_Knee` (0.45 m, 60 shells, 24/m — sparse, ratio 0.18, clean).
- **Good:** camera above the grass looking down or at a moderate angle; bounded areas; short to
  mid-height. Costs no CPU, batches, casts shadows, no compute.
- **Bad, no fix available:** camera INSIDE the grass volume — you end up between the sheets and see them
  as stacked planes. Hard constraint on the controller: eye stays above the grass with margin. Knee-high
  grass rules out crouch, prone, ground-level cutscene cameras and low third-person orbits.
- **Bad:** tall grass silhouetted against the SKY. The top shell is a plane, so the field has a flat top
  edge where real grass has blade tips. Clumping makes it undulate but it is still a lumpy plane — tall
  shell grass wants to be seen against ground, not horizon.
- **Bad:** large areas (fixed mesh, no distance LOD on the geometry) and anything above ~0.5 m, where the
  shell count needed makes it more expensive than the real thing. That is `GrassField`'s job.
- [x] Verified live via Unity MCP: shader compiles clean, all three patches build. Found and fixed
      `_detailFrom/_detailTo` = 0 on the original patch (Hot Reload does not run field initializers on
      live instances — see memory), and a brightness jump where blades merge into the far carpet
      (carpet top was pure tip colour with no root darkening; ramp now squeezed to 0.6 as blades merge).

### Shell grass performance — measured (1080p, close-up filling the screen, A/B with GPU sync)
**Cost is linear in shell count**, and that one fact decides every optimisation: a fragment shader
that can `discard` defeats early depth rejection, so EVERY layer pays in full no matter what is in
front of it. So the levers are (a) make the discarded fragment cheap, (b) fewer shadow layers,
(c) fewer layers, made invisible by jitter.
| Knee patch | before | after |
|---|---|---|
| 60 shells, shadows on | +9.25 ms | +5.87 ms |
| shadow pass alone | 2.77 ms | 0.61 ms |
| 40 shells + jitter (now the default) | — | **+3.84 ms** (−58%) |
- [x] Early-out above the tuft ceiling before the 9-cell search; one `GrassHash42` call per blade
      instead of five hashes, with the height test FIRST (most blades are shorter than the layer asked
      about); dryness hashed only for the blade actually hit. Forward pass −11..14%.
- [x] Shadow caster: only every 3rd layer casts (flag in `uv1.y`, rest pushed out of clip space in the
      vertex stage before any work). Shadow cost −78..86%. Shadow map texels are centimetres and PCF
      blurs them again, so the detail was never visible.
- [x] **Slab jitter** (`_SlabJitter`): each layer samples a random height inside the gap below it,
      moved ALONG THE VIEW RAY (dropping straight down would sample a point the pixel cannot see). The
      ordered "fern" ribbing becomes fine grain at zero cost. Under TAA the noise frame advances per
      camera (`_ShellFrame`, only for TAA cameras — elsewhere a moving pattern just boils) and a 16-frame
      average of 30 shells looked BETTER than 60 raw shells.
- [x] **Jitter noise is blue noise** (`ShellJitterNoise.png`, URP's 64x64, point-sampled with `Load`),
      and shading uses the layer's REAL height, not the jittered one. The first version used interleaved
      gradient noise: great under TAA, but raw it is a diagonal hatch, and reading the colour ramp from the
      jittered height painted that hatch across every blade face. Now faces are smooth and a patternless
      dither is left only on edges. Verified at 4x pixel zoom with jitter on/off A/B.
      **Trap that cost two wrong diagnoses:** `Material.SetTexture` for a property the material's shader
      does not have YET (same editor tick as the shader reimport) is silently dropped — the shader then
      used its "gray" default, jitter became a constant half-step shift, and on/off looked identical.
- [ ] TAA on the GrassLab camera is NOT enabled — it means switching on post-processing for the whole
      camera, and swaying blades will ghost without a MotionVectors pass in the shell shader. Enabling
      it would allow ~30 shells on Knee. User's call; a MotionVectors pass is the follow-up if yes.
- [ ] `ShellGrass_Patch` (user's, 48 shells) left as is; the same drop to ~36 with jitter would cut ~25%.
- [ ] Remaining forward cost is plain fill rate — N full-screen rasterised layers. Beyond this, the
      real fix is the single-pass march through the shell planes discussed as "ShellGrass v2".

### Teaching asset — placed (2026-09-13)
`SimpleShellGrass.shader` (90 lines, 64 without comments) + `SimpleShellGrass.cs` (49 / 34): flat stack of
quads with the layer height in vertex Y, world-XZ cells, per-cell random height, taper, root→tip ramp,
one-line sun + ambient. `ShellGrass_Simple` sits on a raised soil bed (`ShellGrass_Simple_Bed`) at
(100, 92) because it cannot follow terrain. Four wooden signs (`GrassLab_Signs`, TMP, Russian) explain
blocks 1–4. Tall grass is now cleared from under ALL shell blocks — it used to grow straight through
Lawn and Knee. Full shader for comparison: 609 / 299 lines.
A deliberately minimal shell grass for students: world-XZ cells, per-cell random height, taper, layer
colour ramp, one-line lighting. ~50–70 lines of HLSL + ~40 of C#. Each feature of the full shader is
then a lesson in order of the problems it solves (taper → scatter → wind → distance/fwidth → view-angle
jitter), which is exactly the order they were discovered in this project.
- [ ] ShadowCaster deliberately ignores the detail limit (shadow-map texels make `fwidth` meaningless
      there), so at distance the shadow is thin blades while the visible grass is a solid carpet. Harmless
      now, but revisit if distant shadows look sparse.
- [ ] Terrain sculpting does not invalidate the baked heightmap — "Rebake height map" is manual.

### Meadow pass — Zelda-style infinite field (2026-09-13)
Research (BOTW grass.extm colour/height maps, Ghost of Tsushima GDC 2021, SimonDev, Outerra, Far Cry 5):
the look is colour FIELDS + soft light at field scale, and past the last blade the GROUND is shaded as grass.
- [x] **Rings instead of one grid** (`GrassPlacement.compute`, `GrassField.cs`): full density to 30 m, each
      doubling of distance keeps 1 blade in 4, twice as wide; one dispatch per ring, each costs the same.
      Far rings are exact SUBSETS of near ones (hash-chosen child per 2x2, `FineCell`), dropped blades sink
      into the ground. View distance 90 → 400 m. Chosen-child is hashed — always taking one corner put far
      blades on a lattice that read as crop rows.
- [x] **Clumps** (Voronoi, 0.7 m): roots pulled in, blades face out and splay, per-clump height and tint.
- [x] **GrassLook.cs + GrassLook.hlsl**: one palette/lighting for blades AND ground (globals). Tip colour from
      low-frequency drift + dry patches, root = ground colour, carpet colour by 150 m, wrap, translucency,
      sheen and gust brightening that ride the wind noise. View-space thickening of edge-on blades.
- [x] **MeadowGround.shader** on the terrain: same functions, same paint map → no visible end of the grass.
- [x] **GrassLab terrain 200 m → 1.6 km** (`GrassLabMeadowTerrain.asset`, lab heights preserved exactly,
      rolling hills + a rim), paint map re-made at 2048² over the whole terrain. Old 200 m assets kept.
- [x] **Sun never lit the tall grass.** Forward+ leaves `unity_LightData` at 0, and GetMainLight's
      `distanceAttenuation` reads it unless the shader has `_CLUSTER_LIGHT_LOOP`. Dropped the factor.
- [x] Height bake no longer reruns on every inspector edit (16 MB re-upload per slider frame on 1.6 km).
- [x] Measured (RTX 4070, 1920x1080, eye level, 400 m field): blades drawn **~0.81 ms** (2.70 vs 1.90 ms,
      interleaved, culling-layer A/B); compute placement lost in the noise (~0.01 ms). An earlier benchmark
      that looped 8 component rebuilds + 560 renders in one call crashed the editor (D3D11 TDR).
- [ ] Coverage bias: at lower resolution sub-pixel blades fill whole pixels with tip colour, so the field
      reads paler (800x450 vs 1280x720 renders). MSAA x4 or TAA fixes both this and shimmer — measured in
      "Grass variety" item 7 below.
- [x] Terrain layers removed from `GrassLabMeadowTerrain` (alphamap 2048 → 16); the unused meadow
      TerrainLayer went to the OS trash. MeadowGround blends dirt from the paint map itself.
- [ ] No tonemapping on the GrassLab camera; palette is tuned to stay under 1.0. Compared None / Neutral /
      Neutral +0.5 EV / ACES: Neutral greys the field, ACES darkens it. Left off; decide together with the
      Game scene's post stack.
- [x] Shell patches: `ShellGrassPatch._matchMeadow` takes GrassLook's colours; ShellGrass is now lit by
      `GrassShade` (its own _Wrap/_Translucency are gone). All three lab patches match the meadow.
- [x] GoT-style shape: `GrassBend` walks the blade in 4 steps with turn ∝ s^1.7 (straight base, curling tip,
      length kept); blade mesh rows packed towards the tip; tuft "dome" normal per clump (`_TuftRound`).
- [ ] GoT dithered-depth shadow imposter — not done.
- [x] **Density A/B** (see "Grass variety" below): clump pull 0.35 read as separate little bushes; 0.12 is a
      continuous meadow at the same cost — now the default. Density 90 + width ×1.2 is fuller still
      (~1.6× blades). 1.8 m clumps with pull show Voronoi cracks between cells — avoid.
- [x] `LogBladeCounts` threw IndexOutOfRange (GetData into 5 uints from a 1-element args buffer) — fixed.

### Grass variety — plan (agreed 2026-09-13, next after the meadow pass)
Goal: the field stops being one grass everywhere. Plain base grass; rare small clearings of flowering
grass with white seed heads; dense tall wheat / pampas fields à la Ghost of Tsushima; flowers.

1. **Grass types as data.** `GrassType` ScriptableObject: height + variation, width, density multiplier,
   clump size / pull / splay / height, tip / cool / dry colours, seed heads (on/off, share, colour, size),
   blade mesh (segments, head geometry), stiffness and wind response. The compute writes a type index into
   the instance; shaders read that type's numbers from a small StructuredBuffer. One field, many types —
   no second GrassField per kind of grass.
2. **Zones: which type grows where.**
   - Procedural zone noise (30–150 m) with thresholds, so e.g. flowering clearings are rare (~3–5 % of the
     area) and small (8–25 m across).
   - A painted override map (RGBA = weights of up to 4 types), painted with the existing brush.
   - Each blade picks its type by weighted hash → dithered borders; height and colour ease over a couple of
     metres, so a zone never ends in a wall.
   - Base grass gets NO seed heads (flower share 0). The white heads move to the "flowering meadow" type.
3. **The carpet follows the zones.** MeadowGround and the blade carpet colour read the same zone weights,
   so a flowering clearing reads paler and a wheat field golden from the other side of the valley.
4. **Density — how games do it and what we do.**
   - In shipped games density belongs to the grass TYPE (lawn, meadow, tall grass, crop), modulated by
     painted / procedural maps and by the terrain material; region noise only varies it inside a type.
     Ghost of Tsushima: artist-authored per-type settings + type maps. BOTW: per-area grass height and colour
     maps (`grass.extm`).
   - What the eye reads as dense is SCREEN COVERAGE, not blade count: count × width × height, how much bare
     ground the clumping opens up, how dark that ground is, edge-on thickening. Measured today: clump pull
     0.35 → "bushes", 0.12 → meadow at the same cost.
   - Plan: per-type density + clumping; base grass 70–90/m² once TAA / alpha-to-coverage is decided (thin
     dense blades shimmer without it); keep the ring LOD as is.
5. **Wheat / pampas (susuki) fields.** Realistic, as a type:
   - Tall (1.0–1.5 m), very dense, near-uniform height, weak clumping, stiff stems.
   - Own mesh: stem + head (wheat ear = thick pale-gold tip segment; susuki plume = wider feathery card).
     Heads bend more than stems.
   - The Tsushima look is mostly WIND: big uniform waves + strong sheen on the heads. Our gust sheen and
     brightening already do this; per type they get stronger.
   - Player trails: interactors already part the grass; optional persistent trample map (RenderTexture that
     recovers over time).
   - Cost: dense + tall = many big overlapping triangles → shadows LOD0 only, far carpet = head colour, field
     edges ramp height down.
6. **Flowers.**
   - Daisies, poppies, cornflowers, dandelions as their own stream (like seed heads today): a small instanced
     mesh (petal ring as geometry or an alpha-tested card), clustered placement from noise + zone weights,
     a colour palette per zone.
   - Far away flower zones tint the carpet, so the colour survives to the horizon.
   - Textures: first generate them in C# (petal SDF → small atlas); photo/CC0 sources later, licence checked
     per asset.
7. Related: TAA or alpha-to-coverage (shimmer + coverage bias), tonemapping decision.

#### Progress (2026-09-13, second session)
- [x] **1. Types as data.** `Scripts/Grass/GrassType.cs` (ScriptableObject, `Gpu` struct 40 floats) +
      `Art/Grass/GrassTypes.hlsl` (struct, `_GrassTypes` buffer, `GrassInstance`, zone functions). GrassField
      holds `_types[]` (max 8), uploads them every frame (colour edits are live), rebuilds when the densest
      density or head share changes. Old per-field blade/clump/flower settings moved into the types;
      GrassLook keeps ground colour, drifts, light. Assets in `Art/Grass/Types/`: MeadowGrass (base, no heads),
      FloweringMeadow, Wheat, Pampas, Daisies, Poppies, Cornflowers.
- [x] **2. Zones.** Two-octave noise per type, threshold from `GrassZoneNoise.cs` (samples the same noise on
      the CPU: 4% coverage = threshold 0.778, not 0.96). Band centred on the threshold; width in metres
      calibrated (0.75 noise units per cell). Painted `_typeMap` (RGBA = elements 1–4), black = nothing;
      brush got "Zone of type 1–4" channels (plain stroke adds, Shift removes). Pick from the top type down
      with a coin each → mixed band; types shorten to 55% at their zone edge.
- [x] **3. Carpet follows zones.** `GrassZoneFar` blends the types' far colours by the same claims.
      Trap: mixing a third of head colour made far clearings white stains (linear light) — heads now 8%
      for seed heads, 65% ears, 60% blooms; painted seed heads on far blades at 35% strength.
- [x] **4. Density per type.** Grid = densest type, others keep a share. Buffers sized for the VISIBLE part
      of each band (1.0 / 0.75 / 0.5 of the circle; measured need 0.48 / 0.42 / 0.21). Cost at 1080p eye
      level with a 110/m² wheat type in the list: meadow 0.86 ms, looking over wheat 0.96 ms (was 0.81).
      Base grass stays 55/m² until item 7 is decided.
- [x] **5. Wheat & pampas.** Head style `Ear`: the blade's upper rows widen (`_headWidth`) into a spindle,
      coloured darker at its base, at every distance; `_headShine` boosts sheen/glow/gust on heads; per-type
      wind response (wheat 0.7, pampas 1.2). Painted demo fields: wheat x135–190 z35–80, pampas x35–80 z40–90.
      Not done: persistent trample map (optional); shadows are still LOD0+LOD1 (cost measured fine).
- [x] **6. Flowers.** Head style `Bloom` + `overlay` types: they take only their density's share of the
      candidates and leave the rest to the grass underneath. Head mesh is a scalloped disc (6 petals) —
      billboard for seed heads, sky-facing for blooms with a centre colour. Geometry instead of generated
      petal textures (no alpha-test shimmer, nothing to author). Far flower zones tint the carpet (claim ×4).
      Dandelions not made (same asset with yellow petals if wanted).
- [x] **7. Anti-aliasing — measured, decision is the user's.** Alpha-to-coverage does not apply: blades and
      heads are real geometry, nothing is alpha-tested. The candidates are MSAA and TAA.
      - Both grass shaders got a `MotionVectors` pass (blade built at `_Time.y` and `_LastTimeParameters.x`,
        the same time source Shader Graph uses), draws set `MotionVectorGenerationMode.Object`. **Verified in
        Play Mode:** a fixed offset added to the pass smeared the blades under URP object motion blur, the
        heads did not — so URP really draws the indirect grass into its motion pass. In today's wind the
        difference between real and zeroed grass motion under TAA is hard to see; it shows in strong wind.
      - Cost, whole 1080p frame at eye level (Play Mode, interleaved, post on, shadow distance 250 from the
        Ultra preset): none 2.77 ms, **MSAA x4 3.02 (+0.25)**, **TAA 3.21 (+0.44** — includes drawing all
        grass again for motion).
      - Look (2x crops, same view): no AA has stair-stepped blade edges. MSAA x4 smooths them and stays
        sharp. TAA smooths them too but softens the whole picture (~25% less edge contrast) and nearly erases
        sub-pixel far seed heads and the distant flower band (they average to their real coverage). With TAA
        the far heads would need to grow or go to the carpet earlier.
      - The "TAA darkens the frame" seen earlier was not TAA: the pipeline asset's own volume profile
        (`SampleSceneProfile`: Neutral tonemapping, bloom 0.25, vignette 0.2) switches on together with
        post-processing. Sky blue 0.78 → 0.68, the field stays put.
      - Suggestion: MSAA x4 for the meadow (cheaper, sharp, heads survive); TAA only if the Game scene wants
        post anyway (clouds already use it) — then clean `SampleSceneProfile` up first.
      - Tonemapping: still None. Enabling post-processing brings Neutral with it (profile above) — decide both
        in one go.

**Anti-aliasing traps:** URP TAA accumulates only when `Time.frameCount` changes (renders inside one
blocking RunCommand are single-frame resolves); URP motion blur runs only when `Application.isPlaying`; MSAA
needs `msaaSampleCount` on the pipeline asset AND an MSAA target for an RT camera. Entering Play Mode runs
`GraphicsManager.ApplyPreset` from PlayerPrefs, which rewrites the URP asset's shadow distance in memory
(the file says 1000, every Play makes it 250 until the editor restarts; at eye level the frame differs by
less than 0.5%).

**Verification trap:** changing a GrassField setting and rendering in the SAME blocking RunCommand can place
blades with a stale grid (a straight-edged hole in the near field, wrong blade counts). Change settings in
one command, let the editor tick, render in the next. Live editor use is fine.

### Meadow feel pass (2026-09-13, third session)
- [x] **MSAA x4** on `PC_RPAsset` (the only line changed in that file). Note `m_RenderScale` there is 0.8 —
      the whole game renders at 80% and is upscaled, which blurs thin blades. Not touched; worth a look.
- [x] **Post-processing zone for A/B.** Main Camera has post-processing ON. `GrassLab_Post` in the scene:
      `Post_Baseline` (global, `Settings/GrassLabPostBaseline`: tonemapping None, bloom 0, vignette 0 — cancels
      the pipeline's `SampleSceneProfile`, so outside looks as before) and `Post_Zone` (40 m box north of the
      spawn, blend 5 m, `Settings/GrassLabPostZone`: ACES, exposure +0.5, contrast/saturation +10, bloom 0.25,
      vignette 0.2, white balance at 0 ready to tweak) + a sign at its entrance. Chosen over Neutral and warm
      ACES by side-by-side renders.
- [x] **Sparse grass underfoot — measured.** Looking down 60° from 1.7 m, **54% of the screen was bare
      ground** (35°: 29%). Clump pull, clump height and splay changed almost nothing; blade count and width
      did. Fixes: (1) a sparser type now thins the grid with a 4x4 ordered dither (`GrassEvenThreshold`) instead
      of a coin per candidate — since types arrived the grid is laid for wheat (110/m²) and meadow kept a
      RANDOM half, which leaves bald patches; (2) meadow + flowering meadow 55 → 90/m², width ×1.2 → bare
      ground 38% / 16%; (3) `GrassUnderstoreyAlbedo` in MeadowGround: two layers of short strokes on the ground
      within ~20 m (GrassLook `Understorey` slider), so the gaps read as more grass instead of a green floor.
      Cost at 1080p + MSAA + post: understorey +0.1–0.17 ms, density/width +0.4 ms (frame 4.2–4.7 ms).
- [x] **Wind v2 — waves along an arc.** `GrassWind`: direction at its own position (moved to the lab,
      100,0,100), `Bend` (degrees per 100 m; 5 keeps the arc's centre off the terrain), waves with speed,
      wavelength, calm lean, gust lean, gust size, flutter. Shader (`GrassCommon.hlsl`): `GrassWindFrame`
      (downstream/across coordinates; v2 used circles round a pivot — see v3 below), `GrassGustInFrame` (lopsided wave — knocked
      over fast, recovers slowly — with fronts that wander and break into gusts, both noises travelling with
      the wave). `GrassGust` is now 0..1; `GrassShade` turns pressed-over grass paler and a little silvery
      (`Gust Brighten` 0.8) — that band is what shows the waves from far away. Selecting GrassWind draws the
      paths and the current wave fronts as gizmos. ShellGrass per-blade shimmer no longer follows the wave speed.
- [x] **Wind particles.** `GrassWind_Particles` (child of GrassField): an ordinary ParticleSystem (trails only,
      `GrassWindStreak.shader`, additive) steered by `GrassWindParticles.cs` — each particle asks
      `GrassWind.FlowAt/GustAt` (C# mirror of the shader maths, noise in `GrassNoise.cs`) and moves with the
      wave, weaving a little, visible on fronts and faded in the calm. Verified in Play Mode: 120 particles
      alive, moving along the arc.
- [x] **Tall types were the worst** (user caught it): bare ground looking down 60° — wheat 61%, pampas 44%
      (stems 1.3 / 1.8 cm, pampas clump pull 0.3 = tufts with holes). Now wheat 3.2 cm × 150/m², pampas
      3.5 cm × 110/m², pull 0.1 (head widths rescaled so ears keep their size) → ~24% / 16%; wheat field frame
      +0.2 ms. Ground under tall types (`GrassTallCover` → `GrassUnderTallAlbedo`) is darker with straw in it,
      and the understorey strokes are quieter (dark deep layer, faint top layer, default 0.6) — brighter
      strokes had made the gaps MORE visible. Wave paleness is capped at tipness 1 (heads with shine 2.2 went
      white on every wave).
- [x] **Wind v3 — no knot in the corner** (user saw one spot that barely moved). The arc was circles round a
      pivot; near the pivot the waves' "downstream distance" shrinks to nothing, so fronts crowded into one
      place and stood still. Now the paths are parallel curves with no centre: across-coordinate
      `across − ½·bend·along²`, downstream `along·exp(bend·across)`, both clamped to ±800 m (past that the
      wind blows straight). Whole-map top-down render confirmed even fronts everywhere. C# mirror updated.
- [x] **One angle convention for wind, clouds and sky:** degrees around Y, 0 → +Z, 90 → +X (what a transform
      with that yaw faces). `GrassWind` used (cos, sin) — identical at its 45°, so nothing moved.
      `SummerSky` drifted its clouds AGAINST its own `Wind heading` (sampled at `+drift`) — fixed to `−drift`.
- [x] **Cloud shadows v1 (2026-09-14)** — `CloudShadows` on the Directional Light + `Art/Clouds/CloudShadows.compute`.
      The sun gets a light cookie, so every sun-lit shader (grass, terrain, anything) darkens with no changes.
      Tileable 5-octave value noise made once on the CPU; the compute thresholds coarse + fine layers into the
      cookie each frame (fine layer drifts → clouds change shape); the cookie's offset slides along the wind so
      the shapes travel. Sliders: cover, softness, darkness, cloud size (350 m), direction (135°), speed (8 m/s),
      change. Verified: shadow visible on grass and terrain; top-down renders 10 s apart moved (60, −50) m vs
      the expected (57, −57). Not tied to the painted sky clouds yet.
- [x] **Cloud shadows v2 — tied to the sky (2026-09-14).** The cloud pattern moved into
      `Art/Clouds/CloudLayer.hlsl` (`CloudLayerDensity`, `CloudDrift`), included by BOTH `SummerSky.shader` and
      `CloudShadows.compute`. The sky layer is now anchored in the world (ray from `_WorldSpaceCameraPos` to
      world Y = `_CloudHeight`), and each cookie texel follows its sunbeam up to that layer and reads the same
      function. `CloudShadows` now only has darkness, thickness (shadow more solid than the pale cloud looks,
      default 3), area (3 km around `Camera.main`, faded at the edge, pixel-snapped) and resolution; cloud size,
      cover, height and wind come from the sky material. v1's own noise/sliders are gone.
      Verified: from 256 ground points, the brightness of the sun seen from the point vs how lit the point is —
      correlation **0.976**; the same 100 m off 0.1–0.3, 200 m off 0.0. Cost ≈ 0.4 ms/frame incl. sync overhead
      (512 vs 1024 made little difference, kept 1024).
      Honest number: with the sky as it is (cover 0.42, 900 m clouds) ~12% of the ground is under any cloud at a
      time. The sky looks fuller because near the horizon you see clouds 5–20 km away. More shadows = more
      Cover on the sky material, which changes the sky with them.
- [ ] The saved scene carries the cookie size/offset, so `GrassLab.unity` diffs on every save.
- [ ] Later, if the sky needs more body: draw the same `CloudLayerDensity` map as 2.5D/volumetric clouds
      (thin raymarched slab or impostors). The shadows stay correct automatically because they read the map.
- [ ] Tune by eye in Play Mode: wave strength/speed, streak width/alpha (material + trail width), post zone look,
      cloud shadow darkness (0.7 may be heavy once ambient is darkened per LIGHTING.md).

### LOD seams + grass interaction v2 (2026-09-14)
- [x] **LOD rings no longer show when moving.** The user saw a band of grass "moving with you" a couple of
      metres ahead and at every ring. Three causes, three fixes:
      - Blade meshes are NESTED: row heights `t = 1 − (1 − row/segments)^1.5`, so LOD1's rows are a subset of
        LOD0's. `GrassBlade.shader` geomorphs LOD0 → LOD1 over the last 30% before the switch (odd rows slide
        onto the line between their neighbours), so the swap itself changes no pixels.
      - Each blade switches at its own distance (`GrassLodDistance` in `GrassTypes.hlsl`, ±15% jitter) — no
        circle for the eye to follow. Flowers and the painted tip use the same distance.
      - Ring thinning is per blade too: each blade vanishes at its own distance (`GrassPlacement.compute`),
        shrinking in width and a bit in height, instead of a whole ring fading together.
      Measured with a fixed eye, stepping only the LOD centre 0.5 m: strongly changed pixels **53 → 12**.
- [x] **`GrassInteractor` — one component, any collider, no registry.** Put it on anything with colliders:
      effect **Bend** (leans away, springs back — player, animal), **Flatten** (pressed down, grows back
      slowly — paths) or **Remove** (no grass inside — house, rock); `reach` (m past the outline), `strength`,
      `press time` (0 = instant). Sphere / capsule / box / CharacterController are exact SDFs; MeshCollider =
      its mesh bounding box turned with the object; anything else = world bounds.
      `GrassInteraction` (on the GrassField) owns recovery (`spring back` 0.6 s, `regrow` 90 s), the look
      (push/flatten lean, flattened height) and the map: 128 m × 1024² ARGBHalf round `Camera.main`, ping-pong,
      scrolled in whole texels (RG push, B flattened, A removed). Tall blades, shell grass and the ground all
      read it (`GrassTouchAt` in `GrassCommon.hlsl`). Remove is also tested analytically in the placement
      compute, so a house past the map edge still has no grass through its floor (but no memory there).
      Replaced the old `GrassInteractors` list. Demo group `GrassLab_InteractionDemo` (house, boulder, path).
      Cost: **0.14 ms/frame** for the map step (5 interactors); the Remove test in placement is within noise.
- Trap: `collider.bounds` is all zeros for a just-created object in edit mode and a frame late for moving
  ones (physics sync). Shapes and their bounds are computed from the collider's own numbers instead.
- [x] Player trails: a second `GrassInteractor` on the Player — Flatten, reach 0.1, strength 0.7, press 0.25 s
      (one pass at walking speed flattens ~45%, running ~25%; walking the same line again deepens it).
- [ ] **"V" shape at the centre of the screen over the grass, visible only while walking** (user, 2026-09-14).
      Ruled out by measurement: the camera-distance systems (LOD pick, morph, rings, thinning, width —
      stepping only a debug "LOD eye" changes pixels at noise level), the player's bend (at eye level it
      changes ~300 px, only visible looking 20° down), motion blur / TAA (off), shadow cascades (splits at
      30/72/115 m), a clamped ring grid (squares cover every ring), vignette (0 outside the post zone).
      Candidates left: something anchored to the SCREEN rather than the world while walking (translucency /
      sheen towards the sun, haze, edge thickening), or the flashlight (F) left on — in daylight its beam
      makes a V-shaped bright patch on the near blades.
      **Temporary** `GrassDebugKeys` on the Player: keys 1-9 switch suspects off in Play Mode. Once found,
      delete it and the `_GrassDebugEye` lines marked DEBUG-TEMP (`GrassTypes.hlsl`, `GrassPlacement.compute`).
- [ ] Map area 128 m: past it touches are forgotten. Raise if the camera can move far from recent paths.
- [ ] Ideas on top: paths painted by a spline of Flatten boxes; a per-interactor "leaves a trail" flag.

#### Deferred — optional, agreed with the user to keep (2026-09-14)
- [x] Trampled paths that slowly grow back — done as `GrassInteraction` (above).
- [ ] Dandelions (a Bloom overlay type with yellow petals, and a seed-clock variant).
- [ ] Smooth hand-over where shell grass and the tall blades overlap (today they just draw on top of each other).
- [ ] Feathery pampas plumes — right now the plume is just the pale, widened top of a blade.

## P1 — Lighting: depth + sun shafts (Phase 3) and local lights (Phase 4) — BUILT 2026-09-14
Plan in `Docs/LIGHTING.md`. Test scene: GrassLab, object "Atmosphere + Time of Day".
- [x] **`Atmosphere`** (component) + **`VolumetricLightFeature`** (renderer feature on `PC_Renderer`) +
      `Art/Lighting/VolumetricLight.shader`. Half-res raymarch of height fog before transparents: sun with its
      shadow map AND the cloud cookie (so cloud shadows show as shafts in the air), Henyey-Greenstein phase,
      sky ambient from the ambient probe, and every Forward+ point/spot light (halos, shafts through gaps).
      Quadratic step spacing, analytic fog for the tail past the march, depth-aware blur + upsample.
      Sliders: visibility (8 km), base height, height falloff, anisotropy, sun/sky/lamp strength, march
      distance (1.5 km), steps (32), quarter resolution. Built-in `RenderSettings.fog` turned off in GrassLab.
      Cost **+0.8 ms** at half res, 1080p.
- [x] **`TimeOfDay`** — the hour slider (`[` / `]` wind the clock in Play Mode, 3 h/s). Moves the sun on a
      path (sunrise 5:30, sunset 20:30, highest 60°), colours it by height (gradients), intensity curve, turns
      the same light into a cool dim moon at night, and feeds the sky shader its zenith/horizon/cloud colours
      (`_SkyTimeOfDay` globals in `SummerSky`). Ambient probe refreshed every 0.1 h
      (`DynamicGI.UpdateEnvironment`). 15:30 reproduces the old fixed sun.
- [x] **Lamps light the grass.** `GrassLamps` in `GrassLook.hlsl` (Forward+ cluster loop, wrap + translucency)
      used by blades, flowers, shell grass and the ground; each got `_CLUSTER_LIGHT_LOOP` +
      `_ADDITIONAL_LIGHT_SHADOWS`.
- [x] Scene lights in `GrassLab_Lights`: 4 `Lantern` prefabs (`Prefabs/Lighting`), a campfire (flickering
      point, soft shadows, `LightFlicker`), a floodlight on a pole (spot, soft shadows), and a flashlight on the
      Main Camera (`Flashlight`, **F**).
- [ ] **Tonemapping / exposure is still undecided** and now matters: at sunset the glow round the sun clips
      to a hard white disc without it (sun strength lowered to 0.25 as a stop-gap). Decide ACES/Neutral +
      exposure for the whole game, then re-tune sun strength.
- [x] Additional-lights shadow atlas raised to 4096 on `PC_RPAsset` (campfire point light = 6 maps + the
      floodlight did not fit in 2048).
- [x] Wind streaks are lit (`GrassWindStreak.shader`): sun with its shadow and cloud cookie, sky, Forward+
      lamps, times `_Exposure` 0.5. By day they look as before; at night a streak adds 0.04 over grass at 0.09
      (the same ~80% linear contrast to the grass as by day) instead of glowing at full daylight brightness.
      Fog now fades an additive streak to black, not to the fog colour.
- [ ] Rendering Layers so the sun can be gated per area (cottage interior) — from LIGHTING.md, not done.

## P2 — NEXT MAJOR FEATURE: cloth wetness + fabric shaders
Goal: rain lands on clothing; cloth gradually **wets**, changing how it looks and hangs, and dries again.
Two halves that meet in one shared include: the SIM decides how wet a spot is, the FABRIC SHADERS decide
what being that wet looks like on a knitted sweater versus on nylon.

Build order — each step has to look right on its own before the next one starts:
1. `ClothWet.hlsl` + a single 0..1 wetness float on an existing character material (prove the look)
2. knit shader dry → nylon shader dry
3. wetness response inside both
4. per-region wetness (sky exposure), then the spreading mask
5. MagicaCloth2 response, then rain VFX / splashes / puddle contact

### Rain + wetness — plan agreed 2026-09-18 (supersedes the order above where they differ)
Started with the nylon (user's call): `Art/Fabric/ClothNylon.shader` — procedural tights from denier,
wales/cm and yarn bulk; coverage is derived, not a slider; edge darkening `1-(1-c)^(1/cosθ)`; anisotropic
Blinn-Phong along the thread; the drawn knit fades to its mean once a thread is under a pixel. Test rig:
`FabricLab` in GrassLab (black 15 den sphere, nude 20 den capsule "leg", skin `Lit` mesh inside each).

One idea drives the whole rain system: a **RainMap** — a top-down map around the player, exactly the
way `GrassInteraction` is a top-down map — that every other part reads. Same architecture twice, on purpose.

- **R1 RainMap.** Ortho depth from above, ~64 m around the camera, refreshed every few frames. Gives two
  things: the height where a drop falling at (x,z) lands, and shelter — a point below that height is
  under a roof or a tree and stays dry. `RainMap.hlsl`: `RainLandingHeight(xz)`, `RainShelter(posWS)`.
- **R2 Drops.** Instanced streaks in a ~20 m box around the camera; position = hash(world cell) + fall
  time, no CPU particle state; 9 m/s plus the grass wind so gusts tilt the rain with the blades; stretched
  by speed; each drop ends at `RainLandingHeight`. Lit with the same `LightInTheAir` idea as
  `GrassWindStreak` (shadowed sun/moon + lamps) — that was the fix that made the wind streaks sit in the
  night. Far rain = a boost in the volumetric haze + streaked noise, not more particles. Camera-lens
  drops optional, later.
- **R3 Splashes.** Where a drop ends: a crown sprite (2-3 frames, ~0.15 s) + a ripple ring. On the ground,
  procedural ripples in `MeadowGround` (rings expanding from hashed points, rate ∝ intensity) and puddles
  in the dips (flow accumulation from the heightmap, baked once).
- **R4 Wet world.** `Wet.hlsl` — `WetSurface(albedo, smoothness, normal, wetness, porosity)`: albedo
  darkens by porosity (soil 0.6, stone 0.3, nylon 0.05), saturation up, smoothness → 0.85+, micro-normal
  flattened by the water film, ripple normals where water pools. One global `_RainWetness` (rises with
  intensity, dries with sun + wind) × `RainShelter` × how much the surface faces up. Goes into
  `MeadowGround`, `GrassLook` (darker, glossier blades with sparkling drops) and a Lit variant for props.
- **R5 Cloth.** The P2 plan below, fed by the RainMap: a small RT per garment in UV space; drops splat
  where they hit (world position of each texel from a UV-space pass over the skinned mesh, 256²);
  wicking = a blur per fabric (knit spreads, nylon barely); drying rate per fabric. `ClothWet.hlsl` then
  makes nylon glossier and slightly MORE sheer with beads, and knit dark, matted and heavy (MagicaCloth2).
- **Ideas on top.** Rain intensity from the cloud layer (`CloudShadows` density over the player) so it
  starts raining when the cloud arrives; rain sound layered by what the RainMap sees around the player
  (grass / roof / water); lightning = the sun for one frame; the player's trail in the interaction map
  already knows where they walked through wet grass → hem and shoes wet by contact.

Budget guess: RainMap ≈ 0.1 ms, 20k drops ≈ 0.2 ms, wet shading ≈ free, one garment RT ≈ 0.05 ms.

User's corrections (2026-09-18, evening): do NOT tie the rain to the cloud layer; instead have weather
STATES that change post-processing, colours, cloud colour/shape. The nylon must be one opaque material
with the skin drawn inside (a character's leg is one mesh); the knit must read as cells/loops, not
vertical stripes; from afar smooth or a faint moire only. Three lab objects: white / black / nude.
Wetting per material: a "can get wet" switch, drop spots that fade in ~a minute, a 0..1 whole-material
wetness.

#### Built the same evening (autonomous loop)
- [x] `ClothNylon.shader` v2: `_SKIN_UNDER` keyword = one opaque mesh with the skin shaded inside
      (blend/zwrite via hidden `_SrcBlend/_DstBlend/_ZWrite`); knit = two diagonal thread families →
      diamond cells; `Stretch()` from screen derivatives vs the UNSTRETCHED size (`_SizeCm` 22×70,
      `_WalesPerCm` 28 at rest) so a thigh is sheerer than an ankle for free; far = smooth mean, a
      faint moire band (`_Grain`) only where a cell is 1-2 px; anisotropic Blinn-Phong (Ashikhmin-Shirley)
      along the thread; wet response (darker, glossier ×3, slightly more sheer, water-film glint).
      Lab: `FabricLab` at (99, 1.88, 91) — three tapered `LegMesh.asset` legs, White/Black/Nude materials.
- [x] Wetness: `ClothWet.hlsl` (`WetAt(uv) = _Wetness + _WetMap`), `Wettable.cs` (per-mesh 512² wet map
      in UV space, `ClothUnwrap.shader` bakes world position per texel, `WetMap.compute` Dry + Splat,
      spots run 2.5× further down than up, MPB only), `Rain.Pour` throws slanted rays through each
      Wettable's bounds (MeshCollider needed). Verified: MPB path centre pixel 0.275 → 0.157 at wetness 1.
- [x] R1 RainMap: `RainMap.cs` (+`RainMapIgnore` marker on shell-grass meshes) builds a 64 m / 512²
      top-down height map with `RainHeight.shader` using **BlendOp Max** (no depth buffer — the reversed-Z
      depth test was fighting the manual projection; max blending is order-independent and simpler).
      Terrain comes from `GrassField.HeightMap` via a lifted 128² grid. Verified against `SampleHeight`
      (±0.1 m) and object tops. `RainMap.hlsl`: `RainLandingHeight`, `RainShelter`.
- [x] R2 drops: `RainDrop.shader` + `RainDrops.cs` — `RenderPrimitives` quads, world-periodic columns
      (no popping), slant from `_RainVelocity`, end at the landing height, lit by `AirLight.hlsl`
      (extracted from GrassWindStreak — both use it now). Exposure 0.1: at 0.6 the drops hit 5.8 in HDR.
      Night: drops glow only near lamps. Rain object in GrassLab: `Rain` (intensity 0 by default) +
      `RainMap` + `RainDrops`.
- [x] R3 splashes (v1): the drop IS its splash — for the first 0.6 m of "fall" past the landing height
      the same quad is drawn as a growing, fading round dot at the landing point (RainDrop.shader).
      No ripples / puddles yet.
- [x] R4 `Wet.hlsl` (`WorldWetAt` = `_RainWetness` × open-to-sky × facing-up; `WetAlbedo`, `WetGloss`)
      hooked into `GrassLook.GrassShade`, so blades, ground and shell grass all darken and gloss in one
      place. `Rain.cs` soaks the world over `_soakTime` 30 s and dries it over `_dryTime` 180 s.
- [x] Play Mode smoke test 2026-09-18: no errors; 30k drops ≈ no fps change (an early 48-fps reading was
      first-use shader compilation); legs 12-14% wet after 30 s of rain, spots gone ~2 min after it
      stopped, world wetness 1.0 → 0.76 after 44 s dry.
- [x] Second pass the same night (user's review): `Rain_Zone (walk in: it rains)` = BoxCollider trigger
      + local Volume (`Settings/GrassLabRainZone.asset`: ColorAdjustments −0.25 EV / −20 sat / cool
      filter, WhiteBalance −12, Vignette 0.22) + `RainZone.cs` (ramps `Rain.Strength` over 4 s and
      `Atmosphere.Visibility` 8000 → 1500 while `Camera.main` is inside). `Rain._dropsPerCubicMetre`
      (3) now drives BOTH the drawn drop count (× the shader box volume) and the wetting flux
      (× fall speed) — one number, so what you see and what lands agree (~1.4 hits/s on a leg).
      Hits on Wettables spawn `RainSplash.shader` crowns (`Rain.Hits` ring buffer, 0.25 s), because the
      RainMap cannot see the sides of a leg. `Wettable` rebakes its position map when the transform
      changes (the user's scaled leg had a stale map → wrong spots). Wet map is now an AMOUNT of water:
      a drop deposits 3 (super-saturated, 6 mm radius), `WetMap.compute Settle` diffuses it per axis in
      metres (`_wicking` mm²/s: nylon 0.5, cotton 3, knit 8; passes per frame for stability) and
      evaporates it; `ClothWet.WetAt(uv, look)` maps amount → look (nylon 2, knit ~10). Measured on a
      leg: nylon spot 75 → 106 px in 15 s then fades; knit 75 → 190 px in 5 s.
- [x] Puddles + ripples (loop tick 3): `Puddles.cs` + `Puddles.compute` bake a puddle map once from
      `GrassField.HeightMap` (dip below the surroundings within `_reach`, only where the slope is small)
      into a global `_PuddleMap`; `Wet.hlsl` `StandingWater` fills the dips once world wetness > 0.5,
      `RainRipple` (hashed cells, expanding rings, `_Time`) tilts the water normal while it rains,
      `PuddleShade` = darker bottom + Fresnel sky reflection + sun glint. Hooked into `MeadowGround`
      only (×(1−0.6·grass)). Grass wet gloss lowered 0.6 → 0.35 (wet blades looked bleached).
      CAVEAT: the GrassLab meadow has no bare dips — every candidate spot is under blades or a
      shell-grass mesh, so puddles are not visible in the lab yet; they need a path/dirt patch
      (or a puddle-painting brush) to show. Defaults retuned after measuring the terrain (texel 0.78 m,
      2.3 m ring found dips >0.1 m on 0.1% of it): `_reach` 8 m, `_depth` 0.25 m, slope limit 0.12 →
      2.6% of the map >0.1, deepest 0.75; verified from above at (201, 1.1, 116): the dip goes dark and
      sheeny between the stems at world wetness 1.
- [x] Rainy sky (user's request, same night): ONE global `_SkyRain` 0..1, set by `RainZone.cs` on its
      own slower ramp (`_cloudTime` 20 s vs 4 s for the rain). `CloudLayer.hlsl` raises the cover to
      0.97 and softens edges to 0.5 INSIDE `CloudLayerDensity`, so SummerSky and CloudShadows.compute
      go overcast together (the ground loses the sun through the same cloud). `SummerSky.shader` then
      lerps the sky to grey (horizon 0.52/0.55/0.60 → zenith 0.38/0.42/0.48), hides the sun disc,
      dims the glow, greys the cloud colours (lit 0.62/0.64/0.68, shade 0.34/0.37/0.42) and kills
      the silver lining. `OnDisable` resets the global to 0.
- [x] Knit sweater shader (loop tick 4): `Art/Fabric/ClothKnit.shader`, opaque, procedural stockinette.
      Each point finds the nearest LEG of yarn (a V per stitch: segment (0,0)→(±0.5,1) in a cell one
      stitch wide, one row tall; six candidates = this stitch, the neighbour leaning in, rows above and
      below; ties at the crossings go to the leg lower down, which is the one on top). From it: cover
      (yarn radius 0.2·fill in stitch widths, anti-aliased by fwidth), a cylinder normal across the leg,
      a "sink" near the top where it ducks under the stitch above (normal tilts along, occlusion),
      the dark back in the holes. Knitter's numbers: `_StitchesPerCm` 2.5, `_RowHeight` 0.75, `_YarnFill`
      1. Wool = no gloss + FUZZ halo (rim^2.5 lit from any side, most from behind). Far: stitches fade
      to `MeanCover` once ~2 px. Wet reads the map at look 10 (nylon 2): yarn → 0.45× and colour pushed
      deeper, fuzz ×0.1, broad sheen (pow 24), yarn swells 15%. `ClothKnit_Wool.mat` on `Leg_Knit`
      (x +1.4, `Wettable` wicking 8 mm²/s). Verified at 20 cm (rows of Vs, small holes) and 1 m
      (smooth cream column, wet = dark tan). Not done: shell layers for real depth (TODO above) — the
      normal + occlusion trick was enough at these distances.
- [x] Knit sweater v1 (loop tick 4): `Art/Fabric/ClothKnit.shader`; lab object `FabricLab/Leg_Knit`
      with `ClothKnit_Wool.mat` (made by the parallel session — the two sessions both wrote the shader
      file, this version won; the duplicate `Sleeve_Knit`/`ClothKnit.mat` were removed). Stockinette as a height field:
      four fat yarn lines per cell (our V's legs + the neighbours' legs meeting at the top corners),
      cylinder profile, plied twist along the yarn; from it parallax (`_Depth`), bumps (3 taps),
      hollow occlusion. Fuzz = asperity rim halo (`_Fuzz`, `_FuzzColor`) + backlight through the rim
      (`_Translucency`). Wet (`WetAt(uv, 10)`): albedo dark+deep, fuzz ×0.1, loops flattened ×0.5,
      broad weak film gloss — the opposite of the nylon by design. Bumps fade past ~3 px per stitch.
      Fixed on the way: `Wettable` now clears its wet maps on creation (a fresh RenderTexture holds
      old memory — the sleeve started out randomly "damp" and rendered beige).
      Left for the user's eye: stitch reads more like a waffle than V's at macro (`_YarnRadius`,
      the 0.7 "theirs" factor in `Knit()`), fuzz halo strength (`_Fuzz`, rim power 1.8).
- [x] R5 on the character (loop tick 5): `Tena_Lab` (TenaRigged.fbx, Tena.controller) stands in the
      FabricLab at local (−1.6, 0, 0). Her Dress and Skirt wear `TenaDress_Knit` / `TenaSkirt_Knit`
      (ClothKnit, charcoal 0.22, gauge 4 and 5 st/cm). TRAP: the toon atlas UVs are collapsed onto
      colour swatches (UV area ≈ 0), useless for a knit or a wet map — `TenaDress_Unwrapped.asset` /
      `TenaSkirt_Unwrapped.asset` are mesh copies with a lightmap-style unwrap moved into UV0
      (`Unwrapping.GenerateSecondaryUVSet`, tangents recalculated; bones/bindposes survive
      Instantiate). `_SizeCm` per material = sqrt(world area / UV area) × 100 (≈197 and 208 cm/UV).
      `SkinnedMeshCollider.cs` re-bakes a MeshCollider from the SkinnedMeshRenderer every 3 frames so
      drops hit the posed garment — `BakeMesh(mesh, useScale: TRUE)` is the one that gives mesh-space
      vertices for a collider on the same (x100 Mixamo) transform; `false` came out 100× too big and
      no drop ever hit. Play test in the rain zone: 2130 hits on the dress, 901 on the skirt in 25 s,
      spots visible, ~200 fps. Knit wet look lowered 10 → 4: at 10 every spot clamped to soaked out to
      its halo and the skirt looked cow-spotted. She stood in the bind pose in that test: the Animator's
      culling was CullUpdateTransforms and no camera was looking at her — set to AlwaysAnimate.
- [x] MagicaCloth2 response (after the fork session's hand-off): `Wettable.Average` (0..1, slider +
      wet-map mean via `AsyncGPUReadback` twice a second, ×2 like the shader's look) and
      `Scripts/Fabric/WetCloth.cs` on the character root: per `MagicaCloth`, finds the `Wettable` of its
      `sourceRenderers`, then gravity × (1 + 1.5·wet) and damping + 0.3·wet via `SerializeData` +
      `SetParameterChange()` (only when wet moved by > 0.02 — each change rebuilds the cloth).
      UNTESTED with a real cloth: GrassLab's Tena_Lab has no MagicaCloth; the Game/Piano scenes do
      (4 cloths on Tena). To try: add `WetCloth` to Tena in Game.unity + `Wettable`s on the garments.
- [x] "The dress is see-through" (user, evening): NOT alpha. `PC_Renderer` runs SSAO with Source =
      DepthNormals, so URP builds the depth texture with a DepthNormals PREPASS and any shader without a
      `DepthNormals` pass is simply absent from it; `VolumetricLight` (the Atmosphere haze) then paints
      the far horizon's fog over the object — sky tint above the horizon line, ground tint below,
      exactly like glass. Grass shaders already had the pass; added it to `ClothNylon`, `ClothKnit` and
      `Shaders/CelShader` (+ DepthOnly there). RULE: every opaque shader in this project needs
      ForwardLit + ShadowCaster + DepthOnly + DepthNormals.
- [x] Knit taken OFF Tena at the user's request (texture not convincing yet): Dress/Skirt back on
      `TenaToon` with the original meshes, `Wettable`/colliders removed, the unwrapped meshes and
      `Tena*_Knit.mat` deleted. `Tena_Lab` stays in the lab.
- [x] Tights mesh "microscopic" (user): true to life, 28 wales/cm = 0.36 mm cells, sub-pixel past 10 cm.
      Ranges widened (`_WalesPerCm` 2..40, `_Denier` 5..400); lab legs set to 4 wales/cm (2.5 mm cells),
      250 den (nude 150) × bulk 4 so the cord still covers; drawn pattern now kept until a thread is
      under HALF a pixel (`far = smoothstep(1.0, 2.5, ...)`, was 0.5..1.0). Visible at 0.5 m, still
      smooth at 2 m — a mesh that reads from 2 m needs ~2 wales/cm (5 mm, fishnet) and a thread colour
      that contrasts with the skin; the numbers are the user's call.
- [x] Wind streak "born in the ground" (user, evening): the ParticleSystem emitter spawned every
      streak at y = 0 and `GrassWindParticles` lifted it to ground + height a frame later — but the
      trail module had already recorded the birth point, so each streak started with a vertical line
      standing in the grass. Fix: emission module switched off in OnEnable; `Spawn()` emits the same
      rate (`rateOverTime.constant`, 30/s, fractions carried in `_owed`) itself via `Emit(EmitParams)`
      at a random spot in the area, already at ground + random height. Play check: 117 alive, no errors.
- [ ] Rain sound: no rain clip in `Assets/_Tender/Audio` — needs an asset; plan = one looping clip with
      volume ∝ `Rain.Intensity`, low-passed under a roof (`RainShelter` at the listener).
- [ ] Tuning left for the user: `RainDrop.mat` `_Exposure` 0.1 / `_Length` 0.15; `Rain` direction slant;
      `Wettable._dryTime`; nylon `_Grain` (moire) and `_Sheen`.
- [ ] Known: a thin open tube (the lab legs) barely registers in the RainMap (vertical walls have no
      top-down area) — real roofs and rocks do. Character wetting uses raycasts, not the map.
- [ ] PNG readback trap: ReadPixels HDR → RGB24 wraps values > 1 to dark; read RGBAHalf and clamp first.

### Wetness sim — where the number comes from
- [ ] Start with ONE scalar per garment, pushed by a `WetnessController` on the character via
      `MaterialPropertyBlock` — **not** `.material` (that clone leak is already a P0 above).
- [ ] `RainManager`: global intensity 0..1 that feeds both the VFX and the accumulation, so there is a
      single source of truth for "how hard is it raining".
- [ ] Exposure, or the whole thing reads as a fade: shoulders and head soak first, the belly last, and
      nothing at all under a roof. Cheap version = a short upward raycast per region; the value it returns
      is the multiplier on accumulation.
- [ ] Per-region next (shoulders / sleeves / back / hem), before reaching for textures. Regions are
      probably enough for a game seen at this distance — a mask is only worth it if regions read as bands.
- [ ] Then, if needed: a wetness MASK in UV space, accumulated over time. The point of the mask is the
      spreading wet FRONT (a blurred edge creeping outward), which a scalar cannot do at all.
- [ ] Contact wetness, not just sky: walking through wet grass wets the hem and shoes. The grass system
      already knows where the character is (`GrassInteractor` on the player) — reuse that, do not invent a second probe.
- [ ] Drying curve: rate scaled by wind and sun exposure, and MUCH slower for absorbent knit than for nylon.
      Non-linear — the last 20% of drying takes as long as the first 60%.
- [ ] Debug panel: scrub wetness 0..1 live, plus "soak" / "dry" buttons. Without this, tuning the shader
      response means standing in the rain for a minute per iteration.

### `ClothWet.hlsl` — what wetness DOES to a surface (shared by both fabrics)
- [ ] Albedo darkens non-linearly, and by a per-fabric amount: water filling the fibre pores is what
      darkens cloth, so absorbent knit goes very dark and nylon barely changes. One curve, one multiplier.
- [ ] Smoothness up + a thin water sheen layer on top. Keep the wet highlight BROADER than a plastic one,
      or wet cloth reads as vinyl.
- [ ] Saturation drifts up as it darkens (wet colours look deeper, not just darker).
- [ ] Normal/fuzz flattening: wet fibres mat down, so the fuzz rim and the micro-normal both fade with
      wetness. This is the cue that sells "soaked" more than the darkening does.
- [ ] Droplet beading on repellent fabric (nylon) vs. no beads on absorbent knit; drip trails on the hem.

### Knit sweater shader (`ClothKnit.shader`)
- [ ] Yarn structure: tiling knit normal + height (stockinette or rib). Generate it procedurally the way
      `GroundAlbedo`/`GroundNormal` were generated, rather than sourcing a texture.
- [ ] Depth between the loops — a knit is not a flat surface. The shell trick from `ShellGrass.shader`
      transfers directly here: a couple of lifted layers with holes punched in them, or parallax if shells
      cost too much on a garment.
- [ ] Fuzz rim: a wide, soft sheen lobe (asperity scattering) for the halo of loose fibres. Strongest
      against backlight, and the first thing to die when the sweater is wet.
- [ ] Translucency at the rim where light bleeds through loose stitches.
- [ ] Wet response: darkens hard, fuzz collapses, loops mat together, garment sags.

### Nylon / synthetic shader (`ClothNylon.shader`)
- [ ] Thin, tightly woven: anisotropic highlight aligned to the weave, plus a sheen term.
- [ ] Ripstop grid, and a faint iridescent shift at grazing angles.
- [ ] Two-lobe spec (broad sheen + tight highlight) — cheaper and closer than one GGX lobe.
- [ ] Wet response is the OPPOSITE of the knit: albedo nearly unchanged, smoothness jumps, water sits in
      visible beads and runs off. Getting these two fabrics to diverge is the whole point of the feature.

### Simulation / cloth motion
- [ ] Wet cloth is heavier and calmer: shift MagicaCloth2 mass / stiffness / damping with wetness so a
      soaked sweater stops fluttering and starts clinging.
- [ ] Decide whether wetness persists in `SaveData` (probably not — dry on load is fine and simpler).
