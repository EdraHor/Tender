Shader "Custom/VolumetricCloudBox"
{
    Properties
    {
        _CloudColor ("Cloud Lit Color", Color) = (1, 1, 1, 1)
        _AmbientColor ("Cloud Shadow Color", Color) = (0.3, 0.4, 0.5, 1)
        _Density ("Global Density", Range(0.1, 100.0)) = 50.0 // Немного поднял базу
        _StepCount ("Ray Steps", Integer) = 64[Header(Lighting)]
        _LightAbsorption ("Shadow Darkness", Range(0.1, 5.0)) = 1.5
        _SilverLining ("Silver Lining Intensity", Range(0.0, 1.0)) = 0.6

        [Header(Cloud Shape)]
        _NoiseTex ("3D Noise", 3D) = "white" {}
        _Coverage ("Cloud Size (Coverage)", Range(0.0, 3.0)) = 0.6
        _CoreSize ("Solid Core Size", Range(0.0, 0.5)) = 0.4
        _Smoothness ("Cloud Softness", Range(0.01, 1.0)) = 0.2[Header(Noise Settings)]
        _MacroScale ("Macro Noise Scale", Float) = 0.3
        _MicroScale ("Micro Noise Scale", Float) = 1.5
        _MicroStrength ("Micro Detail Strength", Range(0.0, 1.0)) = 0.3

        [Header(Animation)]
        _WindDirection ("Wind Direction & Speed", Vector) = (0.5, 0.0, 0.2, 0)
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
            Name "CloudRaymarchPass"

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
                half4 _AmbientColor;
                float _Density;
                int _StepCount;
                float _LightAbsorption;
                float _SilverLining;
                float _Coverage;
                float _CoreSize;
                float _Smoothness;
                float _MacroScale;
                float _MicroScale;
                float _MicroStrength;
                float4 _WindDirection;
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

            // Мягкая эрозия краев (оставляем из прошлого шага)
            float GetCloudDensity(float3 posWS, float3 posOS)
            {
                // 1. НОРМАЛИЗАЦИЯ ВЫСОТЫ (от 0.0 до 1.0 внутри куба)
                float height = posOS.y + 0.5; 
                
                // 2. РАДИАЛЬНАЯ МАСКА (Делаем ОДИНОЧНЫЙ объект в центре куба)
                // length(posOS.xz) дает круг в плоскости XZ. 
                // Если растянешь куб (например, Scale X=20, Z=10), облако станет овальным.
                // Плавно растворяем его от центра (0.2) к краям куба (0.5), чтобы не резалось о стенки.
                float distXZ = length(posOS.xz);
                float radialMask = 1.0 - smoothstep(0.2, 0.5, distXZ);
                
                // 3. ВЕРТИКАЛЬНЫЙ ПРОФИЛЬ (База и Башни)
                // Снизу (0.0 -> 0.1) быстрый рост, чтобы облако плотно и ровно лежало на земле.
                // Сверху (0.3 -> 1.0) долгий спад. Наверху плотность падает, позволяя шуму формировать "башни".
                float bottomFade = smoothstep(0.0, 0.1, height);
                float topFade = 1.0 - smoothstep(0.3, 1.0, height);
                float heightMask = bottomFade * topFade;
                
                // Скелет нашего одиночного облака (купол с плоским низом)
                float heroSkeleton = radialMask * heightMask;
                
                // 4. ЧИТАЕМ ОСНОВНОЙ ШУМ (Макро-форма)
                float3 uvw = posWS + _WindDirection.xyz * _Time.y;
                float macroNoise = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, uvw * _MacroScale, 0).r;
                
                // 5. ВЫСЕКАЕМ ОБЛАКО
                // Если heroSkeleton равен 1 (внизу), то база густая. 
                // Если heroSkeleton стремится к 0 (наверху), выживают только макушки шума.
                float density = macroNoise - (1.0 - heroSkeleton * _Coverage);
                
                // Умножаем плотность, чтобы внутри туман был ГУСТЫМ (как молоко), а не полупрозрачным
                density *= 2.0;
                
                // 6. МИКРО-ДЕТАЛИЗАЦИЯ (Эрозия краев)
                float microNoise = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, uvw * _MicroScale, 0).r;
                
                // Умная эрозия: мы разрушаем только полупрозрачные края облака. 
                // Плотное ядро внутри (density > 1.0) мы не трогаем, чтобы не делать из него "швейцарский сыр".
                float erosionMask = 1.0 - saturate(density); 
                float erosion = (1.0 - microNoise) * _MicroStrength * erosionMask;
                density -= erosion;

                // Сглаживаем результат для мягкости
                return saturate(density / _Smoothness);
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
                
                // --- ДАННЫЕ О СОЛНЦЕ ---
                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);
                float3 lightColor = mainLight.color;
                
                // --- ПРЕДРАСЧЕТ ДЛЯ ТЕНЕВОГО ЛУЧА (ВЫНЕСЕНО ИЗ ЦИКЛА ДЛЯ ОПТИМИЗАЦИИ) ---
                // Переводим вектор солнца в Object Space и защищаем от деления на ноль
                float3 lightDirOS = mul((float3x3)GetWorldToObjectMatrix(), lightDirWS);
                float3 safeLightDirOS = lightDirOS + (lightDirOS == 0.0 ? 1e-5 : 0.0);
                float3 invLightDirOS = 1.0 / safeLightDirOS;
                // Находим границы куба в направлении солнца
                float3 lightBoundsOS = sign(safeLightDirOS) * 0.5;
                // ------------------------------------------------------------------------
                
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
                        
                        // --- ИСПРАВЛЕННЫЙ ТЕНЕВОЙ ЛУЧ ---
                        // 1. Вычисляем точное расстояние от текущей точки до края куба в сторону Солнца
                        float3 tBox = (lightBoundsOS - currentPosOS) * invLightDirOS;
                        float distToLightEdgeWS = min(min(tBox.x, tBox.y), tBox.z);
                        
                        // 2. Делим это расстояние ровно на 4 шага (теперь шаг не зависит от камеры!)
                        float lightStepSizeWS = distToLightEdgeWS / 4.0;
                        float3 lightStepWS = lightDirWS * lightStepSizeWS;
                        float3 lightStepOS = lightDirOS * lightStepSizeWS;
                        
                        // 3. Начинаем луч чуть впереди, чтобы избежать самозатенения (Self-Shadowing)
                        float3 lightPosWS = currentPosWS + lightStepWS * 0.5;
                        float3 lightPosOS = currentPosOS + lightStepOS * 0.5;
                        
                        float lightTransmittance = 1.0;
                        
                        // Летим к солнцу
                        for(int j = 0; j < 4; j++)
                        {
                            float l_density = GetCloudDensity(lightPosWS, lightPosOS);
                            if(l_density > 0.0) {
                                lightTransmittance *= exp(-l_density * _Density * lightStepSizeWS * _LightAbsorption);
                            }
                            lightPosWS += lightStepWS;
                            lightPosOS += lightStepOS;
                        }
                        // --------------------------------
                        
                        float3 cloudIllumination = lerp(_AmbientColor.rgb, _CloudColor.rgb * lightColor * phase, lightTransmittance);
                        
                        float alphaStep = 1.0 - exp(-frameDensity * stepSize);
                        finalColor += cloudIllumination * alphaStep * transmittance;
                        
                        transmittance *= exp(-frameDensity * stepSize);
                        
                        if(transmittance < 0.01) break;
                    }
                    
                    currentPosWS += rayDirWS * stepSize;
                    currentPosOS += stepDirOS;
                }

                float alpha = 1.0 - transmittance;
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}