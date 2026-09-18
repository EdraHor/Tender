# Lighting Plan — "Dark by Default, Sun Through the Gap"

> Target look: a moody landscape that is dark by default (thick clouds), with the sun breaking
> through a cloud gap to dramatically light a local area. Researched + adversarially verified
> against this project's URP 17.3 source (2026-07-18). Verdict up front: **stay on URP, no RTX,
> no HDRP.** Build phases 1–4.

## 0. The mental model (read first)

Turning the Directional Light down to ~0 is the **wrong lever** — it's why everything breaks.
A frame reads as "night" because of three separate things, none of which is sun intensity:

1. **Ambient/environment light is crushed** → surfaces the sun doesn't hit fall toward black.
2. **The sun is *blocked* per-area** (cloud shadow) → most ground gets no direct sun even at full strength.
3. **HDR + tonemapping + exposure** compress a huge radiance range (near-black meadow ↔ blazing
   cottage) into a dark frame with a few bright pixels.

**Keep the sun STRONG.** Its intensity sets how bright the *lit* areas are, and the volumetric
cloud raymarch samples the main light for in-scattering — dropping it to 0 flattens the contrast
*and* starves the clouds. "Dark" = **ambient + shadowing + exposure**, not a weak sun. The rule:
don't darken via sun intensity; darken via ambient + exposure, and use **cloud shadowing to decide
*where* the sun lands.** Cloud shadows are the single biggest lever.

## Phase 1 — Foundation: exposure, ambient, tonemapping (~½ day, zero code, all built-in)

Gets ~60% of the mood and makes every later step legible.

- **1.1 HDR on.** URP Asset (`PC_RPAsset.asset`) → Quality → HDR. (Already on.) Without it,
  radiance clamps at 1.0 and the sunlit patch can't stay bright above a black scene.
- **1.2 Crush ambient.** Window > Rendering > Lighting > Environment → **Source = Color/Gradient**,
  a **very dark desaturated blue-grey** (not literal black, or shadows lose silhouette). This is the
  single most important "dark by default" knob. Prefer Color/Gradient over Skybox because you'll
  drive ambient dynamically from cloud cover later (2.4), and the Skybox ambient probe doesn't
  auto-update with an animated sky.
- **1.3 Tonemapping.** Global Volume → Post-processing → Tonemapping. URP has exactly **None /
  Neutral / ACES**. Start with **ACES** (cinematic crushed shadows/highlight rolloff).
- **1.4 Color Adjustments + Bloom.** Post Exposure (down) + Contrast (up) — URP has **no
  auto-exposure**, Post Exposure is a fixed authored value (fine for a crafted scene). Bloom with
  **Threshold above the dark ambient floor** so only the sunlit break glows.

## Phase 2 — Cloud shadows: the biggest lever (~2–4 days)

Goal: the clouds overhead darken the ground beneath them, leaving one bright gap. **Recommended:
a directional-light COOKIE fed by a scrolling cloud-coverage texture.**

- **Why the cookie:** URP's `GetMainLight(positionWS)` multiplies the main light by
  `SampleMainLightCookie(positionWS)` when `_LIGHT_COOKIES` is set, and the stock **Lit/SimpleLit
  shaders already do this** (verified in this project's `RealtimeLights.hlsl:114-116`,
  `Lit.shader`). So a directional cookie dims the sun per-pixel on terrain, cottage, characters
  with **zero per-material shader edits**. Black texel = near-night (ambient only); white = full sun = gap.
- **2.1 Enable cookies** in `PC_RPAsset.asset` (Lighting > Advanced). Already `m_SupportsLightCookies: 1`.
- **2.2 Coverage generator (the one custom part).** A **CustomRenderTexture** running a horizontal-slice
  copy of `GetCloudDensity` from the cloud shader — same world-space noise, `_Coverage`, `_WindSpeed*_Time`
  scroll → a 2D coverage/transmittance texture. Reusing the exact noise+wind makes the ground shadow
  **match and drift with the clouds overhead.**
- **2.3 Assign as cookie.** CRT → Directional Light Cookie slot; Cookie Size large (~200+). Directional
  cookies project orthographically and tile by default (set Clamp to defeat tiling). RT cookies are
  accepted (enables scroll). Low-sun caveat: the cookie smears horizontally under a near-horizon sun —
  usually *desirable* for a dramatic god-ray sun.
- **2.4 Drive ambient DOWN from the same coverage — do not skip.** Cookie/shadow maps attenuate only the
  **direct** term; ambient/GI is untouched. Sample the coverage (or its average) in a small script and
  drive `RenderSettings.ambientLight` / a dark gradient so under-cloud areas lose ambient too. **This is
  what makes "near-night under clouds" land.**
- **2.5 Keep main-light shadow maps on.** Independent of and stacking with the cookie — soft cloud
  dimming + crisp cottage/tree shadows inside the gap.
