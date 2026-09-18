Shader "Tender/ShellGrass"
{
    // Short grass - ankle height and below - drawn as stacked shells.
    //
    // The same patch of ground is drawn several times over, each copy lifted a little further
    // along the surface normal. Each layer asks one question per pixel: "at this spot, is the
    // blade of grass still this tall?" If not, the pixel is thrown away. Stack sixteen of those
    // and the holes line up into blades.
    //
    // It is almost free because there is no geometry to speak of - a few thousand triangles for
    // a whole patch, no instancing, no compute, no per-frame C#. What it costs is overdraw, so
    // it is the right answer for grass a few centimetres tall and the wrong one for a meadow.
    // Tall grass is GrassBlade.shader, and the two share their wind so the seam does not show.
    //
    // The field is built at TWO scales, and knowing which is which explains most of the code
    // below. Single blades - their height, their lean, whether they dried out - live at two
    // centimetres, so they exist only while a pixel is smaller than that, and past that limit
    // they merge and hand over rather than being drawn wrong. Tufts and patches live at thirty
    // centimetres and at metres, so they are still many pixels wide across the whole view, and
    // they NEVER fade: at distance they are the only thing left to look at, and a field with
    // nothing at that scale is the flat green sheet this shader used to draw.

    Properties
    {
        [Header(Colour)]
        [Toggle] _MatchMeadow ("Use the meadow's colours (GrassLook)", Float) = 0
        _RootColor ("Root colour", Color) = (0.09, 0.17, 0.05, 1)
        _TipColor ("Tip colour", Color) = (0.38, 0.62, 0.18, 1)
        _DryColor ("Dried-out blade colour", Color) = (0.60, 0.58, 0.32, 1)
        _DryShare ("Share of blades gone dry", Range(0, 1)) = 0.2
        _RootAO ("Root darkening", Range(0, 1)) = 0.55

        [Header(Clumping)]
        _ClumpScale ("Tufts per metre", Range(0.5, 12)) = 3.5
        _Clumping ("How much a tuft changes the height", Range(0, 1)) = 0.5

        [Header(Shape)]
        _ShellHeight ("Grass height (m)", Float) = 0.12
        _BladesPerMetre ("Blades per metre", Float) = 55
        _MinBlade ("Shortest blade", Range(0, 1)) = 0.35
        _Thickness ("Blade thickness", Range(0.2, 2)) = 1.0
        _Scatter ("Scatter inside the cell", Range(0, 1)) = 0.85
        _Round ("Normal rounding", Range(0, 2)) = 0.7
        _Lean ("Resting lean (cells at the tip)", Range(0, 1)) = 0.5
        _Ribbon ("Blade flatness (1 = round)", Range(1, 4)) = 2.0
        _GrazeFill ("Close the shell seams edge-on", Range(0, 3)) = 1.0
        _SlabJitter ("Sample inside the gap between layers", Range(0, 1)) = 1.0
        [NoScaleOffset] _JitterNoise ("Jitter noise (tiling blue noise)", 2D) = "gray" {}
        [HideInInspector] _ShellStep ("Gap between layers, 0-1 of the height", Float) = 0.0666

        [Header(Detail limit)]
        _DetailFrom ("Blades start merging at (cells per pixel)", Range(0.1, 4)) = 1.0
        _DetailTo ("Solid carpet past (cells per pixel)", Range(0.2, 8)) = 3.0

        [Header(Motion)]
        _Sway ("How far the whole layer sways (m)", Range(0, 0.5)) = 0.05
        _WindLean ("How much wind leans single blades", Range(0, 2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "GrassCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _MatchMeadow;
            float4 _RootColor;
            float4 _TipColor;
            float4 _DryColor;
            float _DryShare;
            float _RootAO;
            float _ClumpScale;
            float _Clumping;
            float _ShellHeight;
            float _BladesPerMetre;
            float _MinBlade;
            float _Thickness;
            float _Scatter;
            float _Round;
            float _Lean;
            float _Ribbon;
            float _GrazeFill;
            float _SlabJitter;
            float _ShellStep;
            float _DetailFrom;
            float _DetailTo;
            float _Sway;
            float _WindLean;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float2 shell : TEXCOORD1;      // x = this layer's height, 0 at the ground, 1 at the top
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float  shellT : TEXCOORD2;
            float  fogCoord : TEXCOORD3;
            float3 groundWS : TEXCOORD4;

            // The wind here, carried down from the vertex stage.
            //
            // Single blades lean with the wind, which is a per-PIXEL question, and GrassForce is
            // far too expensive to ask per pixel - two noise fields plus a loop over every object
            // pushing the grass around. The wind field is metres across and the ground mesh has a
            // vertex every half metre or so, so interpolating it is indistinguishable from
            // sampling it and costs one interpolator instead.
            float2 force : TEXCOORD5;

            // The big gust here, for the meadow lighting's travelling highlight. Same reasoning.
            float gust : TEXCOORD6;
        };

        Varyings ShellVertex(Attributes IN)
        {
            Varyings OUT;

            float3 groundWS = TransformObjectToWorld(IN.positionOS);
            float3 normalWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
            float shellT = IN.shell.x;

            // Blades this short do not need an arc: a lateral shove that grows towards the tip is
            // indistinguishable at ten centimetres, and it costs three instructions.
            float2 force = GrassForce(groundWS);
            float3 offset = normalWS * (shellT * _ShellHeight);
            offset.xz += force * (pow(shellT, 1.5) * _Sway);

            OUT.groundWS = groundWS;
            OUT.force = force;
            OUT.gust = GrassGust(groundWS.xz);
            OUT.positionWS = groundWS + offset;
            OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
            OUT.normalWS = normalWS;
            OUT.shellT = shellT;
            OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
            return OUT;
        }

        /// How much of a blade cell fits inside one screen pixel.
        ///
        /// This is the honest measure of "too far to draw", and it replaces a distance in metres.
        /// A fixed distance cannot work: looking along the ground at a grazing angle, one pixel
        /// covers many times more grass than the same pixel does looking straight down, so any
        /// distance tuned for one view is wrong for the other. Screen-space derivatives give the
        /// real footprint, so the limit adjusts itself to the angle, the field of view and the
        /// resolution without a single number to re-tune.
        float ShellFootprint(float3 groundWS)
        {
            float2 cell = groundWS.xz * _BladesPerMetre;
            float2 width = fwidth(cell);

            // How many cells land in this pixel by AREA - not how many land along its worst axis.
            //
            // max() looks like the careful choice and is what used to make the field collapse a
            // couple of metres out. Looking along the ground at eye height, the derivative ACROSS
            // the view stays small while the one ALONG it runs to tens of cells per pixel, so
            // max() reports "hopeless" for ground the eye can still plainly see blades on. The
            // footprint's area is the honest count, and its square root is the side of the square
            // pixel that would cover the same amount of grass.
            return sqrt(max(width.x * width.y, 1e-8));
        }

        /// smoothstep, not saturate of a ramp, and on a small clearing you can see which is which.
        ///
        /// A straight ramp arrives at both ends with a corner in it: the rate of change jumps from
        /// something to nothing in the width of a pixel, and the eye finds that instantly - it
        /// reads as a drawn line on the ground where the grass "stops", which is exactly what the
        /// blend was supposed to hide. Easing both ends leaves nowhere for the line to be.
        float ShellFade(float3 groundWS)
        {
            float footprint = ShellFootprint(groundWS);
            return smoothstep(_DetailFrom, max(_DetailTo, _DetailFrom + 1e-3), footprint);
        }

        /// How tall the grass is allowed to be here, before any single blade is considered.
        ///
        /// This is the answer to "why does distant grass go flat", and it is worth being precise
        /// about what is and is not possible. An individual blade is about two centimetres across:
        /// past the point where one pixel covers a whole blade it CANNOT be drawn, by anyone, and
        /// trying anyway just trades a flat field for a boiling one. But a tuft is thirty
        /// centimetres across and a patch is metres across, and those are still many pixels wide
        /// at fifty metres. Structure at that scale is not merely possible at distance, it is the
        /// ONLY thing left to look at - so the clump field never fades out, and the per-blade
        /// detail fades into it.
        ///
        /// Two octaves because grass clumps at two scales: tufts, and broad patches of thin and
        /// thick growth. One octave reads as a regular bobble pattern.
        float ShellClump(float2 worldXZ)
        {
            float tuft = GrassValueNoise(worldXZ * _ClumpScale);
            float patch = GrassValueNoise(worldXZ * (_ClumpScale * 0.12) + 17.3);

            return 1.0 - _Clumping * (1.0 - (tuft * 0.6 + patch * 0.4));
        }

        /// How far up the colour ramp the merged carpet is allowed to reach.
        ///
        /// A pixel far away covers many blades, and what it averages is not their tips: it is tips,
        /// the lit sides lower down, and the dark gaps between them where the roots and soil show.
        /// Let the carpet's top read as pure tip colour with no root darkening and it comes out
        /// far brighter than the field of blades it replaces, and THAT jump is the line on the
        /// ground where the grass seems to stop - not the change in detail, the change in light.
        /// So the ramp is squeezed towards this as the blades merge, continuously, so the solid
        /// branch and the last stretch of the search agree exactly at the hand-over.
        #define FarRamp 0.6

        /// Which frame this is, for noise that should change every frame. Left at zero it never
        /// changes, which is what you want without temporal anti-aliasing: a still grain rather
        /// than a boiling one. A global, not a material property, so every patch agrees.
        float _ShellFrame;

        /// Blue noise, tiling every 64 pixels, read with Load so no sampler and no filtering touch it.
        ///
        /// It has to be BLUE noise, and the first version proved why. That one used interleaved
        /// gradient noise, which is excellent under temporal anti-aliasing and has a strong diagonal
        /// structure on its own - so without TAA every blade came out cross-hatched with a fine moire.
        /// White noise has no pattern but clumps into blotches. Blue noise is the one with neither:
        /// its energy sits entirely at the finest scale, which is what a dither is supposed to be.
        TEXTURE2D(_JitterNoise);

        /// Four random numbers from one 2D key, in one call. Same family and the same frac-first
        /// ordering as GrassHash, so it keeps its precision far from the origin (see there), from
        /// Dave Hoskins' "Hash without Sine".
        float4 GrassHash42(float2 p)
        {
            float4 q = frac(float4(p.xyxy) * float4(0.1031, 0.1030, 0.0973, 0.1099));
            q += dot(q, q.wzxy + 33.33);
            return frac((q.xxyz + q.yzzw) * q.zywx);
        }

        /// What the shading needs to know about the blade a pixel landed on.
        struct ShellHit
        {
            float  along;        // 0 at this blade's root, 1 at its own tip
            float2 fromCentre;   // where across the blade, for the rounded normal
            float  dryness;      // 0 green, 1 dead straw
            float2 tilt;         // slope of the clump's own surface; distance only
        };

        /// Decides whether this pixel is inside a blade, and if so how far up it is.
        /// Returns false when the pixel is empty air and should be discarded.
        ///
        /// Blades sit at a SCATTERED point inside their cell, not at its centre. A plain lattice
        /// of centres is the single most obvious giveaway in shell grass - from above it reads as
        /// a pinboard, and no amount of colour variation hides it. The cost of scattering is that
        /// a blade now overhangs into its neighbours, so all nine surrounding cells have to be
        /// asked whether one of their blades covers this pixel.
        ///
        /// The cell is keyed on the GROUND position, never on the displaced one. Key it on the
        /// displaced position and the pattern slides across the ground as the wind blows or an
        /// object leans the grass over - the blades appear to crawl, which is far worse than not
        /// animating at all. Leaning a single blade therefore happens HERE, by moving the stem
        /// the pixel is measured against, and never by moving the shell the pixel sits on.
        bool ShellSample(float3 groundWS, float shellT, float fade, float2 wind, float graze,
                         out ShellHit hit)
        {
            hit.along = 0.0;
            hit.fromCentre = 0.0;
            hit.dryness = 0.0;
            hit.tilt = 0.0;

            // The ground layer is solid: it is the soil showing between the blades.
            if (shellT < 1e-4) return true;

            // How tall the grass grows in this tuft. Every blade below gets a share of it.
            float clump = ShellClump(groundWS.xz);

            // Touched grass (GrassInteraction): flattened grass grows shorter, cleared grass not at all.
            // Only the layers above the ground care - the soil layer was already answered above.
            float4 touch = GrassTouchAt(groundWS.xz);
            clump *= 1.0 - max(touch.b * (1.0 - _GrassFlattenedHeight), touch.a);

            // Nothing grows above its tuft's ceiling, so every layer above it is thrown away here,
            // before the search. That matters more than it looks: nearly all of this shader's
            // cost is fragments that end up discarded - depth testing cannot skip a layer whose
            // shader might still discard, so every layer pays in full - and this is the cheapest
            // of them to throw away.
            if (shellT > clump) return false;

            // Thin growth is dry growth - worn patches show straw and soil. Unlike the per-blade
            // dryness further down, this survives to any distance, because a patch is metres wide.
            float clumpDry = _DryShare * (1.0 - clump);

            // Past the limit the blades are gone as INDIVIDUALS - not as grass. Each has grown to
            // its tuft's ceiling and wide enough to close the gaps, so the stack is solid here and
            // the nine-cell search has nothing left to decide.
            if (fade > 0.999)
            {
                hit.along = shellT / max(clump, 1e-4) * FarRamp;
                hit.dryness = clumpDry;

                // The search is skipped out here, which leaves the budget to ask which way the
                // tuft slopes and tilt the normal with it. Without this the far field is lit as
                // one flat plane however much its height varies underneath, and flat lighting is
                // most of what makes distant grass read as paint rather than as grass.
                float reach = 0.35 / max(_ClumpScale, 0.5);
                float2 slope = float2(ShellClump(groundWS.xz + float2(reach, 0.0)),
                                      ShellClump(groundWS.xz + float2(0.0, reach))) - clump;
                hit.tilt = -slope * (_ShellHeight / reach);
                return true;
            }

            float2 cell = groundWS.xz * _BladesPerMetre;
            float2 baseId = floor(cell);
            float2 local = cell - baseId;

            // Blades round off and stand up as they merge. Both have to go: a flattened blade is
            // narrow across one axis and a leaning one has moved off its cell, and either will
            // hold gaps open through the last stretch before the solid branch above takes over -
            // so the field breaks into a ring of holes exactly where it should be closing up.
            float ribbon = lerp(_Ribbon, 1.0, fade);
            float bend = 1.0 - fade;

            for (int i = 0; i < 9; i++)
            {
                float2 neighbour = float2(i % 3 - 1, i / 3 - 1);
                float2 id = baseId + neighbour;

                // Four numbers from one hash call, and the height test before anything else.
                //
                // Most blades are shorter than the layer being asked about, so most of these nine
                // iterations should cost one hash and one comparison. They used to cost five
                // hashes, a trig call and an ellipse test, and only then find out the blade never
                // reached this high. Measured, it is a large share of the patch's frame time.
                float4 rnd = GrassHash42(id);
                float grow = rnd.z;                   // this blade's share of the tuft's height

                // Two things happen to a blade as it stops being resolvable, and NEITHER of them
                // is shrinking.
                //
                // It grows towards the full height of its tuft, so the layer keeps the depth and
                // the silhouette it had close up. Scaling height down by (1 - fade) - which is
                // what this did - collapses the field onto the soil and leaves a flat green plane
                // ringing the camera a few metres out: the worst artefact this shader had. What
                // fades is only how far this blade DIFFERS from its neighbours, which is the part
                // a pixel this size could not have shown anyway.
                //
                // And it grows WIDER, until neighbours close over the gaps. Coverage going to one
                // is also what kills the shimmer, because past the limit there is no
                // high-frequency hole pattern left to alias against the pixel grid.
                float height = clump * lerp(lerp(_MinBlade, 1.0, grow), 1.0, fade);
                if (shellT > height) continue;

                float2 scatter = 0.5 + (rnd.xy - 0.5) * _Scatter;
                float turn = rnd.w;                   // the way it leans, and its place in the gust
                float along = shellT / max(height, 1e-4);

                // Where this blade's stem has got to at THIS height up it.
                //
                // Blades standing dead straight is the loudest tell that this is a shader and not
                // a field, so every one gets its own resting direction and its own share of the
                // wind with its own place in the gust - a field where every blade flutters in step
                // reads as a moving texture. It bends by along^2 because a blade is a cantilever:
                // almost all of the movement belongs near the tip, and the root barely shifts.
                //
                // Tall blades lean further, which is why `grow` sets the amount as well as the
                // height. Sharing the number is not laziness about hashes - it is true of grass.
                float leanSin, leanCos;
                sincos(turn * 6.2831853, leanSin, leanCos);
                float2 leanDir = float2(leanCos, leanSin);

                // Its own steady rate, not the speed the waves travel at: that is metres per second
                // across the field, and tied to it a fresh wind set every short blade buzzing.
                float gust = 0.7 + 0.6 * sin(_Time.y * 3.5 + turn * 6.2831853);
                float2 lean = (leanDir * (_Lean * (0.35 + 0.65 * grow))
                            + wind * (_WindLean * gust)) * bend;

                // A tip that leans further than this has walked out of the nine cells its
                // neighbours ask about, so the end of the blade is simply never drawn: in the wind
                // the field looks like it is being clipped along an invisible line. The stem sits
                // up to half a cell off centre already, which is why the limit is under one cell
                // and not one. Capping the lean costs a square root; allowing it costs
                // twenty-five cells of search per pixel instead of nine.
                float span = length(lean);
                lean *= min(span, 0.85) / max(span, 1e-4);

                float2 toStem = (neighbour + scatter + lean * (along * along)) - local;

                float radius = lerp((1.0 - along) * _Thickness * 0.5,  // narrows towards the tip
                                    0.9,                               // ...until it closes up
                                    fade);

                // Blades fatten as the view flattens, and THIS is what hides the seams between
                // the shells.
                //
                // The stack is a set of parallel sheets a few millimetres apart. Look down on it
                // and you see the top of the grass; look along it and a ray can pass through the
                // gap between two sheets without meeting a blade, so the field separates into
                // visible layers - the taller the grass, the wider the gaps and the worse it
                // gets. A wider blade is more likely to be in the way, so the stack closes up.
                // It is free in the only sense that matters: at a grazing angle nobody can
                // resolve one blade from the next anyway, so nothing true is being lost.
                radius = min(radius * (1.0 + _GrazeFill * graze * graze), 0.9);

                // A blade is a RIBBON, not a needle: wide across, thin in the plane it bends in,
                // the way a leaf is thin in the direction it curls. The area is held constant, so
                // flattening the blades does not quietly change how much ground they cover.
                //
                // The flattening is capped so the wide axis stays inside the ring of cells the
                // neighbours actually search - past that a blade covers pixels that never ask
                // about it, and its edge is cut off along a straight line. Blades that would
                // exceed it go rounder instead, which is invisible next to being clipped.
                float spread = min(ribbon, 0.95 / max(radius, 1e-4));
                float2 across = float2(-leanDir.y, leanDir.x);
                float2 inBlade = float2(dot(toStem, leanDir) * spread,
                                        dot(toStem, across) / spread) / max(radius, 1e-4);

                if (dot(inBlade, inBlade) <= 1.0)
                {
                    hit.along = along * lerp(1.0, FarRamp, fade);
                    hit.fromCentre = -(leanDir * inBlade.x + across * inBlade.y) * 0.5;

                    // A blade either dried out or it did not, and only the driest share did. This
                    // variation belongs to the blades themselves, so at distance it would be one
                    // random value per pixel - which is noise. It hands over to the clump instead.
                    // max() on the edge, because smoothstep divides by the width of its ramp and a
                    // share of exactly zero - a perfectly reasonable thing to ask for - would
                    // divide by nothing and scatter NaNs through the field.
                    float share = max(_DryShare, 1e-3);
                    float dry = GrassHash(id + 31.7);   // only the blade that was hit needs this
                    hit.dryness = max(clumpDry, smoothstep(1.0 - share, 1.0, dry) * bend);
                    return true;
                }
            }

            return false;
        }

        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            // The bottom shell lies in the same plane as the ground it was built from, so at a
            // grazing angle the two fight over the depth buffer and the field breaks into long
            // radial stripes. Nudging this surface towards the camera settles the argument.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5      // the meadow's grass types are a StructuredBuffer

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "GrassLook.hlsl"

            Varyings vert(Attributes IN) { return ShellVertex(IN); }

            half4 frag(Varyings IN) : SV_Target
            {
                float fade = ShellFade(IN.groundWS);

                // How flat-on this pixel is being looked at. 0 is straight down onto the grass,
                // 1 is along it - and along it is where the gaps between the shells open up.
                float3 viewWS = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float graze = 1.0 - saturate(abs(dot(viewWS, IN.normalWS)));

                // Take this layer's sample somewhere inside the gap BELOW it, not exactly on it.
                //
                // Every layer answers "is there grass at this exact height", so a blade's edge is
                // only ever known at the layer heights - and neighbouring pixels agree on those
                // heights, so the edge comes out as a regular staircase: the fern-like ribbing.
                // Asking each pixel about a different, random height inside the gap breaks that
                // agreement. The staircase has nothing ordered left to be made of, and dissolves
                // into a fine grain, which grass hides far better than it hides stripes. With
                // temporal anti-aliasing and _ShellFrame counting up, the grain averages away
                // entirely into the continuous blade the layers were approximating.
                //
                // The sample moves ALONG THE VIEW RAY, not straight down. Dropping straight down
                // would ask about a point this pixel cannot see; the ray is the only line of
                // points that belong to it. Past a steep angle the sideways step is capped, where
                // graze fill has already taken over.
                float3 groundWS = IN.groundWS;
                float shellT = IN.shellT;
                if (shellT > 1e-4)
                {
                    // Stepping by the golden ratio each frame visits every value in the most evenly
                    // spread order, so the average under TAA converges without ever repeating.
                    float noise = LOAD_TEXTURE2D(_JitterNoise, uint2(IN.positionCS.xy) % 64).r;
                    noise = frac(noise + _ShellFrame * 0.6180340);

                    float drop = noise * _SlabJitter * _ShellStep * (1.0 - fade);
                    float3 ray = -viewWS;
                    float3 sideways = ray - IN.normalWS * dot(ray, IN.normalWS);
                    groundWS += sideways * (drop * _ShellHeight / max(1.0 - graze, 0.25));
                    shellT = max(shellT - drop, 1e-3);
                }

                ShellHit hit;
                if (!ShellSample(groundWS, shellT, fade, IN.force, graze, hit)) discard;

                // The jitter decides WHETHER this pixel is inside a blade - it must not also decide
                // how it is shaded. The colour ramp and root darkening come from the height up the
                // blade, and reading that from the jittered sample paints the jitter's noise across
                // the whole face of every blade. Shaded from the layer's real height instead, a
                // blade's face is smooth, and the dither is left only on its edges, where it is
                // actually doing something.
                hit.along = saturate(hit.along * IN.shellT / max(shellT, 1e-3));

                // Bending the normal away from the blade's centre gives each one a rounded look,
                // which is most of what stops shell grass reading as flat stripes. Far out there
                // is no blade left to round off, and the slope of the tuft takes the job over.
                float3 normalWS = normalize(IN.normalWS
                                            + float3(hit.fromCentre.x, 0, hit.fromCentre.y) * _Round
                                            + float3(hit.tilt.x, 0, hit.tilt.y));

                // The gradient runs along THIS blade, not up the shell stack. shellT is an absolute
                // height above the ground, so using it means a short blade stops partway up the
                // ramp and never reaches the tip colour - the field ends up sorted by height into
                // dark patches and light ones. hit.along is 0 at the root and 1 at the tip whatever
                // the blade's height, which is what the ramp actually wants, and past the detail
                // limit it measures up the tuft instead.
                float up = hit.along;

                // A patch sitting in a meadow takes the meadow's colours, so the short grass reads
                // as the same field cut shorter instead of a carpet laid on top of it. The drift
                // between warm and cool green is looked up at the patch's own ground, exactly as the
                // blades and the terrain around it do.
                float3 root = _RootColor.rgb;
                float3 tip = _TipColor.rgb;
                float3 dry = _DryColor.rgb;
                if (_MatchMeadow > 0.5)
                {
                    // The field's base grass type - the one that grows everywhere.
                    root = _GrassGroundColor.rgb;
                    tip = GrassTipAlbedo(IN.groundWS.xz, 1.0, 0.5, 0, 1.0);
                    dry = _GrassTypes[0].dryTipColor.rgb;
                }

                float3 albedo = lerp(root, tip, up);
                albedo = lerp(albedo, dry, hit.dryness);
                float ao = lerp(1.0 - _RootAO, 1.0, up);

                // Lit by the same function as the tall grass and the ground, whichever colours it
                // uses. Two light models side by side is exactly the seam GrassLook exists to avoid.
                float3 colour = GrassShade(albedo, normalWS, IN.positionWS, up, IN.gust, ao, GetNormalizedScreenSpaceUV(IN.positionCS));

                colour = MixFog(colour, IN.fogCoord);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5      // GrassCommon.hlsl declares the grass types' StructuredBuffer

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            Varyings vert(Attributes IN)
            {
                // Only one layer in three casts, and the rest are pushed outside clip space before
                // any of their work is done, so the rasteriser throws them away for free.
                //
                // The shadow pass is roughly a third of this grass's cost, and it spends it on
                // detail the shadow map cannot hold: a cascade covering tens of metres has texels
                // centimetres wide, then soft-shadow filtering blurs them again. Three layers per
                // texel give the same shadow as nine. Which layers cast is decided when the mesh is
                // built and stored in uv1.y, so the top layer always does.
                if (IN.shell.y < 0.5)
                {
                    Varyings culled = (Varyings)0;
                    culled.positionCS = float4(2.0, 2.0, 0.5, 1.0);
                    return culled;
                }

                Varyings OUT = ShellVertex(IN);
                OUT.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(OUT.positionWS, OUT.normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                ShellHit hit;

                // Fade is deliberately ZERO here, not ShellFade(...).
                //
                // The detail limit is built out of screen-space derivatives, and in this pass the
                // "screen" is the shadow map. A cascade covering thirty metres has centimetre
                // texels, so the footprint comes out far past the threshold no matter where the
                // camera is, every blade merges to full width, and the patch casts one solid slab
                // instead of blade shadows. The shadow map wants the blades as they really are.
                if (!ShellSample(IN.groundWS, IN.shellT, 0.0, IN.force, 0.0, hit)) discard;
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
