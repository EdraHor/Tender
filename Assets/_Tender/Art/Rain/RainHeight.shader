Shader "Tender/RainHeight"
{
    // Writes nothing but height. RainMap.cs draws the world with this, looking straight down, to
    // learn where the rain lands (see RainMap.cs).
    //
    // Pass 0  any renderer: its world Y.
    // Pass 1  the terrain: a flat grid lifted by the height map GrassField baked.
    //
    // The highest thing wins - not by a depth test but by blending with BlendOp Max: every pixel
    // keeps the largest height written to it. No depth buffer, no reversed-Z conventions to get
    // wrong, and the order things are drawn in does not matter.

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        BlendOp Max
        Blend One One
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float height : TEXCOORD0;
        };

        float4 frag(Varyings IN) : SV_Target
        {
            return float4(IN.height, 0, 0, 1);
        }
        ENDHLSL

        Pass
        {
            Name "Objects"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            Varyings vert(float3 positionOS : POSITION)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(positionOS);
                OUT.positionCS = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
                OUT.height = positionWS.y;
                return OUT;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Terrain"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            Texture2D<float> _HeightMap;      // world Y, baked by GrassField
            SamplerState sampler_HeightMap;
            float4 _TerrainOrigin;            // world XZ of the terrain's corner
            float4 _TerrainSize;              // world XZ extent of the terrain
            float4 _HeightMapUVScaleOffset;

            Varyings vert(float3 positionOS : POSITION)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(positionOS);
                float2 local = (positionWS.xz - _TerrainOrigin.xy) / _TerrainSize.xy;
                float2 uv = local * _HeightMapUVScaleOffset.x + _HeightMapUVScaleOffset.y;
                positionWS.y = _HeightMap.SampleLevel(sampler_HeightMap, uv, 0);
                OUT.positionCS = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
                OUT.height = positionWS.y;
                return OUT;
            }
            ENDHLSL
        }
    }
}
