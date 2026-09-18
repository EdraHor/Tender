Shader "Custom/CloudStep0"
{
    // A raymarched cloud built in three layers, in this order:
    //
    //   1. MASS      a few big ellipsoids, smooth-unioned as TRUE SIGNED DISTANCES (metres).
    //                An invisible seed: it decides where the cloud's weight sits, never what
    //                its outline looks like.
    //   2. BILLOW    self-similar Worley bumps SUBTRACTED from that distance. Because the field
    //                is real metres, subtracting h metres pushes the surface OUTWARD by h metres.
    //                That is displacement, and it is what erases the seed from the silhouette.
    //   3. EROSION   high-frequency noise that nibbles ONLY a thin outer shell, so the core stays
    //                solid and the inside holds up when the camera flies in.
    //
    // COST MODEL - read before touching the marching code.
    //
    // The view ray takes N steps; every step that lands in density runs a second, inner march
    // toward the sun. So the sun march is multiplied by N and completely dominates the shader.
    // The rule that follows: the VIEW field may be expensive, the SUN field must not be. The sun
    // march therefore runs a deliberately cheaper field - few billow octaves, no erosion, no
    // rippled base, no interior variation. Shadows are low-frequency; nobody can see the
    // difference, and it is worth several milliseconds a frame.
    //
    // Two more things keep the cost down:
    //   - empty air is sphere-traced on a NOISE-FREE field (mass only), so most steps are free;
    //   - step size and octave count both scale with distance, because a cloud 3 km away cannot
    //     show 5 m detail anyway.
    Properties
    {
        [Header(Mass)]
        _LobeBlend   ("Mass blend (m)",       Range(1,250))     = 70
        _MassShrink  ("Seed shrink vs billow",Range(0,1.2))     = 0.45
        _BaseHeight  ("Flat base height",     Range(0,1))       = 0.28
        _Softness    ("Edge softness (m)",    Range(0.5,60))    = 10
        _DensityMul  ("Density (per m)",      Range(0.005,0.4)) = 0.045

        [Header(Billow)]
        [NoScaleOffset] _BillowTex ("Billow 3D (tileable Worley)", 3D) = "white" {}
        _BillowScale   ("Largest lobe size (m)",   Float)          = 180
        _BillowAspect  ("Amplitude over lobe size",Range(0,0.9))   = 0.42
        _Octaves       ("Octaves",                 Range(1,6))     = 5
        _Lacunarity    ("Octave shrink",           Range(1.6,3.2)) = 2.2
        _VertSquash    ("Lobe vertical stretch",   Range(0.4,2.5)) = 1.30
        _TopBias       ("Growth toward crown",     Range(0.5,2.5)) = 1.35
        _BaseSoften    ("Base flattening",         Range(0.02,0.8))= 0.30
        _BaseRipple    ("Base raggedness (m)",     Range(0,150))   = 55
        _BaseBlend     ("Base softness (m)",       Range(1,120))   = 35

        [Header(Erosion)]
        _Erosion       ("Edge erosion (m)",        Range(0,50))    = 11
        _ErosionScale  ("Erosion lobe size (m)",   Range(1,40))    = 14
        _ShellDepth    ("Erosion shell depth (m)", Range(5,250))   = 70

        [Header(Interior)]
        _CoreColor     ("Interior glow",           Color)          = (0.58,0.60,0.64,1)
        _CoreVariation ("Interior variation",      Range(0,1))     = 1.0
        _CoreVarScale  ("Interior variation (m)",  Range(5,400))   = 70
        _CoreVarDepth  ("Interior fade-in depth (m)",Range(20,400))= 200

        [Header(Lighting)]
        _Albedo      ("Albedo",            Color) = (1,1,1,1)
        _SkyColor    ("Ambient sky (top)", Color) = (0.45,0.58,0.80,1)
        _GroundColor ("Ambient ground",    Color) = (0.16,0.19,0.26,1)
        _Absorption  ("Sun absorption",    Range(0.05,4)) = 1.75
        _LightSteps  ("Sun march steps",   Range(2,10))   = 5
        _LightStep   ("Sun step (m)",      Range(1,60))   = 4
        _LightGrowth ("Sun step growth",   Range(1,2.5))  = 1.45
        _LightOctaves("Sun march octaves", Range(1,5))    = 2
        _MsAtten     ("Multiscatter falloff",  Range(0.1,0.95)) = 0.65
        _MsContrib   ("Multiscatter strength", Range(0.1,0.95)) = 0.45
        _PhaseFwd    ("Forward scatter (silver)", Range(0,0.95)) = 0.8
        _PhaseBack   ("Back scatter",             Range(0,0.9))  = 0.25
        _PhaseBlend  ("Phase blend",              Range(0,1))    = 0.5
        _AoDensity   ("Depth darkening (per m)",  Range(0,0.05)) = 0.006
        _AoFloor     ("Depth darkening floor",    Range(0,1))    = 0.35

        [Header(Wind)]
        _WindDir     ("Wind direction",           Vector)         = (1,0.12,0.35,0)
        _WindSpeed   ("Wind speed (m per s)",     Range(0,40))    = 3
        _BoilRatio   ("Fine detail boils faster", Range(1,2.5))   = 1.5
        _TimeOffset  ("Time offset (s)",          Float)          = 0

        [Header(Sorting)]
        // On = clouds occlude each other correctly per pixel. Off = safest with other
        // transparent or sky effects that draw after this one.
        [Enum(Off,0,On,1)] _ZWrite ("Write depth", Float) = 0

        [Header(Raymarch)]
        _StepSize    ("Step size (m)",            Range(0.5,20))  = 5
        _MaxSteps    ("Max steps",                Range(16,320))  = 200
        _StepGrowth  ("Step growth per km",       Range(0,4))     = 0.4
        _LodDistance ("Drop an octave every (m)", Range(200,5000))= 1500
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        // Cull Front: we draw the BACK faces of the bounding box, so the shader keeps running
        // when the camera is inside the box - that is what allows flying into the cloud.
        // ZWrite On + the per-pixel core depth written below lets overlapping cloud boxes
        // occlude each other correctly regardless of transparent sort order. Without it two
        // clouds pop in front of one another as the camera turns.
        Cull Front  ZWrite [_ZWrite]  ZTest LEqual
        Blend One OneMinusSrcAlpha            // premultiplied alpha

        Pass
        {
            Name "CloudStep0"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define MAX_LOBES 8

            // The billow texture stores this many Worley cells across ONE texture repeat, so a
            // LOBE of L metres means sampling with a repeat of L * kCellsPerTile metres. Getting
            // this wrong collapses every octave to the same size -> uniform popcorn.
            #define kCellsPerTile 4.0
            #define kFineCellsPerTile 8.0     // channel A is baked at 8 cells

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            TEXTURE3D(_BillowTex);  SAMPLER(sampler_BillowTex);

            CBUFFER_START(UnityPerMaterial)
                float  _LobeBlend, _MassShrink, _BaseHeight, _Softness, _DensityMul;
                float  _BillowScale, _BillowAspect, _Octaves, _Lacunarity;
                float  _VertSquash, _TopBias, _BaseSoften, _BaseRipple, _BaseBlend;
                float  _Erosion, _ErosionScale, _ShellDepth;
                half4  _CoreColor;
                float  _CoreVariation, _CoreVarScale, _CoreVarDepth;
                half4  _Albedo, _SkyColor, _GroundColor;
                float  _Absorption, _LightSteps, _LightStep, _LightGrowth, _LightOctaves;
                float  _MsAtten, _MsContrib, _PhaseFwd, _PhaseBack, _PhaseBlend;
                float  _AoDensity, _AoFloor;
                float  _StepSize, _MaxSteps, _StepGrowth, _LodDistance;
                float4 _WindDir;
                float  _WindSpeed, _BoilRatio, _TimeOffset, _ZWrite;
            CBUFFER_END

            // Fed per-instance by the CloudMass component (a MaterialPropertyBlock), so one
            // material can drive many differently shaped clouds. Declared outside the CBUFFER
            // because per-instance arrays are not SRP-batcher material data.
            float4 _Lobes[MAX_LOBES];         // xyz = centre, as a fraction of the box
            float4 _LobeRadii[MAX_LOBES];     // xyz = radii,  as a fraction of the box
            float  _LobeCount;

            // Orthonormal rotation applied once per octave, so octaves never line up with each
            // other or with the texture's own grid - which would read as visible tiling.
            static const float3x3 kRot = float3x3( 0.00,  0.80,  0.60,
                                                  -0.80,  0.36, -0.48,
                                                  -0.60, -0.48,  0.64);

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.screenPos  = ComputeScreenPos(p.positionCS);
                return o;
            }

            float IGN(float2 uv)
            {
                float3 m = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(m.z * frac(dot(uv, m.xy)));
            }

            // Inigo Quilez's ellipsoid approximation. Returns an approximate SIGNED DISTANCE in
            // the same units as p and r - negative inside. Everything downstream depends on this
            // being real metres, not a 0..1 "inside-ness".
            float SdEllipsoid(float3 p, float3 r)
            {
                float k0 = length(p / r);
                float k1 = length(p / (r * r));
                return k0 * (k0 - 1.0) / max(k1, 1e-6);
            }

            float SMin(float a, float b, float k)
            {
                float t = saturate(0.5 + 0.5 * (b - a) / max(1e-4, k));
                return lerp(b, a, t) - k * t * (1.0 - t);
            }
            float SMax(float a, float b, float k)
            {
                float t = saturate(0.5 - 0.5 * (a - b) / max(1e-4, k));
                return lerp(a, b, t) + k * t * (1.0 - t);
            }

            // The coarse MASS. Because the union works on real distances, a big lobe and a small
            // lobe blend identically - a normalised "inside-ness" field blends them
            // inconsistently, which is what reads as a pile of separate spheres.
            // No texture reads here, which is what makes empty-space skipping cheap.
            float MassDistance(float3 pM, float3 scaleWS, float shrink)
            {
                int count = (int)_LobeCount;

                if (count <= 0)   // no component attached: fall back to a single ellipsoid
                    return SdEllipsoid(pM, max(scaleWS * 0.5 - shrink, scaleWS * 0.12));

                float d = 1e9;
                [loop]
                for (int i = 0; i < count; i++)
                {
                    float3 c = _Lobes[i].xyz * scaleWS;
                    float3 r = max(_LobeRadii[i].xyz * scaleWS - shrink, scaleWS * 0.03);
                    d = SMin(d, SdEllipsoid(pM - c, r), _LobeBlend);
                }
                return d;
            }

            // How far the surface bulges outward here, IN METRES. Self-similar: amplitude =
            // lobe size * _BillowAspect, and both shrink together each octave.
            float BillowHeight(float3 pM, float3 halfBox, int octaves)
            {
                float hNorm = saturate(pM.y / max(1e-3, halfBox.y) * 0.5 + 0.5);   // 0 base, 1 crown

                // Convection is anisotropic: almost nothing at the flat condensation base,
                // strongest in the growing crown. This is what gives a cumulus its profile.
                float heightW = lerp(0.12, 1.0, smoothstep(0.0, _BaseSoften, hNorm));
                heightW *= lerp(1.0, _TopBias, hNorm);

                float3 q = pM;
                q.y /= max(0.05, _VertSquash);        // >1 = lobes taller than they are wide

                float lobe = _BillowScale;
                float h = 0.0;

                // Motion. The MASS never moves - it is an authored object at a world position.
                // What moves is the billow field sliding through it, which reads as the cloud
                // churning rather than the cloud sliding away. Each finer octave drifts faster,
                // so small lobes boil while the big structure barely changes. Offsetting every
                // octave by the same amount instead would slide the whole cloud like a bar of
                // soap - that is the classic mistake here.
                // _TimeOffset shifts this cloud along its own timeline. Give neighbouring
                // clouds different offsets or a whole group churns in lockstep, which reads
                // instantly as "one shader, copied".
                float3 drift = normalize(_WindDir.xyz + 1e-5) * ((_Time.y + _TimeOffset) * _WindSpeed);

                [loop]
                for (int i = 0; i < octaves; i++)
                {
                    float4 t = SAMPLE_TEXTURE3D_LOD(_BillowTex, sampler_BillowTex,
                                                    (q + drift) / (lobe * kCellsPerTile), 0);
                    h += ((i & 1) ? t.g : t.r) * lobe * _BillowAspect;

                    lobe /= _Lacunarity;
                    q = mul(kRot, q);
                    drift *= _BoilRatio;        // finer octaves churn faster
                }

                return h * heightW;
            }

            // Worst-case bulge over ALL octaves - the bound the empty-space skip relies on.
            float BillowMax()
            {
                float lobe = _BillowScale;
                float h = 0.0;
                int octaves = (int)_Octaves;
                [loop]
                for (int i = 0; i < octaves; i++)
                {
                    h += lobe * _BillowAspect;
                    lobe /= _Lacunarity;
                }
                return h * max(1.0, _TopBias);
            }

            // Noise-free field: mass + flat base. Empty air is sphere-traced on this, so skipping
            // costs zero texture reads.
            float BaseDistance(float3 pM, float3 scaleWS, float shrink, float baseY)
            {
                return max(MassDistance(pM, scaleWS, shrink), baseY - pM.y);
            }

            // The expensive field, used by the VIEW ray only.
            float CloudDistanceFull(float3 pM, float3 scaleWS, float3 halfBox,
                                    float shrink, float baseY, int octaves)
            {
                float d = MassDistance(pM, scaleWS, shrink) - BillowHeight(pM, halfBox, octaves);

                // Fine erosion within a thin shell: 1 at the surface, 0 by _ShellDepth deep. The
                // core is left untouched, which is what keeps the interior solid.
                if (_Erosion > 0.0)
                {
                    float shell = saturate(1.0 + d / max(1.0, _ShellDepth));
                    if (shell > 0.001)
                    {
                        float3 fineDrift = normalize(_WindDir.xyz + 1e-5) * ((_Time.y + _TimeOffset) * _WindSpeed * 2.5);
                        float fine = SAMPLE_TEXTURE3D_LOD(_BillowTex, sampler_BillowTex,
                                        (pM + fineDrift) / (_ErosionScale * kFineCellsPerTile), 0).a;
                        d += (1.0 - fine) * _Erosion * shell * shell;
                    }
                }

                // The condensation level is flat but RAGGED. A hard max() slices a razor edge
                // that reads as a sticker rather than a cloud.
                float ripple = SAMPLE_TEXTURE3D_LOD(_BillowTex, sampler_BillowTex,
                                   pM / (_BillowScale * kCellsPerTile * 2.0), 0).b;
                float plane = (baseY + (ripple - 0.5) * _BaseRipple) - pM.y;
                return SMax(d, plane, _BaseBlend);
            }

            // The cheap field, used by the SUN march - which runs once per lit view step and so
            // dominates the whole shader. Few octaves, no erosion, no rippled base. Shadows are
            // low-frequency; the difference is invisible and the saving is large.
            float CloudDistanceCheap(float3 pM, float3 scaleWS, float3 halfBox,
                                     float shrink, float baseY, int octaves)
            {
                float d = MassDistance(pM, scaleWS, shrink) - BillowHeight(pM, halfBox, octaves);
                return SMax(d, baseY - pM.y, _BaseBlend);
            }

            // Shaping the surface is not enough: inside, density saturates to a constant. Fade a
            // mid-frequency modulation in with depth so the interior gains thin patches while the
            // silhouette stays exactly as authored.
            float ApplyCoreVariation(float density, float3 pM, float d)
            {
                if (_CoreVariation <= 0.0) return density;

                float deep = saturate(-d / max(1.0, _CoreVarDepth));
                if (deep <= 0.001) return density;

                float v = SAMPLE_TEXTURE3D_LOD(_BillowTex, sampler_BillowTex,
                              pM / (_CoreVarScale * kCellsPerTile), 0).g;
                return density * lerp(1.0, saturate(v * 2.4 - 0.25), _CoreVariation * deep);
            }

            float HenyeyGreenstein(float cosA, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (12.5663706 * pow(max(1e-3, 1.0 + g2 - 2.0 * g * cosA), 1.5));
            }

            struct FragOut { half4 color : SV_Target; float depth : SV_Depth; };

            FragOut frag (Varyings i)
            {
                FragOut o;
                #if UNITY_REVERSED_Z
                    o.depth = 0.0;      // default = far plane, so clear pixels never occlude
                #else
                    o.depth = 1.0;
                #endif

                float2 uv = i.screenPos.xy / i.screenPos.w;
                #if UNITY_REVERSED_Z
                    float rawDepth = SampleSceneDepth(uv);
                #else
                    float rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif

                float3 scenePosWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 camWS = GetCameraPositionWS();
                float3 rayWS = normalize(i.positionWS - camWS);
                float rayEnd = min(distance(camWS, i.positionWS), distance(camWS, scenePosWS));

                // --- the cloud's own frame, in METRES ---------------------------------------
                float4x4 o2w = GetObjectToWorldMatrix();
                float3 centerWS = float3(o2w._m03, o2w._m13, o2w._m23);
                float3 scaleWS  = float3(length(o2w._m00_m10_m20),
                                         length(o2w._m01_m11_m21),
                                         length(o2w._m02_m12_m22));
                float3 halfBox = scaleWS * 0.5;
                float3x3 w2o = (float3x3)GetWorldToObjectMatrix();

                float hMax   = BillowMax();
                // Shrink the seed so the billow grows the mass back to roughly the authored size.
                // Billow only reaches its MAXIMUM in rare spots, so shrinking by the full worst
                // case leaves a cloud far thinner than intended.
                float shrink = hMax * _MassShrink;
                float baseY  = lerp(-halfBox.y, halfBox.y, _BaseHeight);

                // near intersection with the object-space unit box
                float3 roOS = TransformWorldToObject(camWS);
                float3 rdOS = mul(w2o, rayWS);
                float3 inv = 1.0 / rdOS;
                float3 t0 = (-0.5 - roOS) * inv, t1 = (0.5 - roOS) * inv;
                float3 tmin = min(t0, t1);
                float tNear = max(0.0, max(max(tmin.x, tmin.y), tmin.z));
                if (tNear >= rayEnd) { o.color = 0; return o; }

                // Distance to where this ray enters the cloud. Constant along the ray, so LOD
                // cannot produce shells of detail that swim as the camera moves, and it varies
                // correctly across the screen - which is what a stretched cloud wall needs.
                float viewDist = max(tNear, 1.0);
                int viewOctaves  = (int)clamp(_Octaves - floor(viewDist / max(200.0, _LodDistance)), 2.0, 6.0);
                int lightOctaves = (int)min(_LightOctaves, (float)viewOctaves);
                // The step must also guarantee the ray can CROSS the whole box within the step
                // budget. A fixed 5 m step with 200 steps reaches 1000 m - on a 1700 m cloud the
                // ray simply runs out and the far half is sliced off in a hard flat cut.
                float segmentLength = rayEnd - tNear;
                float step = max(_StepSize * (1.0 + viewDist * 0.001 * _StepGrowth),
                                 segmentLength / max(1.0, _MaxSteps));

                Light sun = GetMainLight();
                float3 lDirWS = normalize(sun.direction);
                float3 lDirLocal = normalize(mul(w2o, lDirWS) * scaleWS);

                float cosA = dot(rayWS, lDirWS);
                float phase = lerp(HenyeyGreenstein(cosA, _PhaseFwd),
                                   HenyeyGreenstein(cosA, -_PhaseBack), _PhaseBlend) * 12.5663706;

                float t = tNear + IGN(i.positionCS.xy) * step;
                float transmittance = 1.0;
                float3 color = 0;
                int maxSteps = (int)_MaxSteps;
                int lightSteps = (int)_LightSteps;
                float hitT = -1.0;          // where the cloud turns solid, for the depth write

                [loop]
                for (int s = 0; s < maxSteps; s++)
                {
                    if (t >= rayEnd || transmittance < 0.01) break;

                    float3 pWS = camWS + rayWS * t;
                    float3 pM  = mul(w2o, pWS - centerWS) * scaleWS;

                    // Sphere trace the empty air on the noise-free field. The surface can never
                    // sit further out than hMax, so this skip is safe and costs no texture reads.
                    float dBase = BaseDistance(pM, scaleWS, shrink, baseY);
                    if (dBase > hMax)
                    {
                        t += max((dBase - hMax) * 0.85, step);
                        continue;
                    }

                    float d = CloudDistanceFull(pM, scaleWS, halfBox, shrink, baseY, viewOctaves);
                    float density = ApplyCoreVariation(saturate(-d / _Softness), pM, d);
                    if (density <= 0.001) { t += step; continue; }

                    // --- sun march on the CHEAP field: growing steps, fine near, coarse far ---
                    float opticalDepth = 0.0;
                    float3 lp = pM;
                    float ls = _LightStep;
                    [loop]
                    for (int j = 0; j < lightSteps; j++)
                    {
                        lp += lDirLocal * ls;
                        float ld = CloudDistanceCheap(lp, scaleWS, halfBox, shrink, baseY, lightOctaves);
                        opticalDepth += saturate(-ld / _Softness) * ls;
                        ls *= _LightGrowth;
                    }
                    opticalDepth *= _Absorption * _DensityMul;

                    // Multiple scattering, octave approximation: each octave is a dimmer but far
                    // less absorbed copy of the light. This is what puts readable structure into
                    // the shadow side instead of one flat grey mass.
                    float scatter = 0.0;
                    float atten = 1.0, contrib = 1.0;
                    [unroll]
                    for (int k = 0; k < 3; k++)
                    {
                        scatter += contrib * exp(-opticalDepth * atten);
                        atten   *= _MsAtten;
                        contrib *= _MsContrib;
                    }

                    // Ambient: sky above, ground bounce below at the surface; deep inside, the
                    // cloud's own diffused glow - milky, not sky blue. 'd' is reused here, so the
                    // expensive field is evaluated exactly once per step.
                    float hNorm = saturate(pM.y / max(1e-3, halfBox.y) * 0.5 + 0.5);
                    float occlusion = lerp(_AoFloor, 1.0, exp(-max(0.0, -d) * _AoDensity));
                    float3 ambient = lerp(_GroundColor.rgb, _SkyColor.rgb, hNorm);
                    ambient = lerp(_CoreColor.rgb, ambient, occlusion);

                    float3 lit = _Albedo.rgb * (sun.color * scatter * phase + ambient);

                    float sigma = density * _DensityMul;
                    float aStep = 1.0 - exp(-sigma * step);
                    color += lit * aStep * transmittance;
                    transmittance *= exp(-sigma * step);

                    // Record only the DENSE core, so wispy edges still blend softly instead of
                    // punching a hard depth hole.
                    if (hitT < 0.0 && transmittance < 0.25) hitT = t;

                    t += step;
                }

                if (hitT >= 0.0)
                {
                    float4 hp = TransformWorldToHClip(camWS + rayWS * hitT);
                    o.depth = hp.z / hp.w;
                }

                o.color = half4(color, saturate(1.0 - transmittance));
                return o;
            }
            ENDHLSL
        }
    }
}
