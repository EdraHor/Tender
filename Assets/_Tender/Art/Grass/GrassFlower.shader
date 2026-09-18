Shader "Tender/GrassFlower"
{
    // Everything that sits on top of a stem as a separate little shape: the pale seed heads that
    // make a meadow read as a meadow, and flowers.
    //
    // Each one is placed on the TIP of a blade, so it has to bend exactly as that blade bends. It
    // does not recompute the bend: it calls the same GrassLean and GrassBend from GrassCommon.hlsl
    // that the blade itself used, reading the same instance out of the same buffer. Anything less
    // and the heads drift off their stems the moment the wind picks up.
    //
    // One mesh serves both: a disc with a scalloped rim (GrassBladeMesh.BuildFlower). A seed head
    // turns it to the camera and colours it all one colour, where the scallops read as fluff. A
    // bloom lays it facing the sky and colours the middle differently, where the scallops read as
    // petals. Colour, size and which of the two it is come from the blade's GrassType.
    //
    // Geometry and not a texture, for the same reason as the seed heads always were: a small
    // alpha-tested card shimmers as the camera moves, and there are thousands of them.

    Properties
    {
        _ShadeAmount ("How dark the side away from the sun is", Range(0, 1)) = 0.5
        _SizeVariation ("Size variation", Range(0, 1)) = 0.45
        _Sink ("How far the head sits below the very tip (m)", Float) = 0.015
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+1" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "GrassCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _ShadeAmount;
            float _SizeVariation;
            float _Sink;
        CBUFFER_END

        // GrassInstance itself is declared in GrassTypes.hlsl, shared with the compute that fills it.
        StructuredBuffer<GrassInstance> _Instances;

        /// Where this blade's tip has ended up at `time`, wind and all.
        float3 FlowerAnchorAt(GrassInstance blade, float time, out float size)
        {
            // The same resting splay GrassBlade.shader gives the blade, or the head floats off
            // the tip of every blade that leans out of its clump.
            float2 facing = float2(sin(blade.yaw), cos(blade.yaw));

            GrassType type = _GrassTypes[(int)blade.type];

            float2 direction;
            float lean = GrassLeanAt(blade.position, facing * blade.splay, type.windResponse, time, direction);

            float3 normal = float3(0, 0, 1);   // GrassBend wants somewhere to put the turned normal
            float3 tipLocal = float3(0, blade.height - _Sink, 0);

            size = type.headSize * (1.0 - _SizeVariation * GrassHash(blade.position.xz + 3.9));

            // Shrink away over the last stretch before the head stops being drawn - by the same
            // per-blade distance the compute uses to stop drawing it - while GrassBlade.shader paints
            // the tip in over the same stretch. No head ever blinks out.
            size *= 1.0 - smoothstep(_GrassFlowerDistance * 0.8, _GrassFlowerDistance,
                                     GrassLodDistance(blade.position, _WorldSpaceCameraPos));
            return blade.position + GrassBend(tipLocal, blade.height, direction, lean, normal);
        }

        /// Where one vertex of the head mesh goes at `time`, and which way the head faces.
        float3 FlowerVertexAt(GrassInstance blade, float3 positionOS, float time, out float3 normalWS)
        {
            float size;
            float3 anchor = FlowerAnchorAt(blade, time, size);
            GrassType type = _GrassTypes[(int)blade.type];

            float3 right, up;
            if ((int)type.headStyle == GRASS_HEAD_BLOOM)
            {
                // A flower faces the sky - and a little towards whoever is looking, because flat up
                // it is a thin sliver from eye height. Each one is spun to its own angle so the
                // petals of neighbouring flowers do not line up.
                float3 toCamera = normalize(_WorldSpaceCameraPos - anchor);
                normalWS = normalize(lerp(float3(0, 1, 0), toCamera, 0.45));
                float spin = GrassHash(blade.position.xz + 8.3) * 6.2831853;
                right = normalize(cross(normalWS, float3(cos(spin), 0.0, sin(spin))));
                up = cross(right, normalWS);
            }
            else
            {
                // A seed head has no silhouette of its own worth keeping: turn it to the camera.
                right = UNITY_MATRIX_V[0].xyz;   // rows of the view matrix are the camera axes
                up = UNITY_MATRIX_V[1].xyz;
                normalWS = UNITY_MATRIX_V[2].xyz;
            }

            return anchor + (right * positionOS.x + up * positionOS.y) * size;
        }

        float3 FlowerVertex(GrassInstance blade, float3 positionOS, out float3 normalWS)
        {
            return FlowerVertexAt(blade, positionOS, _Time.y, normalWS);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

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
            #include "GrassLook.hlsl"       // for GrassLamps: heads glow in lantern light like the blades

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogCoord : TEXCOORD2;
                nointerpolation float3 color : TEXCOORD3;
                nointerpolation float4 center : TEXCOORD4;      // rgb = centre colour, a = 1 for a bloom
            };

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                GrassInstance blade = _Instances[instanceID];
                GrassType type = _GrassTypes[(int)blade.type];
                OUT.color = type.headColor.rgb;
                OUT.center = float4(type.centerColor.rgb, (int)type.headStyle == GRASS_HEAD_BLOOM ? 1.0 : 0.0);

                float3 normalWS;
                OUT.positionWS = FlowerVertex(blade, IN.positionOS, normalWS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light main = GetMainLight(shadowCoord, IN.positionWS, half4(1, 1, 1, 1));

                // A head is shaded by where on it the pixel sits rather than by any surface normal:
                // a ball of fluff takes light in from every side, and a bloom's petals are thin
                // enough to glow through. Darker towards the middle is what keeps either from
                // reading as a flat sticker.
                float2 fromCentre = IN.uv * 2.0 - 1.0;
                float radius = length(fromCentre);
                float dome = sqrt(saturate(1.0 - radius * radius));

                float3 colour = IN.color;
                float shade = 0.35 + 0.65 * dome;
                if (IN.center.a > 0.5)
                {
                    // Petals brighten towards their tips; the middle is its own colour.
                    colour = lerp(IN.center.rgb, IN.color, smoothstep(0.22, 0.3, radius));
                    shade = lerp(0.8, 1.0, radius);
                }

                float3 lit = colour * lerp(1.0 - _ShadeAmount, 1.0, shade);
                float3 lighting = main.color * (0.45 + 0.55 * main.shadowAttenuation);

                float3 lamps = GrassLamps(IN.positionWS, float3(0, 1, 0), GetNormalizedScreenSpaceUV(IN.positionCS), 1.0);
                colour = lit * (lighting + SampleSH(float3(0, 1, 0)) + lamps);
                colour = MixFog(colour, IN.fogCoord);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            // Motion for temporal anti-aliasing - see the same pass in GrassBlade.shader. Without it
            // TAA drags each head back to where its blade used to be, and it slides off the tip.
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            Cull Off
            ColorMask RG

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionNow : TEXCOORD0;
                float4 positionBefore : TEXCOORD1;
            };

            Varyings vert(float3 positionOS : POSITION, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                GrassInstance blade = _Instances[instanceID];
                float3 normalWS;
                float3 now = FlowerVertexAt(blade, positionOS, _Time.y, normalWS);
                float3 before = FlowerVertexAt(blade, positionOS, _LastTimeParameters.x, normalWS);

                OUT.positionCS = TransformWorldToHClip(now);
                OUT.positionNow = mul(_NonJitteredViewProjMatrix, float4(now, 1.0));
                OUT.positionBefore = mul(_PrevViewProjMatrix, float4(before, 1.0));
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                return float4(CalcNdcMotionVectorFromCsPositions(IN.positionNow, IN.positionBefore), 0, 0);
            }
            ENDHLSL
        }

        Pass
        {
            // Heads have to appear in the depth-normals buffer as well as the depth one, or every
            // effect built on it - SSAO first among them - sees straight through the flowers to
            // whatever is behind, and each head gets a halo of occlusion that belongs to the ground.
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                GrassInstance blade = _Instances[instanceID];
                OUT.positionCS = TransformWorldToHClip(FlowerVertex(blade, IN.positionOS, OUT.normalWS));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 vert(Attributes IN, uint instanceID : SV_InstanceID) : SV_POSITION
            {
                GrassInstance blade = _Instances[instanceID];
                float3 normalWS;
                return TransformWorldToHClip(FlowerVertex(blade, IN.positionOS, normalWS));
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}
