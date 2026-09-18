Shader "Tender/GrassWindStreak"
{
    // The thin pale lines GrassWindParticles draws where the wind is blowing hard.
    //
    // A particle trail's colour times a tint, faded towards the edges of the strip so the line has no
    // hard border, added over the scene without writing depth. The particle system decides where the
    // lines are and how visible each one is; this draws them.
    //
    // A streak is lit like the air it flies through: the sun (with its shadows and the clouds' shadow),
    // the sky, and any lamp nearby. It used to be a fixed colour, which is right at noon and wrong at
    // every other time: at night every streak on the meadow glowed as brightly as by day, far brighter
    // than the dark grass around it, and the whole field seemed to be full of them.

    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 0.5)
        _Exposure ("Brightness per unit of light", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Blend SrcAlpha One          // adds light, so a streak brightens the grass behind it and never darkens it
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Exposure;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fogCoord : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            /// All the light arriving at a point in the air, from every direction at once: a streak is a
            /// wisp of dust or seed fluff, lit from all round rather than on one face.
            #include "../Lighting/AirLight.hlsl"

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.color = IN.color * _Color;
                OUT.uv = IN.uv;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // A trail's v runs across the strip: fade both edges. Its u runs along it: fade the
                // head and the tail so the line starts and ends softly.
                float across = 1.0 - abs(IN.uv.y * 2.0 - 1.0);
                float along = smoothstep(0.0, 0.25, IN.uv.x) * smoothstep(1.0, 0.7, IN.uv.x);
                float alpha = IN.color.a * across * across * along;

                float3 light = LightInTheAir(IN.positionWS, GetNormalizedScreenSpaceUV(IN.positionCS));
                float3 colour = IN.color.rgb * light * _Exposure;

                // Added light fades out with distance: into black, not into the fog's colour, which would
                // brighten the fog wherever a far streak happened to be.
                colour = MixFogColor(colour, half3(0, 0, 0), IN.fogCoord);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
