Shader "Tender/MeadowGround"
{
    // The terrain under the grass - and, past the last blade, the grass itself.
    //
    // No game draws blades to the horizon. What you see on a far hillside in Breath of the Wild or
    // Ghost of Tsushima is the ground, coloured and lit to look like grass seen from that far away.
    // This shader does exactly that, and it does it by calling the SAME functions as the blades
    // (GrassLook.hlsl) with the same numbers (GrassLook.cs) and the same painted map
    // (GrassField.cs). Nothing here is tuned separately, so there is nothing to drift apart.
    //
    // Up close it is the dark ground between the stems. With distance it slides to the carpet
    // colour, over exactly the distances the blades do. Where the grass was painted away it is
    // plain dirt.
    //
    // Assign a material with this shader as the Terrain's material. Draw Instanced must be off:
    // this is a plain mesh shader and does not read the instanced heightmap.

    Properties
    {
        _DirtMap ("Dirt albedo", 2D) = "white" {}
        _DirtTiling ("Dirt tile size (m)", Float) = 5
        _DirtTint ("Dirt tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry-100" "TerrainCompatible" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _DirtMap_ST;
            float _DirtTiling;
            float4 _DirtTint;
        CBUFFER_END
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
            #include "GrassCommon.hlsl"
            #include "GrassLook.hlsl"

            TEXTURE2D(_DirtMap);
            SAMPLER(sampler_DirtMap);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 xz = IN.positionWS.xz;
                float3 normalWS = normalize(IN.normalWS);

                // Everything the compute shader knew when it grew the blades standing here.
                float2 mapUV = (xz - _GrassPaintArea.xy) / _GrassPaintArea.zw;
                float3 paint = SAMPLE_TEXTURE2D(_GrassPaintMap, sampler_GrassPaintMap, mapUV).rgb;
                float region = GrassRegion(xz, _GrassRegionScale) * paint.b;
                float grass = paint.r;

                float distance = length(IN.positionWS - _WorldSpaceCameraPos);
                float carpet = GrassCarpet(distance);
                float footprint = max(fwidth(xz.x), fwidth(xz.y));

                // Between the stems near you, the carpet far away. The carpet is every kind of
                // grass growing here, mixed by their zones, so a flowering clearing or a wheat field
                // keeps its colour long after its blades have stopped being drawn.
                float4 painted = SAMPLE_TEXTURE2D(_GrassTypeMap, sampler_GrassTypeMap, mapUV);
                float3 meadow = _GrassGroundColor.rgb * (1.0 - _GrassRootShade);
                float3 tip = GrassTipAlbedo(xz, region, 0.5, 0, 1.0);
                meadow = GrassUnderstoreyAlbedo(xz, meadow, tip, distance, footprint);
                meadow = GrassUnderTallAlbedo(meadow, GrassTallCover(xz, painted));
                if (carpet > 0.0)
                {
                    float3 far = GrassZoneFar(xz, region, painted);
                    meadow = lerp(meadow, GrassCarpetAlbedo(far), carpet);
                }

                float3 dirt = SAMPLE_TEXTURE2D(_DirtMap, sampler_DirtMap, xz / _DirtTiling).rgb * _DirtTint.rgb;

                // Where the grass has been flattened the ground under it shows worn, and where it has
                // been cleared - under a house, or where one stood - it is bare soil until it regrows.
                float4 touch = GrassTouchAt(xz);
                grass *= 1.0 - max(touch.b * 0.35, touch.a);

                float3 albedo = lerp(dirt, meadow, grass);

                // The carpet catches the same gusts and the same sheen as the blades do, so the
                // waves keep rolling across hills far past the last blade that was drawn.
                float tipness = 0.7 * carpet * grass;
                float gust = GrassGust(xz);

                float3 colour = GrassShade(albedo, normalWS, IN.positionWS, tipness, gust, 1.0, GetNormalizedScreenSpaceUV(IN.positionCS));

                // Standing water in the dips once the ground is soaked (Wet.hlsl): less of it where
                // the grass stands thick, and the rain's rings on it while it rains.
                float water = StandingWater(xz, WorldWetAt(IN.positionWS, normalWS)) * (1.0 - 0.6 * grass);
                if (water > 0.001)
                {
                    Light sun = GetMainLight(TransformWorldToShadowCoord(IN.positionWS), IN.positionWS, half4(1, 1, 1, 1));
                    float3 toCamera = normalize(GetWorldSpaceViewDir(IN.positionWS));
                    colour = PuddleShade(colour, normalWS, IN.positionWS, sun.direction, sun.color * sun.shadowAttenuation, toCamera, water);
                }

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

    Fallback Off
}
