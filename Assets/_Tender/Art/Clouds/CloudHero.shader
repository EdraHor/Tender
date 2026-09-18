Shader "Custom/CloudHero"
{
    // Hero cloud, raymarched. The IDEA (kept deliberately simple/teachable):
    //
    //   density = SOLID BLOB MASS  minus  NOISE
    //
    // A cloud is a pile of spheres ("blobs"). Their smooth-union is a solid, multi-lobe
    // cumulus body. We then SUBTRACT noise from that body so the noise only sculpts the
    // SURFACE into cauliflower — it can never fill the empty box with grain, because
    // outside the blobs there is nothing to carve. This is why earlier versions looked
    // like "noise in a box": they built the whole shape out of a noise threshold.
    //
    // Box = render bounds only. Steps are fixed in METRES so big clouds don't undersample.
    // Lighting is stylized (anime ramp + Beer-Powder self-shadow + silver lining).
    Properties
    {
        [Header(Colors)]
        _LitColor      ("Lit (sun)",      Color) = (1,1,1,1)
        _ShadowColor   ("Self-shadow",    Color) = (0.42,0.5,0.66,1)
        _AmbientTop    ("Ambient top",    Color) = (0.9,0.94,1.0,1)
        _AmbientBottom ("Ambient bottom", Color) = (0.4,0.47,0.6,1)

        [Header(Form (blob mass))]
        _CloudSize   ("Blob size",      Range(0.4,1.8)) = 1.0
        _Blend       ("Lobe blend",     Range(0.02,0.4)) = 0.12
        _Coverage    ("Coverage",       Range(0,1)) = 0.85
        _BottomFade  ("Flat bottom",    Range(0.01,0.5)) = 0.14

        [Header(Noise carving)]
        _NoiseTex    ("Perlin-Worley (RGBA)", 3D) = "white" {}
        _NoiseScale  ("Billow scale",   Float) = 0.04
        _Billow      ("Billow depth",   Range(0,1)) = 0.45
        _NoiseLow    ("Billow remap lo", Range(0,1)) = 0.40
        _NoiseHigh   ("Billow remap hi", Range(0,1)) = 0.85
        _DetailScale ("Detail scale",   Float) = 0.16
        _Erosion     ("Edge erosion",   Range(0,1)) = 0.35
        _ShellWidth  ("Erosion shell",  Range(0.5,6)) = 3.0
        _WarpStrength("Anti-tile warp", Range(0,0.4)) = 0.0
        _WarpScale   ("Warp scale",     Float) = 0.012

        [Header(Detail LOD (fade grain with distance))]
        _LodStart    ("Full detail within (m)", Float) = 220
        _LodRange    ("Detail fades over (m)",  Float) = 600
        _EmptyStep   ("Empty-space skip x", Range(1,5)) = 2.0

        [Header(Animation (subtle life, not translation))]
        _WindSpeed   ("Wind dir/speed", Vector) = (0.5,0.0,0.15,0)
        _BaseDrift   ("Form drift (0=still)", Range(0,1)) = 0.12
        _Evolve      ("Detail boil",    Range(0,3)) = 1.0

        [Header(Raymarch (world metres))]
        _StepSize   ("Step size (m)",  Range(0.05,10)) = 1.0
        _MaxSteps   ("Max steps",      Integer) = 96
        _DensityMul ("Density",        Range(0.1,20)) = 6.0

        [Header(Lighting)]
        _LightSteps    ("Light steps",     Integer) = 6
        _LightStepSize ("Light step (m)",  Range(0.1,10)) = 2.0
        _Absorption    ("Absorption",      Range(0.1,4)) = 1.2
        _PowderStrength("Powder (dark edge)", Range(0,1)) = 0.4
        _Silver        ("Silver lining",   Range(0,3)) = 0.8
        _ToonSteps     ("Toon bands (0=off)", Range(0,6)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        // ZWrite On + a per-pixel core depth (below) lets overlapping cloud boxes occlude each other
        // correctly regardless of transparent sort order (fixes chunks popping in front).
        Cull Front  ZWrite On  ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "CloudHero"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            TEXTURE3D(_NoiseTex);  SAMPLER(sampler_NoiseTex);

            // A cloud is a LIST OF SPHERES ("blobs"), fed in per-instance by the CloudShape
            // component (xyz = centre in object space -0.5..0.5, w = radius). One shader draws
            // any shape: a hero cumulus, a towering cumulonimbus, or a segment of a cloud wall.
            #define MAX_BLOBS 32

            CBUFFER_START(UnityPerMaterial)
                half4 _LitColor, _ShadowColor, _AmbientTop, _AmbientBottom;
                float _CloudSize, _Blend, _Coverage, _BottomFade;
                float _NoiseScale, _Billow, _NoiseLow, _NoiseHigh, _DetailScale, _Erosion, _ShellWidth;
                float _WarpStrength, _WarpScale, _LodStart, _LodRange, _EmptyStep;
                float4 _WindSpeed;
                float _BaseDrift, _Evolve;
                float _StepSize, _DensityMul; int _MaxSteps;
                float _LightStepSize, _Absorption, _PowderStrength, _Silver, _ToonSteps; int _LightSteps;
                float4 _Blobs[MAX_BLOBS];
                float _BlobCount;
            CBUFFER_END

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

            // smooth union of "inside-ness" values (higher = deeper inside the union)
            float SMax(float a, float b, float k)
            {
                float t = saturate(0.5 + 0.5 * (a - b) / max(1e-4, k));
                return lerp(b, a, t) + k * t * (1.0 - t);
            }

            // Density at a point. posOS in [-0.5,0.5]^3 drives the FORM; posWS drives NOISE.
            // 'detail' in [0,1] scales the high-freq erosion: 1 = full grain (near), 0 = smooth (far / LOD).
            // Passing 0 (e.g. in the light march) also SKIPS the detail texture fetch -> cheaper.
            float Density(float3 posWS, float3 posOS, float detail)
            {
                float h = saturate(posOS.y + 0.5);

                // --- 1) FORM: smooth-union of the blob list -> a solid, multi-lobe mass. ---
                int count = (int)_BlobCount;
                float shape = 0.0;
                if (count <= 0)
                {
                    // fallback so the material isn't blank without a CloudShape component
                    shape = 1.0 - length(posOS / max(0.02, 0.45 * _CloudSize));
                }
                else
                {
                    [loop]
                    for (int bi = 0; bi < count; bi++)
                    {
                        float3 c = _Blobs[bi].xyz;
                        float  r = max(0.02, _Blobs[bi].w * _CloudSize);
                        float  fi = 1.0 - length((posOS - c) / r);   // inside-ness of this sphere
                        shape = SMax(shape, fi, _Blend);
                    }
                }
                shape *= smoothstep(0.0, _BottomFade, h);        // soft, slightly flat base
                if (shape <= 0.0) return 0.0;

                // Optional domain warp: offset sample coords by low-freq noise so the texture's
                // repeat isn't axis-aligned -> hides tiling. Free when _WarpStrength = 0.
                float3 wp = posWS;
                if (_WarpStrength > 0.0)
                {
                    float3 w = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, posWS * _WarpScale, 0).gba - 0.5;
                    wp += w * _WarpStrength / max(1e-4, _NoiseScale);
                }

                // --- 2) BILLOWS: SUBTRACT low-freq noise so the surface breaks into cauliflower. ---
                float3 baseAnim = _WindSpeed.xyz * _Time.y * _BaseDrift;           // form barely drifts
                float pw = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, wp * _NoiseScale + baseAnim, 0).r;
                pw = saturate((pw - _NoiseLow) / max(0.01, _NoiseHigh - _NoiseLow)); // stretch to full 0..1
                // billow is shallower with distance (detail<1) -> far clouds read as smooth soft shapes
                float billow = (1.0 - pw) * _Billow * lerp(0.5, 1.0, detail);

                // --- 3) FINE erosion: high-freq Worley nibbles ONLY the outer shell (and fades with LOD). ---
                float erode = 0.0;
                if (detail > 0.001)
                {
                    float3 detailAnim = _WindSpeed.xyz * _Time.y * _Evolve;        // surface boils
                    float4 det = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, wp * _DetailScale + detailAnim, 0);
                    float worley = det.g * 0.625 + det.b * 0.25 + det.a * 0.125;   // fbm-ish 0..1
                    float shell  = saturate(1.0 - shape * _ShellWidth);            // 1 at edge -> 0 in core
                    erode = (1.0 - worley) * _Erosion * shell * detail;
                }

                // density = solid form, carved by billows + edge erosion; coverage is a global threshold.
                float d = shape - billow - erode - (1.0 - _Coverage);
                return saturate(d) * _DensityMul;
            }

            struct FragOut { half4 color : SV_Target; float depth : SV_Depth; };

            FragOut frag (Varyings i)
            {
                FragOut fo;
                #if UNITY_REVERSED_Z
                    fo.depth = 0.0;   // default = far plane (transparent pixels don't occlude)
                #else
                    fo.depth = 1.0;
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

                float sceneDist = distance(camWS, scenePosWS);
                float backDist  = distance(camWS, i.positionWS);
                float rayEnd    = min(backDist, sceneDist);

                // near intersection with the object-space unit box
                float3 roOS = TransformWorldToObject(camWS);
                float3 rdOS = mul((float3x3)GetWorldToObjectMatrix(), rayWS);
                float3 inv  = 1.0 / rdOS;
                float3 t0 = (-0.5 - roOS) * inv, t1 = (0.5 - roOS) * inv;
                float3 tmin = min(t0, t1);
                float tNear = max(0.0, max(max(tmin.x, tmin.y), tmin.z));

                // --- adaptive stepping -----------------------------------------------------------
                // baseStep (fine) is scaled so the whole box is crossed within _MaxSteps -> no missing
                // chunks on big boxes; never finer than _StepSize. Empty air is skipped in coarse strides;
                // when a coarse step first enters density we STEP BACK and refine, so the cloud surface
                // isn't quantized into bands that swim as the camera moves.
                float segLen     = rayEnd - tNear;
                float baseStep   = max(_StepSize, segLen / (float)_MaxSteps);
                float coarseStep = baseStep * _EmptyStep;

                // LOD is per-CLOUD (box-centre distance), CONSTANT along the ray -> the grain doesn't form
                // moving concentric shells the way a per-sample (camera-distance) LOD did.
                float3 objCenterWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float  detail = saturate(1.0 - (distance(camWS, objCenterWS) - _LodStart) / max(1.0, _LodRange));

                float jitter = IGN(i.positionCS.xy) * baseStep;
                float t = tNear + jitter;
                if (t >= rayEnd) { fo.color = 0; return fo; }

                Light sun = GetMainLight();
                float3 lDirWS = normalize(sun.direction);
                float3 lDirOS = mul((float3x3)GetWorldToObjectMatrix(), lDirWS);
                float  cosA   = dot(rayWS, lDirWS);
                float  silver = 1.0 + pow(saturate(cosA), 4.0) * _Silver;   // forward glow

                float transmittance = 1.0;
                float3 color = 0;
                float stride = coarseStep;    // start coarse (most of the ray is empty)
                int emptyRun = 0;
                float hitT = -1.0;            // ray distance where the cloud becomes solid (for depth write)

                [loop]
                for (int s = 0; s < _MaxSteps; s++)
                {
                    if (t >= rayEnd || transmittance < 0.02) break;

                    float3 pWS = camWS + rayWS * t;
                    float3 pOS = roOS + rdOS * t;
                    float dens = Density(pWS, pOS, detail);

                    if (dens > 0.0)
                    {
                        if (stride > baseStep)        // hit density during a coarse skip -> back up, go fine
                        {
                            t -= stride;
                            stride = baseStep;
                            t += stride;
                            continue;
                        }

                        // light march toward the sun -> self-shadowing (no fine detail needed)
                        float od = 0.0;
                        float3 lpWS = pWS, lpOS = pOS;
                        [loop]
                        for (int j = 0; j < _LightSteps; j++)
                        {
                            lpWS += lDirWS * _LightStepSize;
                            lpOS += lDirOS * _LightStepSize;
                            od += Density(lpWS, lpOS, 0.0) * _LightStepSize * _Absorption;
                        }

                        float beer   = exp(-od);
                        float powder = 1.0 - exp(-od * 2.0);
                        float energy = beer * lerp(1.0, powder, _PowderStrength);
                        if (_ToonSteps >= 1.0)
                            energy = floor(energy * _ToonSteps + 0.5) / _ToonSteps;   // banded toon

                        float hGrad = saturate(pOS.y + 0.5);
                        float3 ambient = lerp(_AmbientBottom.rgb, _AmbientTop.rgb, hGrad);
                        float3 sunlit  = _LitColor.rgb * sun.color * silver;
                        float3 lit = lerp(_ShadowColor.rgb * ambient, sunlit, energy);

                        float aStep = 1.0 - exp(-dens * baseStep);
                        color += lit * aStep * transmittance;
                        transmittance *= exp(-dens * baseStep);
                        if (hitT < 0.0 && transmittance < 0.25) hitT = t;  // record only the DENSE core -> soft edges still blend
                        emptyRun = 0;
                        t += baseStep;                       // fine step through the cloud
                    }
                    else
                    {
                        if (stride <= baseStep)              // fine, but empty: only go coarse once clearly out
                        {
                            emptyRun++;
                            if (emptyRun >= 4) stride = coarseStep;
                        }
                        t += stride;
                    }
                }

                float alpha = saturate(1.0 - transmittance);

                // write the cloud's solid-core depth so overlapping cloud boxes occlude per-pixel
                if (hitT >= 0.0)
                {
                    float4 hp = TransformWorldToHClip(camWS + rayWS * hitT);
                    fo.depth = hp.z / hp.w;
                }

                fo.color = half4(color, alpha);
                return fo;
            }
            ENDHLSL
        }
    }
}
