Shader "Tender/ClothKnit"
{
    // A knitted sweater, built from numbers the way the nylon is - but everything about it is the
    // opposite of nylon, which is the point of having both:
    //
    //   Nylon: one thin glossy thread, a flat sheer mesh, repels water and goes shiny.
    //   Knit:  thick fuzzy yarn, a surface with real depth, drinks water and goes dark and flat.
    //
    // The stitch is stockinette - the plain side of a jumper - seen as columns of V's. Each V is
    // the two legs of one loop rising from the row below; where a leg reaches the top corner it
    // hooks through the loop of the next column. The shader draws that as a height field: each
    // leg is a fat cylinder of yarn, the yarn has a twist along it, and the hollows between loops
    // are lower and darker. From the height come the bumps (normal), the depth (parallax) and the
    // shading in the hollows (occlusion).
    //
    // What says "wool" rather than "moulded plastic" is the fuzz: the halo of loose fibres that
    // catches light at the silhouette (asperity scattering) and lets backlight bleed through at
    // the rim. It is also the first thing to die when the sweater gets wet.
    //
    // Wet (ClothWet.hlsl): the yarn soaks, so it darkens hard and deepens in colour, the fuzz
    // mats down, the loops flatten and a broad, weak film gloss appears. Water spreads through
    // this cloth fast and dries slowly - that lives in Wettable, not here.

    Properties
    {
        [Header(Yarn)]
        _Color ("Yarn colour", Color) = (0.45, 0.12, 0.15, 1)
        _FuzzColor ("Fuzz colour", Color) = (0.7, 0.45, 0.45, 1)
        _StitchMm ("Stitch width (mm)", Range(1.5, 10)) = 4
        _StitchAspect ("Row height / stitch width", Range(0.5, 1.2)) = 0.8
        _YarnRadius ("Yarn thickness (stitch widths)", Range(0.15, 0.5)) = 0.3
        _Twist ("Yarn twist", Range(0, 1)) = 0.5
        _SizeCm ("Garment size along U and V (cm)", Vector) = (22, 70, 0, 0)

        [Header(Look)]
        _Depth ("Loop depth", Range(0, 2)) = 1
        _Fuzz ("Fuzz halo", Range(0, 2)) = 0.8
        _Translucency ("Backlight through the rim", Range(0, 2)) = 0.6
        _Wrap ("Light wrap", Range(0, 1)) = 0.4

        [Header(Wet)]
        _Wetness ("Wetness (whole garment)", Range(0, 1)) = 0
        [HideInInspector] _WetMap ("Wet spots (from Wettable)", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _FuzzColor;
            float _StitchMm;
            float _StitchAspect;
            float _YarnRadius;
            float _Twist;
            float4 _SizeCm;
            float _Depth;
            float _Fuzz;
            float _Translucency;
            float _Wrap;
            float _Wetness;
        CBUFFER_END
        #include "ClothWet.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

            // Height of the yarn over one point of the knit, in yarn radii (0 in a hollow, 1 on
            // the crown of a thread), plus a little twist along the thread for the fibre look.
            //
            // In stitch space a cell is one stitch wide (x from -0.5 to 0.5) and one row tall
            // (y from 0 to 1). The two legs of this loop run from the bottom centre to the top
            // corners; the legs of the neighbouring loops run in from the bottom corners to meet
            // them there. Four fat lines, and the highest wins.
            float Knit(float2 stitch, out float twist)
            {
                float x = frac(stitch.x) - 0.5;
                float y = frac(stitch.y);
                float slant = rsqrt(1.25);                      // legs climb 2 rows per stitch width

                // Distance square across each leg: ours (|x| = y/2) and the neighbours' (|x| = 1 - y/2).
                float ours = abs(abs(x) - 0.5 * y) * slant;
                float theirs = abs(abs(x) - (1.0 - 0.5 * y)) * slant;

                // A cylinder's profile across its width. Our legs lie over the neighbours' where
                // they meet at the top corners, so ours ride a touch higher.
                float crownOurs = sqrt(saturate(1.0 - (ours / _YarnRadius) * (ours / _YarnRadius)));
                float crownTheirs = sqrt(saturate(1.0 - (theirs / _YarnRadius) * (theirs / _YarnRadius))) * 0.7;
                bool onOurs = crownOurs >= crownTheirs;
                float height = onOurs ? crownOurs : crownTheirs;

                // Plied yarn: fibres wind round the thread, so along its length the surface ripples.
                float along = onOurs ? y : (1.0 - y);
                float across = onOurs ? (abs(x) - 0.5 * y) : (abs(x) - (1.0 - 0.5 * y));
                twist = 0.5 + 0.5 * sin((along * 2.2 + across * 3.0 + sign(x) * 0.5) * TWO_PI * 2.0);
                return height;
            }

            // What one light adds to the wool, gathered over the sun and every lamp.
            struct Wool
            {
                float3 normal, bumped, toCamera;
                float rim;
                float3 lit, halo, film;
            };

            void Shade(inout Wool wool, float3 lightDir, float3 light)
            {
                // Thick, soft yarn: wrapped diffuse on the bumped normal.
                float diffuse = saturate((dot(wool.bumped, lightDir) + _Wrap) / (1.0 + _Wrap));
                wool.lit += light * diffuse;
                // The fuzz halo: loose fibres at the silhouette lit from anywhere in front, and
                // glowing when the light is behind them.
                float front = saturate(dot(wool.normal, lightDir) * 0.5 + 0.5);
                float behind = pow(saturate(dot(-wool.toCamera, lightDir)), 3.0) * _Translucency;
                wool.halo += light * wool.rim * (front + behind);
                // Wet wool: a broad, weak film of water, nothing like nylon's tight glint.
                float3 halfDir = normalize(lightDir + wool.toCamera);
                wool.film += light * pow(saturate(dot(wool.bumped, halfDir)), 16.0) * 0.15 * saturate(dot(wool.normal, lightDir) * 4.0);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 tangentWS = normalize(IN.tangentWS.xyz);
                float3 bitangentWS = cross(normalWS, tangentWS) * IN.tangentWS.w;
                float3 toCamera = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // --- Wet: the numbers the rest of the shader bends by ---------------------------
                float wet = WetAt(IN.uv, 4.0);
                float depth = _Depth * (1.0 - 0.5 * wet);            // loops mat down
                float fuzz = _Fuzz * (1.0 - 0.9 * wet);              // fibres cling to the yarn

                // --- Where on the knit ------------------------------------------------------------
                float2 cm = IN.uv * _SizeCm.xy;
                float2 stitchSize = float2(_StitchMm, _StitchMm * _StitchAspect) * 0.1;   // mm -> cm
                float2 stitch = cm / stitchSize;

                // Parallax: look INTO the knit. The eye's direction in the tangent frame, and the
                // surface shifted along it by how deep the hollows are.
                float3 viewTS = float3(dot(toCamera, tangentWS), dot(toCamera, bitangentWS), dot(toCamera, normalWS));
                float yarnCm = _YarnRadius * stitchSize.x;             // yarn radius in cm
                float twist;
                float h = Knit(stitch, twist);
                float2 offset = viewTS.xy / max(viewTS.z, 0.3) * (h - 1.0) * yarnCm * depth;   // cm, towards the eye
                stitch = (cm + offset) / stitchSize;
                h = Knit(stitch, twist);

                // Bumps: the height field's slope, measured a little way along each axis, in cm.
                float2 step = stitchSize * 0.04;
                float unused;
                float hx = Knit(stitch + float2(step.x / stitchSize.x, 0.0), unused);
                float hy = Knit(stitch + float2(0.0, step.y / stitchSize.y), unused);
                float rise = yarnCm * depth;                         // cm of height per unit of h
                float2 slope = float2(hx - h, hy - h) * rise / step;
                float3 bumpTS = normalize(float3(-slope.x, -slope.y, 1.0));
                float3 bumped = normalize(tangentWS * bumpTS.x + bitangentWS * bumpTS.y + normalWS * bumpTS.z);

                // Fade the bumps out as the stitches shrink below a few pixels, or they crawl.
                float footprint = max(fwidth(stitch.x), fwidth(stitch.y));
                float far = smoothstep(0.15, 0.4, footprint);
                bumped = normalize(lerp(bumped, normalWS, far));
                float hollow = lerp(h, 0.7, far);                    // the average height, from afar

                // --- Colour -----------------------------------------------------------------------
                // Fibre twist lightens and darkens along the yarn; the hollows between loops sit in
                // their neighbours' shade.
                float3 yarn = _Color.rgb * lerp(0.85, 1.15, twist * (1.0 - far));
                yarn = lerp(yarn, yarn * yarn * 2.0 * 0.7, wet);     // soaked: dark and deep
                float occlusion = lerp(0.45, 1.0, hollow);

                // --- Light ------------------------------------------------------------------------
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light sun = GetMainLight(shadowCoord, IN.positionWS, half4(1, 1, 1, 1));
                float3 sunLight = sun.color * sun.shadowAttenuation;   // no distanceAttenuation: see GrassLook.hlsl

                Wool wool;
                wool.normal = normalWS; wool.bumped = bumped; wool.toCamera = toCamera;
                wool.rim = pow(1.0 - saturate(dot(normalWS, toCamera)), 1.8);
                wool.lit = 0.0; wool.halo = 0.0; wool.film = 0.0;
                Shade(wool, sun.direction, sunLight);

            #if USE_CLUSTER_LIGHT_LOOP
                InputData inputData = (InputData)0;                   // LIGHT_LOOP_BEGIN reads these two by name
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.positionWS = IN.positionWS;
                uint lampCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lampCount)
                    Light lamp = GetAdditionalLight(lightIndex, IN.positionWS, half4(1, 1, 1, 1));
                    Shade(wool, lamp.direction, lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation));
                LIGHT_LOOP_END
            #endif
                float3 lit = wool.lit, halo = wool.halo, film = wool.film;
                float rim = wool.rim;

                float3 ambient = SampleSH(bumped);
                float3 colour = yarn * occlusion * (lit + ambient)
                              + _FuzzColor.rgb * fuzz * (halo + ambient * rim * 0.5)
                              + film * wet;
                colour = MixFog(colour, IN.fogCoord);
                return half4(colour, 1);
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
