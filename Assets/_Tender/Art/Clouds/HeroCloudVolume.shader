Shader "Custom/HeroCloudVolume"
{
    Properties
    {
        [Header(Color and Lighting)]
        _CloudColor ("Cloud Lit Color", Color) = (1, 1, 1, 1)
        _ShadowColor ("Cloud Shadow Color", Color) = (0.3, 0.4, 0.5, 1)
        _Density ("Global Density", Range(0.1, 100.0)) = 50.0
        _StepCount ("Ray Steps", Integer) = 64
        
        [Header(Advanced Lighting)]
        _LightAbsorption ("Shadow Darkness", Range(0.1, 5.0)) = 2.0
        _MultiScattering ("Light Wrapping (Relief in Shadows)", Range(0.0, 1.0)) = 0.5
        _SilverLining ("Silver Lining Intensity", Range(0.0, 1.0)) = 0.6
        _OpacityBoost ("Opacity Boost (Hide Background)", Range(1.0, 10.0)) = 3.0

        [Header(Cloud Volume Shape)]
        _Coverage ("Cloud Coverage (Fill)", Range(0.0, 1.0)) = 0.5
        _EdgeSoftness ("Edge Softness (Meters)", Range(0.1, 20.0)) = 2.0
        _BottomFade ("Flat Bottom Height", Range(0.01, 0.5)) = 0.05
        _TowerHeight ("Towers Height", Range(0.1, 1.0)) = 0.8

        [Header(Noise and AntiTiling)]
        _NoiseTex ("3D Noise (FBM)", 3D) = "white" {}
        _MacroScale ("Base Cloud Scale", Float) = 0.2
        _AntiTilingStrength ("Anti-Tiling (Chaos Mask)", Range(0.0, 1.0)) = 0.8
        _AntiTilingScale ("Chaos Mask Scale", Float) = 0.35[Header(Detailing)]
        _DetailScale ("Cauliflower Detail Scale", Float) = 0.8
        _DetailErosion ("Detail Erosion (Chew edges)", Range(0.0, 1.0)) = 0.5

        [Header(Animation)]
        _WindSpeed ("Wind Velocity", Vector) = (0.5, 0.0, 0.2, 0)
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
            Name "HeroCloudPass"

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

            TEXTURE3D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _CloudColor;
                half4 _ShadowColor;
                float _Density;
                int _StepCount;
                
                float _LightAbsorption;
                float _MultiScattering;
                float _SilverLining;
                float _OpacityBoost;
                
                float _Coverage;
                float _EdgeSoftness;
                float _BottomFade;
                float _TowerHeight;

                float _MacroScale;
                float _AntiTilingStrength;
                float _AntiTilingScale;

                float _DetailScale;
                float _DetailErosion;
                float4 _WindSpeed;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.screenPos = ComputeScreenPos(vertexInput.positionCS);
                return output;
            }

            float InterleavedGradientNoise(float2 uv)
            {
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(uv, magic.xy)));
            }

            static const float3x3 rot3D = float3x3(
                 0.36,  0.48, -0.80,
                -0.80,  0.60,  0.00,
                 0.48,  0.64,  0.60
            );

            float GetCloudDensity(float3 posWS, float3 posOS)
            {
                float3 scale = float3(
                    length(GetObjectToWorldMatrix()[0].xyz),
                    length(GetObjectToWorldMatrix()[1].xyz),
                    length(GetObjectToWorldMatrix()[2].xyz)
                );
                
                float3 distToWallWS = (0.5 - abs(posOS)) * scale;
                float distToEdge = min(min(distToWallWS.x, distToWallWS.y), distToWallWS.z);
                float edgeMask = smoothstep(0.0, _EdgeSoftness, distToEdge);

                float height = posOS.y + 0.5; 
                float bottomFade = smoothstep(0.0, _BottomFade, height); 
                float topFade = 1.0 - smoothstep(_TowerHeight - 0.2, _TowerHeight + 0.2, height); 
                float profileMask = bottomFade * topFade;

                float skeleton = edgeMask * profileMask;
                if (skeleton <= 0.01) return 0.0; 

                float3 maskUVW = mul(rot3D, posWS) * (_MacroScale * _AntiTilingScale) + _WindSpeed.xyz * _Time.y * 0.2;
                float chaosMask = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, maskUVW, 0).r;
                chaosMask = smoothstep(0.1, 0.9, chaosMask);

                float3 uvw = posWS * _MacroScale + _WindSpeed.xyz * _Time.y;
                float baseNoise = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, uvw, 0).r;
                
                float macroNoise = baseNoise * lerp(1.0, chaosMask, _AntiTilingStrength);

                float density = macroNoise - (1.0 - skeleton * _Coverage);
                density *= 3.0; 

                if (density > 0.0)
                {
                    float3 detailUVW = mul(rot3D, posWS) * _DetailScale + _WindSpeed.xyz * _Time.y * 1.5;
                    float detailNoise = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, detailUVW, 0).r;
                    
                    float erosionMask = 1.0 - saturate(density);
                    float erosion = (1.0 - detailNoise) * _DetailErosion * erosionMask;
                    density -= erosion;
                }

                return saturate(density);
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
                float backfaceDist = distance(cameraPosWS, input.positionWS);
                float rayEndDist = min(backfaceDist, sceneDist);

                float3 rayOriginOS = TransformWorldToObject(cameraPosWS);
                float3 rayDirOS = mul((float3x3)GetWorldToObjectMatrix(), rayDirWS);
                
                float3 invRayDirOS = 1.0 / rayDirOS;
                float3 t0 = (-0.5 - rayOriginOS) * invRayDirOS;
                float3 t1 = ( 0.5 - rayOriginOS) * invRayDirOS;
                float3 tmin = min(t0, t1);
                float tNear = max(max(tmin.x, tmin.y), tmin.z);
                
                float stepSize = (rayEndDist - max(0.0, tNear)) / (float)_StepCount;
                float jitter = InterleavedGradientNoise(input.positionCS.xy);
                
                float rayStartDist = max(0.0, tNear) + jitter * stepSize; 
                float rayLength = rayEndDist - rayStartDist;
                
                if (rayLength <= 0.0) return half4(0, 0, 0, 0);

                float3 currentPosWS = cameraPosWS + rayDirWS * rayStartDist;
                float3 currentPosOS = rayOriginOS + rayDirOS * rayStartDist;
                float3 stepDirOS = rayDirOS * stepSize;
                
                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);
                float3 lightColor = mainLight.color;
                
                float3 lightDirOS = mul((float3x3)GetWorldToObjectMatrix(), lightDirWS);
                float3 safeLightDirOS = lightDirOS + (lightDirOS == 0.0 ? 1e-5 : 0.0);
                float3 invLightDirOS = 1.0 / safeLightDirOS;
                float3 lightBoundsOS = sign(safeLightDirOS) * 0.5;
                
                float cosAngle = dot(rayDirWS, lightDirWS);
                float phase = lerp(1.0, 1.0 + pow(max(0.0, cosAngle), 4.0) * _SilverLining, _SilverLining);

                float transmittance = 1.0; 
                float3 finalColor = float3(0, 0, 0);
                
                for(int i = 0; i < _StepCount; i++)
                {
                    float density = GetCloudDensity(currentPosWS, currentPosOS);
                    
                    if (density > 0.0)
                    {
                        float frameDensity = density * _Density;
                        
                        float3 tBox = (lightBoundsOS - currentPosOS) * invLightDirOS;
                        float distToLightEdgeWS = min(min(tBox.x, tBox.y), tBox.z);
                        
                        float lightStepSizeWS = distToLightEdgeWS / 4.0;
                        float3 lightStepWS = lightDirWS * lightStepSizeWS;
                        float3 lightStepOS = lightDirOS * lightStepSizeWS;
                        
                        float3 lightPosWS = currentPosWS + lightStepWS * 0.5;
                        float3 lightPosOS = currentPosOS + lightStepOS * 0.5;
                        
                        float opticalDepth = 0.0;
                        for(int j = 0; j < 4; j++)
                        {
                            float l_density = GetCloudDensity(lightPosWS, lightPosOS);
                            if(l_density > 0.0) {
                                opticalDepth += l_density * _Density * lightStepSizeWS * _LightAbsorption;
                            }
                            lightPosWS += lightStepWS;
                            lightPosOS += lightStepOS;
                        }
                        
                        // МАГИЯ №1: Multiple Scattering
                        // Прямой свет отсекается жестко, а рассеянный свет проникает в 5 раз глубже!
                        // Это дает объемный рельеф на теневой стороне.
                        float directLight = exp(-opticalDepth);
                        float scatterLight = exp(-opticalDepth * 0.2) * _MultiScattering;
                        float lightTransmittance = saturate(directLight + scatterLight);
                        
                        // МАГИЯ №2: Ambient Gradient
                        // Делаем низ облака темнее (от земли), а верх светлее (от неба)
                        float heightGradient = saturate(currentPosOS.y + 0.5);
                        float3 ambientColor = lerp(_ShadowColor.rgb * 0.4, _ShadowColor.rgb * 1.1, heightGradient);

                        float3 cloudIllumination = lerp(ambientColor, _CloudColor.rgb * lightColor * phase, lightTransmittance);
                        
                        float alphaStep = 1.0 - exp(-frameDensity * stepSize);
                        finalColor += cloudIllumination * alphaStep * transmittance;
                        
                        transmittance *= exp(-frameDensity * stepSize);
                        
                        if(transmittance < 0.01) break;
                    }
                    
                    currentPosWS += rayDirWS * stepSize;
                    currentPosOS += stepDirOS;
                }

                float alpha = saturate(1.0 - transmittance);
                
                // МАГИЯ №3: Фикс просвечивания гор (Opacity Boost)
                // Агрессивно умножаем итоговую непрозрачность, чтобы перекрыть фон.
                // При этом масштабируем накопленный цвет, чтобы не создать черный ореол.
                float boostedAlpha = saturate(alpha * _OpacityBoost);
                if (alpha > 0.001) {
                    finalColor *= (boostedAlpha / alpha);
                }

                return half4(finalColor, boostedAlpha);
            }
            ENDHLSL
        }
    }
}