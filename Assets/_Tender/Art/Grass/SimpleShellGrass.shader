Shader "Tender/SimpleShellGrass"
{
    // The whole idea of shell grass, and nothing else.
    //
    // SimpleShellGrass.cs builds a stack of flat squares, one per layer, each at a height from 0
    // (the ground) to 1 (the top). This shader lifts each square to that fraction of the grass
    // height, then asks one question per pixel: "is the blade growing here still this tall?"
    // If not, the pixel is thrown away. Stack enough layers and what is left looks like grass.

    Properties
    {
        _Height ("Grass height (m)", Float) = 0.25
        _Density ("Blades per metre", Float) = 30
        _Thickness ("Blade thickness", Range(0, 1)) = 0.8
        _RootColor ("Root colour", Color) = (0.10, 0.22, 0.05, 1)
        _TipColor ("Tip colour", Color) = (0.45, 0.70, 0.20, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Height;
                float _Density;
                float _Thickness;
                float4 _RootColor;
                float4 _TipColor;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float layer : TEXCOORD1;            // 0 at the ground, 1 at the top
            };

            Varyings vert(float3 positionOS : POSITION)
            {
                Varyings OUT;
                OUT.layer = positionOS.y;           // the mesh keeps each layer's height here
                OUT.positionWS = TransformObjectToWorld(float3(positionOS.x, positionOS.y * _Height, positionOS.z));
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            // A random number from 0 to 1 for each cell. frac() comes first so it stays random
            // far from the world origin, where a big multiply would run out of float precision.
            float Random(float2 cell)
            {
                float3 p = frac(cell.xyx * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Chop the ground into square cells, one blade per cell. World position, not UV,
                // so the blades stay the same size however big the patch is.
                float2 cell = IN.positionWS.xz * _Density;

                // Each blade gets its own random height...
                float bladeHeight = Random(floor(cell));
                if (IN.layer > bladeHeight) discard;

                // ...and gets thinner towards its tip, so it is a blade and not a column.
                float radius = _Thickness * 0.5 * (1.0 - IN.layer / bladeHeight);
                if (distance(frac(cell), float2(0.5, 0.5)) > radius) discard;

                // Dark at the root, bright at the tip: the cheapest depth cue there is.
                float3 colour = lerp(_RootColor.rgb, _TipColor.rgb, IN.layer);

                Light sun = GetMainLight();
                float3 up = float3(0, 1, 0);
                float3 light = sun.color * saturate(dot(up, sun.direction)) + SampleSH(up);
                return half4(colour * light, 1);
            }
            ENDHLSL
        }
    }
}