- **Alt (b):** if you later need shadow independent of sun angle (top-down, no smear/tiling), publish an
  ortho top-down RT via `Shader.SetGlobalTexture` (in `RecordRenderGraph`, not a legacy CommandBuffer) and
  sample it in a **custom Lit / Shader Graph Custom Function** (stock Lit can't sample an arbitrary global RT).
- **Note (c):** a per-ground-pixel raymarch toward the sun *is* real-time-feasible (~16–32 steps); prefer
  the 2D precompute for **efficiency/reuse**, not feasibility.

## Phase 3 — Depth + visible sun shafts (god-rays)

- **3.1 Built-in distance fog (free).** Lighting > Environment > Fog (Linear/Exp/Exp2), dark color. This is
  URP's **only** built-in fog — distance color blend, **no height gradient, no in-scattering, no shadowing,
  cannot make god-rays.** Depth/mood floor only.
- **Hard fact:** Unity 6 URP has **NO built-in volumetric fog / volumetric lighting** (HDRP-only, still true
  through 6.4). Any real sun shaft is custom.
- **3.2 Placeholder (afternoon): screen-space radial-blur god-rays** (Renderer Feature). Cheap, validates
  art direction, but **view-dependent** (only toward the sun, a 2D smear ignoring 3D geometry).
- **3.3 Target (high effort): one fullscreen raymarched volumetric-light Renderer Feature.** Reconstruct
  world ray from depth, march N steps, accumulate in-scattering with a Henyey-Greenstein phase — constant
  term = volumetric fog, shadow-modulated term = shafts (both in one pass). Fully supported in URP
  RenderGraph (refs: CristianQiu `Unity-URP-Volumetric-Light` MIT; Valerio Marty).
  - **Reuse the 3D noise/density + toward-sun transmittance** from the cloud shader — **not** its
    box-bounded object-space Cull-Front transparent loop (that's a geometry pass; this is fullscreen).
  - **The transparent cloud is NOT in URP's opaque shadowmap**, so beams won't emerge from the gap for free.
    Couple it in by **sampling the Phase-2 coverage/cookie map** in the fog pass (cheap, reuses what you built).
  - Run at **half/quarter res + bilateral upsample + temporal jitter** to stay cheap.
- Paid assets (Ethereal URP / AERO / HAZE) only if timeline beats the "keep it teachable" constraint;
  the free MIT package is the better reference.

## Phase 4 — Local / gameplay lights (~1 day, all built-in)

The setup is already correct.

- **4.1 Stay on Forward+.** `PC_Renderer.asset` `m_RenderingMode: 2` = Forward+ (verified). It **ignores**
  the "Additional Lights Per Object Limit" (dead value 4) — additional lights are always per-pixel, no
  4-per-mesh cap. Ceiling is **per-camera: 256 desktop** (32 mobile / 16 GLES3). Do NOT switch to Deferred
  (loses MSAA, fights CelShader).
- **4.2 Local sources = point/spot.** Torch = spot child of player; windows = small spots/points. Distance
  falloff is built-in (range-bounded inverse-square from Range + Intensity). On the near-black base, dramatic.
- **4.3 Additional-light shadows sparingly.** Already on (2048 atlas). Cast shadows only where needed; prefer
  **spot (1 shadow map) over point (6 cube faces)**. No fixed count cap — limit is atlas fit; over-budget
  lights quietly drop shadows (Editor warns).
- **4.4 Rendering Layers to gate the sun** (`m_SupportsLightLayers: 1`). Put meadow/cottage on a "Sunlit"
  layer, set the sun's Rendering Layers mask to only that. Hard binary include/exclude (soft shaping still
  from Phase 2/3); the shadow layer mask must agree.
- **4.5 Cheap glow:** emissive materials on windows/lanterns (needs Phase-1 Bloom), cookies for window-mullion
  dapple, Mixed/baked lighting for static lit windows so runtime only pays for the moving torch.

## Phase 5 — Verdict: RTX and HDRP

**Do NOT pursue hardware ray tracing. Do NOT migrate to HDRP.** (Both verified, load-bearing.)

- **RTX in URP is impossible.** In Unity 6, hardware DXR ray tracing is **HDRP-only**; URP has no RT path —
  an engine limitation, not a toggle. Off the table regardless of GPU.
- **RTX solves the wrong problem.** This look is **shadowing + volumetric scattering + exposure/tonemapping**.
  Scattering through cloud/fog is **raymarching**, not BVH ray tracing. RT GI/AO could *enhance* ambient bleed
  around the cottage, but it's never the driver.
- **HDRP would give a lot out of the box** (native volumetric fog+lighting, volumetric clouds with cloud
  shadows, PBR sky, physically-based auto-exposure) — but **no automated URP→HDRP converter exists**; you'd
  re-shader every material and rewrite every custom shader by hand (cloud raymarcher, CelShader,
  smoothed-normals) + re-author lighting. Multi-week, and it contradicts the "simple/teachable, build our own
  clouds" philosophy — HDRP would make the cloud raymarcher largely redundant.
- **Recommendation:** stay on URP, build Phases 1–4. Revisit HDRP only if the custom volumetric-light +
  exposure work proves visually insufficient *and* you'll absorb a full pipeline rebuild.

## Build order at a glance

| # | Step | Type | Effort | Lever |
|---|------|------|--------|-------|
| 1 | HDR + crush ambient + ACES + exposure/bloom | Built-in settings | ~½ day | Makes "dark" real |
| 2 | Cloud-coverage cookie from existing noise + drive ambient from it + keep shadow maps | Custom CRT gen, built-in receiver | 2–4 days | **THE look** |
| 3 | Distance fog → radial god-rays → raymarched volumetric feature (reuse cloud density + Phase-2 map) | Built-in → Renderer Feature → custom RG pass | ½ day → afternoon → high | Depth + shafts |
| 4 | Point/spot locals, spot shadows, Rendering Layers, emissive | Built-in (already Forward+) | ~1 day | Local drama |
| 5 | No RTX, no HDRP | Decision | — | Stay the course |

Relevant files: `Assets/_Tender/Art/Clouds/HeroCloudVolume.shader`, `.../CloudHero.shader`,
`Assets/_Tender/Settings/PC_RPAsset.asset`, `Assets/_Tender/Settings/PC_Renderer.asset`.
