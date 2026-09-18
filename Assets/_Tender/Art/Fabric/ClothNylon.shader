Shader "Tender/ClothNylon"
{
    // Sheer nylon - tights, stockings - built from nothing but numbers.
    //
    // Tights are not a texture. They are a knit of one very thin nylon thread, and everything you
    // see - how much skin shows through, the darkening at the edge of a leg, the glossy streaks -
    // follows from three numbers a shop would print on the packet:
    //
    //   Denier      how heavy the yarn is: grams per 9000 m. 8-15 den is sheer, 40 is semi-opaque,
    //               70+ is opaque. From it we get the thread's real diameter (nylon weighs 1.14 g/cm3).
    //   Wales / cm  how fine the knit is: stitch columns per centimetre of the tube as it comes out
    //               of the packet, unstretched (sheer hosiery is about 28; on the leg it opens to 10-25).
    //   Bulk        how fluffy the yarn is. A bare monofilament is 1; textured or Lycra-covered
    //               yarn covers two or three times its own diameter.
    //
    // Coverage - how much of the surface is thread and how much is hole - is not a slider. It falls
    // out of thread diameter times thread length per stitch. Change the denier and the tights get
    // more opaque for the same reason real ones do.
    //
    // Four effects that make it read as tights rather than a tinted glass ball:
    //   1. Edge darkening. At a grazing angle you look through the knit sideways and every thread
    //      hides more of the skin behind it: coverage(angle) = 1 - (1 - c)^(1 / cos angle). This is the
    //      dark rim on a leg in sheer tights, and it is free.
    //   2. Stretch. Tights are one size of knit pulled over legs of different sizes. Where the fabric
    //      is stretched further the loops open up and the same thread covers less: a thigh is paler
    //      than a shin in the same pair. The stretch is read straight off the mesh - how many
    //      centimetres of leg one UV unit spans here, against the reference size - so it costs nothing
    //      to author.
    //   3. Anisotropic sheen. Nylon is glossy, and a highlight on a thread stretches ALONG the thread.
    //      Each thread here knows its own direction, so the highlight follows the loops up close
    //      and, once the loops are too small to see, runs down the wales - the streak on a shin.
    //   4. Round threads. A thread is a cylinder, so its normal tilts across its width. Without this
    //      the knit is a flat print.
    //
    // Up close the cells of the knit are drawn; from a metre away, when a thread is under a pixel,
    // the pattern fades to its own average and the tights are smooth. Between the two there is a
    // faint moire shimmer, as there is on a photograph - kept on purpose, and only there.
    //
    // Two ways to use it:
    //   Skin inside OFF - a transparent overlay: the alpha is the coverage, put it on a mesh over a
    //                     skin-coloured one.
    //   Skin inside ON  - one opaque material for one mesh: the shader draws the skin itself and the
    //                     knit over it. This is the one for a character's legs.
    //
    // Wetness comes later, from ClothWet.hlsl: wet nylon goes glossier and slightly MORE sheer
    // (water fills the gaps between fibres and stops them scattering), and beads water.

    Properties
    {
        [Header(Knit)]
        _Color ("Yarn colour", Color) = (0.30, 0.20, 0.16, 1)
        _Denier ("Denier (yarn weight, g per 9 km)", Range(5, 400)) = 15
        _WalesPerCm ("Wales per cm, unstretched", Range(2, 40)) = 28
        _CoursesPerWale ("Courses per wale (stitch height)", Range(0.6, 2)) = 1.3
        _Bulk ("Yarn bulk (1 = bare monofilament)", Range(1, 4)) = 2
        _SizeCm ("Unstretched size along U and V (cm)", Vector) = (22, 70, 0, 0)
        _StretchSheer ("Stretch opens the knit", Range(0, 1)) = 1
        _Grain ("Moire shimmer at mid distance", Range(0, 1)) = 0.4

        [Header(Nylon)]
        _Sheen ("Nylon sheen", Range(0, 3)) = 1
        _Gloss ("Highlight tightness", Range(2, 200)) = 80
        _Wrap ("Light wrap", Range(0, 1)) = 0.25

        [Header(Skin inside)]
        [Toggle(_SKIN_UNDER)] _SkinUnder ("Skin inside (one opaque mesh)", Float) = 0
        _SkinColor ("Skin colour", Color) = (0.80, 0.60, 0.50, 1)
        _SkinGloss ("Skin highlight tightness", Range(2, 100)) = 24

        [Header(Wet)]
        _Wetness ("Wetness (whole garment)", Range(0, 1)) = 0
        [HideInInspector] _WetMap ("Wet spots (from Wettable)", 2D) = "black" {}

        [HideInInspector] _SrcBlend ("Src blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst blend", Float) = 10
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Denier;
            float _WalesPerCm;
            float _CoursesPerWale;
            float _Bulk;
            float4 _SizeCm;
            float _StretchSheer;
            float _Grain;
            float _Sheen;
            float _Gloss;
            float _Wrap;
            float4 _SkinColor;
            float _SkinGloss;
            float _Wetness;
        CBUFFER_END
        #include "ClothWet.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma shader_feature_local _SKIN_UNDER
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // How much the legs of the loops bow, in stitch widths: 0 draws a lattice of straight
            // lines and sharp diamonds, more rounds the cells off.
            #define LOOP_SWING 0.08

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;   // w = bitangent sign
                float2 uv : TEXCOORD3;
                float fogCoord : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.tangentWS = float4(TransformObjectToWorldDir(IN.tangentOS.xyz), IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv = IN.uv;
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            // Everything the knit tells us about one point on the garment.
            struct Knit
            {
                float cover;        // 0 hole .. 1 thread, seen face on
                float2 along;       // direction of the thread here, in UV space
                float across;       // -1..1 across the nearest thread, for its rounded normal
            };

            // Seen face on, the legs of the loops in a stretched jersey knit run diagonally - each
            // loop leans on the one beside it - and what the eye picks out is not columns but the
            // holes between the legs: a lattice of little diamonds, a stitch wide and a stitch tall.
            // So the thread is drawn as two families of lines, one leaning each way, bowed a little
            // so the cells come out rounded rather than sharp.
            Knit KnitAt(float2 loop, float radius, float footprint)
            {
                Knit knit;

                float sum = loop.x + loop.y + LOOP_SWING * sin(TWO_PI * (loop.x - loop.y));
                float diff = loop.x - loop.y + LOOP_SWING * sin(TWO_PI * (loop.x + loop.y));

                // Lines x+y = n and x-y = n are 1/sqrt(2) apart, measured square across them.
                float toRising = (abs(frac(sum) - 0.5)) * 0.7071;
                float toFalling = (abs(frac(diff) - 0.5)) * 0.7071;

                // Anti-aliased edges: a thread's edge is softened by about one pixel.
                float edge = footprint * 0.7;
                float rising = 1.0 - smoothstep(radius - edge, radius + edge, toRising);
                float falling = 1.0 - smoothstep(radius - edge, radius + edge, toFalling);
                knit.cover = 1.0 - (1.0 - rising) * (1.0 - falling);

                bool nearestIsRising = toRising < toFalling;
                knit.along = nearestIsRising ? float2(0.7071, -0.7071) : float2(0.7071, 0.7071);
                float signedOffset = nearestIsRising ? (frac(sum) - 0.5) : (frac(diff) - 0.5);
                knit.across = clamp(signedOffset * 0.7071 / radius, -1.0, 1.0);
                return knit;
            }

            // The same knit seen from far away, where the cells are below pixel size: each family
            // of lines covers its width over its spacing, and two families cover 1 - (1-a)^2.
            float MeanCover(float radius)
            {
                float family = saturate(2.0 * radius / 0.7071);
                return 1.0 - (1.0 - family) * (1.0 - family);
            }

            // A highlight on a thread is a tight line ACROSS the thread and a long smear ALONG it: an
            // anisotropic Blinn-Phong lobe (Ashikhmin-Shirley), whose exponent depends on which way the
            // half vector leans. Sixteen times tighter across than along.
            float ThreadSheen(float3 normal, float3 along, float3 across, float3 lightDir, float3 toCamera, float gloss)
            {
                float3 halfDir = normalize(lightDir + toCamera);
                float dotNH = saturate(dot(normal, halfDir));
                float dotAH = dot(along, halfDir);
                float dotXH = dot(across, halfDir);
                float exponent = (gloss * dotXH * dotXH + gloss / 16.0 * dotAH * dotAH) / max(1.0 - dotNH * dotNH, 1e-4);
                return pow(dotNH, exponent);
            }

            // A film of water on skin: a tight, colourless highlight.
            float WaterFilm(float3 normal, float3 lightDir, float3 toCamera)
            {
                float3 halfDir = normalize(lightDir + toCamera);
                return pow(saturate(dot(normal, halfDir)), 160.0) * saturate(dot(normal, lightDir) * 4.0);
            }

            // How stretched the fabric is here, along U and V: centimetres of mesh per UV unit,
            // against the unstretched size. Read from screen derivatives - the mesh tells us.
            float2 Stretch(float3 positionWS, float2 uv)
            {
                float3 dPdx = ddx(positionWS), dPdy = ddy(positionWS);
                float2 dUVdx = ddx(uv), dUVdy = ddy(uv);
                float det = dUVdx.x * dUVdy.y - dUVdy.x * dUVdx.y;
                if (abs(det) < 1e-12) return 1.0;
                float3 dPdu = (dPdx * dUVdy.y - dPdy * dUVdx.y) / det;
                float3 dPdv = (dPdy * dUVdx.x - dPdx * dUVdy.x) / det;
                float2 actualCm = float2(length(dPdu), length(dPdv)) * 100.0;
                return clamp(actualCm / _SizeCm.xy, 0.5, 4.0);
            }

            // Bare skin, for the one-mesh mode: soft wrapped light that goes warm in the wrap (a hint
            // of the blood under it) and a broad, weak highlight.
            float3 SkinLight(float3 normal, float3 lightDir, float3 toCamera, float3 light)
            {
                float dotNL = dot(normal, lightDir);
                float diffuse = saturate((dotNL + 0.5) / 1.5);
                float3 warm = lerp(float3(1.0, 0.45, 0.35), float3(1, 1, 1), saturate(dotNL * 2.0 + 0.5));
                float3 halfDir = normalize(lightDir + toCamera);
                float spec = pow(saturate(dot(normal, halfDir)), _SkinGloss) * 0.12 * saturate(dotNL * 4.0);
                return light * (_SkinColor.rgb * diffuse * warm + spec);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 tangentWS = normalize(IN.tangentWS.xyz);
                float3 bitangentWS = cross(normalWS, tangentWS) * IN.tangentWS.w;
                float3 toCamera = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // --- The knit, in stitches ------------------------------------------------------
                float2 cm = IN.uv * _SizeCm.xy;
                float2 loop = cm * float2(_WalesPerCm, _WalesPerCm * _CoursesPerWale);

                // Pulled over a bigger leg the loops open: fewer wales per real centimetre here,
                // so the same thread is thinner in loop units and covers less. Only the stretch
                // AROUND the leg matters: the strands run along the wales, so spreading the rows
                // apart lengthwise only straightens them, it does not thin them out.
                float2 stretch = lerp(1.0, Stretch(IN.positionWS, IN.uv), _StretchSheer);
                float walesPerCmHere = _WalesPerCm / stretch.x;

                // A nylon monofilament's diameter from its denier: mass per length over density.
                // 15 den comes out at 0.043 mm, which is what a micrometer says.
                float yarnMm = 0.0111 * sqrt(_Denier) * _Bulk;
                float radius = 0.5 * yarnMm * walesPerCmHere / 10.0;    // in wale widths (a wale is 10/wales mm)

                float footprint = max(fwidth(loop.x), fwidth(loop.y));  // stitches per pixel
                Knit knit = KnitAt(loop, radius, footprint);

                // Once a THREAD is thinner than a pixel the drawn pattern gives way to its average:
                // the coverage to the mean, the thread normal to the surface, the thread direction
                // to the wale. A 15 den thread is 0.04 mm, so on a 1080p screen that happens about
                // 10 cm from the leg; from a metre away tights are smooth.
                //
                // In between there is a band where the cells are a pixel or two across - too small
                // to draw honestly, but a camera at that distance shows a faint shimmer of moire,
                // and so does the eye. A little of the drawn pattern is kept there (_Grain says how
                // much) precisely because it aliases; it fades out completely once a cell is under a
                // pixel, so nothing crawls at a distance.
                float far = smoothstep(1.0, 2.5, footprint / (2.0 * radius));   // pattern kept until a thread is under half a pixel
                float shimmer = _Grain * 0.5 * smoothstep(1.5, 0.6, footprint);
                float mean = MeanCover(radius);
                float cover = lerp(knit.cover, mean, far * (1.0 - shimmer));
                float2 alongUV = normalize(lerp(knit.along, float2(0.0, 1.0), far));
                float across = knit.across * (1.0 - far);

                // --- Wet ---------------------------------------------------------------------------
                // Nylon does not soak. Water sits on and between the threads and does three things:
                // the threads stop scattering light and go a little darker and much glossier, and
                // the wet knit clings and shows more skin. (A knit would go dark and matt instead.)
                float wet = WetAt(IN.uv, 2.0);
                float3 yarn = _Color.rgb * (1.0 - 0.5 * wet);
                float sheenStrength = _Sheen * (1.0 + 3.0 * wet);
                float gloss = _Gloss * (1.0 + wet);
                cover *= 1.0 - 0.15 * wet;

                // --- Edge darkening --------------------------------------------------------------
                // Looking through the knit at a slant, every thread hides more of what is behind it.
                float cosView = max(dot(normalWS, toCamera), 0.08);
                cover = 1.0 - pow(saturate(1.0 - cover), 1.0 / cosView);

                // --- Thread shape ----------------------------------------------------------------
                float3 alongWS = normalize(tangentWS * alongUV.x + bitangentWS * alongUV.y);
                float3 acrossWS = cross(normalWS, alongWS);
                float3 threadNormal = normalize(normalWS + acrossWS * across * 0.8);

                // --- Light -----------------------------------------------------------------------
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light sun = GetMainLight(shadowCoord, IN.positionWS, half4(1, 1, 1, 1));
                float3 sunLight = sun.color * sun.shadowAttenuation;   // no distanceAttenuation: see GrassLook.hlsl

                // A thread is thin enough that light wraps round it, like a blade of grass.
                float diffuse = saturate((dot(threadNormal, sun.direction) + _Wrap) / (1.0 + _Wrap));
                float3 lit = sunLight * diffuse;
                // The glint is scaled by the same wrapped term, or threads on the night side of a
                // leg would still flash.
                float3 sheen = sunLight * diffuse * ThreadSheen(threadNormal, alongWS, acrossWS, sun.direction, toCamera, gloss);
                float3 skin = 0.0;
            #if _SKIN_UNDER
                skin = SkinLight(normalWS, sun.direction, toCamera, sunLight);
            #endif
                // The film of water over everything, knit and skin alike: a tight colourless glint.
                float3 film = sunLight * WaterFilm(normalWS, sun.direction, toCamera);

            #if USE_CLUSTER_LIGHT_LOOP
                InputData inputData = (InputData)0;                   // LIGHT_LOOP_BEGIN reads these two by name
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.positionWS = IN.positionWS;
                uint lampCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lampCount)
                    Light lamp = GetAdditionalLight(lightIndex, IN.positionWS, half4(1, 1, 1, 1));
                    float3 lampLight = lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation);
                    float lampDiffuse = saturate((dot(threadNormal, lamp.direction) + _Wrap) / (1.0 + _Wrap));
                    lit += lampLight * lampDiffuse;
                    sheen += lampLight * lampDiffuse * ThreadSheen(threadNormal, alongWS, acrossWS, lamp.direction, toCamera, gloss);
                #if _SKIN_UNDER
                    skin += SkinLight(normalWS, lamp.direction, toCamera, lampLight);
                #endif
                    film += lampLight * WaterFilm(normalWS, lamp.direction, toCamera);
                LIGHT_LOOP_END
            #endif

                float3 threads = yarn * (lit + SampleSH(threadNormal)) + sheen * sheenStrength;

            #if _SKIN_UNDER
                // The skin under the knit, in the shade of the threads over it, then the threads on top.
                // Wet skin is darker too: the water film lets less light scatter back out.
                skin += _SkinColor.rgb * SampleSH(normalWS);
                skin *= (1.0 - 0.35 * cover) * (1.0 - 0.5 * wet);
                float3 colour = lerp(skin, threads, cover) + film * 0.8 * wet;
                float alpha = 1.0;
            #else
                float3 colour = threads + film * 0.8 * wet;
                float alpha = max(cover, 0.8 * wet * WaterFilm(normalWS, sun.direction, toCamera));
            #endif

                colour = MixFog(colour, IN.fogCoord);
                return half4(colour, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            float4 vert(float3 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            float4 vert(float3 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    

        Pass
        {
            // Without this pass the object is missing from the depth texture (the renderer builds it
            // with a DepthNormals prepass because SSAO wants normals) and the volumetric haze paints
            // the sky and far hills over it as if it were glass.
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings vert(float3 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(positionOS);
                OUT.normalWS = TransformObjectToWorldNormal(normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

}
