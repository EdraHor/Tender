Shader "Tender/RainSplash"
{
    // The splash where a drop hits something that can get wet - a leg, a shoulder. The rain map
    // knows nothing about the sides of things, so these come from the rays Rain.cs throws: every
    // hit is a small crown of water at the point of impact that opens and is gone in a quarter of a
    // second, lit by the light in the air like the drops themselves.

    Properties
    {
        _Exposure ("Brightness per unit of light", Range(0, 2)) = 0.25
        _Size ("Crown size (m)", Range(0.005, 0.1)) = 0.03
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+51" }

        Pass
        {
            Name "Splashes"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
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
            #include "../Lighting/AirLight.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Exposure;
                float _Size;
            CBUFFER_END

            StructuredBuffer<float4> _Hits;   // xyz where a drop hit, w when
            float _HitLife;                   // seconds a splash lasts
            float _Now;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 fade : TEXCOORD1;      // x: life left, yz: across the quad
                float fogCoord : TEXCOORD2;
            };

            Varyings vert(uint vertexId : SV_VertexID, uint index : SV_InstanceID)
            {
                Varyings OUT;
                float4 hit = _Hits[index];
                float age = saturate((_Now - hit.w) / _HitLife);

                uint corner = vertexId % 6;
                float u = (corner == 1 || corner == 2 || corner == 4) ? 1.0 : -1.0;
                float v = (corner == 2 || corner == 4 || corner == 5) ? 1.0 : -1.0;

                // A crown opens fast and thins as it does: wide and faint at the end of its life.
                float size = _Size * lerp(0.3, 1.0, sqrt(age));
                float3 toCamera = normalize(_WorldSpaceCameraPos - hit.xyz);
                float3 right = normalize(cross(float3(0, 1, 0), toCamera));
                float3 up = cross(toCamera, right);
                float3 positionWS = hit.xyz + right * (u * size) + up * (v * size * 0.6 + size * 0.3);

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.fade = float3(1.0 - age, u, v);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // A ring rather than a blob: bright at the rim of the crown, hollow in the middle.
                float radius = length(IN.fade.yz);
                float ring = saturate(1.0 - abs(radius - 0.7) * 3.0);
                float alpha = IN.fade.x * IN.fade.x * ring;   // saturated pieces: see RainDrop.shader on MSAA
                float3 light = LightInTheAir(IN.positionWS, GetNormalizedScreenSpaceUV(IN.positionCS));
                float3 colour = light * _Exposure * alpha;
                colour = MixFogColor(colour, half3(0, 0, 0), IN.fogCoord);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
