Shader "Tender/ClothUnwrap"
{
    // Draws a mesh flat, laid out by its UVs, and writes the WORLD position of every point into
    // the pixel: a map that answers "where in the world is this bit of the garment?" for every
    // texel of its texture. Wettable.cs renders it once for a still mesh and every frame for a
    // skinned one, and the wet-map compute shader then knows which texels a raindrop landed on.
    //
    // Alpha is 1 where the mesh is and 0 in the gaps between UV islands.

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Unwrap"
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS);
                // The UV IS the screen position here. Flipped where the render target's first row
                // is the top, so that sampling the map with the same UV lands on the same texel.
                float2 clip = IN.uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                OUT.positionCS = float4(clip, 0.5, 1.0);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                return float4(IN.positionWS, 1.0);
            }
            ENDHLSL
        }
    }
}
