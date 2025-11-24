Shader "Custom/SimpleCelShader"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        
        // Cel Shading
        _ShadowSteps ("Shadow Steps", Range(2, 10)) = 3
        _ShadowIntensity ("Shadow Intensity", Range(0, 1)) = 0.5
        
        // Outline
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.01
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        // PASS 1: ОБВОДКА
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            
            Cull Front
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 color : COLOR;  // Vertex Color
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                float outlineMask = input.color.r;
                
                // Проверяем есть ли запеченные smooth normals
                float3 smoothNormal;
                if (length(input.tangentOS.xyz) > 0.1)
                    smoothNormal = normalize(input.tangentOS.xyz);  // Используем запеченные
                else
                    smoothNormal = normalize(input.normalOS);       // Fallback на обычные
                
                float3 positionOS = input.positionOS.xyz + smoothNormal * _OutlineWidth * outlineMask;
                
                output.positionCS = TransformObjectToHClip(positionOS);
                output.positionCS.z -= 0.0001;
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
        
        // PASS 2: CEL SHADING
        Pass
        {
            Name "CelShading"
            Tags { "LightMode"="UniversalForward" }
            
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };
            
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _ShadowSteps;
                float _ShadowIntensity;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Базовая текстура
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                
                // Основной свет
                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 normal = normalize(input.normalWS);
                
                // Диффузное освещение
                float NdotL = dot(normal, lightDir);
                float lightIntensity = saturate(NdotL);
                
                // Cel Shading эффект (ступенчатое затенение)
                lightIntensity = floor(lightIntensity * _ShadowSteps) / _ShadowSteps;
                lightIntensity = lerp(_ShadowIntensity, 1.0, lightIntensity);
                
                // Финальный цвет
                half3 finalColor = baseColor.rgb * lightIntensity * mainLight.color;
                
                return half4(finalColor, baseColor.a);
            }
            ENDHLSL
        }
    }
}