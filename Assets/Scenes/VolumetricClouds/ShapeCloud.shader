Shader "Custom/HeroMetaballCloud"
{
    Properties
    {
        [Header(Cloud Colors)]
        _LitColor ("Lit Color", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Color", Color) = (0.35, 0.4, 0.5, 1)
        
        [Header(Density Settings)]
        _GlobalDensity ("Global Density", Range(1.0, 100.0)) = 40.0
        _CloudContrast ("Cloud Sharpness", Range(0.5, 5.0)) = 2.0
        _Coverage ("Coverage (Fill)", Range(-1.0, 1.0)) = 0.0

        [Header(Structure Noise)]
        _NoiseTex ("3D Noise (FBM)", 3D) = "white" {}
        _MacroScale ("Macro Noise Scale", Float) = 0.05
        _DetailScale ("Detail Noise Scale", Float) = 0.2
        _DetailErosion ("Detail Erosion", Range(0.0, 1.0)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Cull Front
        ZWrite Off
        ZTest Always 
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "MetaballCloudPass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5 // Нужно для работы с массивами (StructuredBuffer)

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            TEXTURE3D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _LitColor;
                half4 _ShadowColor;
                float _GlobalDensity;
                float _CloudContrast;
                float _Coverage;
                
                float _MacroScale;
                float _DetailScale;
                float _DetailErosion;
            CBUFFER_END

            // ДАННЫЕ ИЗ C#-СКРИПТА
            StructuredBuffer<float4> _Metaballs;
            int _MetaballCount;
            float _BlobBlend;

            Varyings vert (Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.screenPos = ComputeScreenPos(vertexInput.positionCS);
                return output;
            }

            float smax(float a, float b, float k) 
            {
                float h = saturate(0.5 + 0.5 * (a - b) / k);
                return lerp(b, a, h) + k * h * (1.0 - h);
            }

            // Динамическая генерация каркаса на основе массива из C#
            float GetMetaballMask(float3 posOS)
            {
                if (_MetaballCount <= 0) return 0.0;

                float density = -10.0; // Стартуем с отрицательной плотности (пустота)
                
                for (int i = 0; i < _MetaballCount; i++)
                {
                    float4 mb = _Metaballs[i]; // xyz = позиция, w = радиус
                    
                    // Высчитываем плотность для текущей сферы
                    float sphereDens = mb.w - length(posOS - mb.xyz);
                    
                    // Плавно сливаем с общей массой
                    density = smax(density, sphereDens, _BlobBlend);
                }
                
                return saturate(density * 5.0); 
            }

            float GetCloudDensity(float3 posOS, float3 posWS)
            {
                float shapeMask = GetMetaballMask(posOS);
                if (shapeMask <= 0.01) return 0.0;

                float macroNoise = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, posWS * _MacroScale, 0).r;
                
                float baseDensity = macroNoise - (1.0 - shapeMask);
                baseDensity += _Coverage;
                baseDensity = saturate(baseDensity * _CloudContrast);

                if (baseDensity > 0.0)
                {
                    float detailNoise = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, posWS * _DetailScale, 0).r;
                    float edgeErosionMask = 1.0 - baseDensity; 
                    float erosion = (1.0 - detailNoise) * _DetailErosion * edgeErosionMask;
                    baseDensity = saturate(baseDensity - erosion);
                }

                return baseDensity;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                
                #if UNITY_REVERSED_Z
                    float rawDepth = SampleSceneDepth(screenUV);
                #else
                    float rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(screenUV));
                #endif

                float3 scenePosWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
                float3 cameraPosWS = GetCameraPositionWS();
                float3 rayDirWS = normalize(input.positionWS - cameraPosWS);

                float sceneDist = distance(cameraPosWS, scenePosWS);

                float3 rayOriginOS = TransformWorldToObject(cameraPosWS);
                float3 rayDirOS_raw = mul((float3x3)GetWorldToObjectMatrix(), rayDirWS);
                float3 rayDirOS = normalize(rayDirOS_raw);
                
                float scaleWS2OS = length(rayDirOS_raw);
                float sceneDistOS = sceneDist * scaleWS2OS;

                float3 invRayDirOS = 1.0 / rayDirOS;
                float3 t0 = (-0.5 - rayOriginOS) * invRayDirOS;
                float3 t1 = ( 0.5 - rayOriginOS) * invRayDirOS;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                float tNear = max(max(tmin.x, tmin.y), tmin.z);
                float tFar  = min(min(tmax.x, tmax.y), tmax.z);
                
                float rayStartOSDist = max(0.0, tNear);
                float rayEndOSDist = min(tFar, sceneDistOS); 
                float rayLengthOS = rayEndOSDist - rayStartOSDist;
                
                if (tNear > tFar || rayLengthOS <= 0.0) return half4(0,0,0,0);

                int stepCount = 64;
                float stepSizeOS = rayLengthOS / (float)stepCount;
                
                float3 currentPosOS = rayOriginOS + rayDirOS * rayStartOSDist;
                float3 stepDirOS = rayDirOS * stepSizeOS;
                
                float transmittance = 1.0;     
                half3 finalColor = half3(0,0,0); 

                for(int i = 0; i < stepCount; i++)
                {
                    float3 currentPosWS = mul(GetObjectToWorldMatrix(), float4(currentPosOS, 1.0)).xyz;
                    float density = GetCloudDensity(currentPosOS, currentPosWS);
                    
                    if (density > 0.0)
                    {
                        float localDensity = density * _GlobalDensity;
                        float alphaStep = 1.0 - exp(-localDensity * stepSizeOS);
                        
                        float h = saturate(currentPosOS.y + 0.5);
                        half3 stepIllumination = lerp(_ShadowColor.rgb, _LitColor.rgb, h);
                        
                        finalColor += stepIllumination * alphaStep * transmittance;
                        transmittance *= exp(-localDensity * stepSizeOS);
                        
                        if (transmittance < 0.01) break;
                    }
                    currentPosOS += stepDirOS;
                }

                return half4(finalColor, 1.0 - transmittance);
            }
            ENDHLSL
        }
    }
}