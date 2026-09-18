Shader "Hidden/Tender/VolumetricLight"
{
    // Light you can see in the air: haze that thickens towards the horizon, shafts of sun where the
    // clouds break, a glow round every lantern at night. Drawn by VolumetricLightFeature, set up by
    // the Atmosphere component in the scene.
    //
    // Every pixel walks its view ray through the air, a few dozen steps, and at each step asks two
    // questions: how thick is the air here, and how much light reaches this point? The light is the
    // sun - through URP's own shadow map and cloud cookie, so it is dark exactly where the ground is
    // in shadow - plus the sky's ambient, plus every point and spot light nearby. What the air sends
    // towards the camera is added up, and so is how much of what lies behind it gets through.
    //
    // Four passes, run in order by the feature:
    //   0 March      at half resolution (the air is soft, the pixels are not needed)
    //   1 Blur       one direction at a time, twice, and never across an edge in depth
    //   2 Composite  back up to full resolution over the frame: colour * transmittance + light

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    // Set by Atmosphere.cs as globals.
    float  _FogDensity;          // how much light a metre of air at the base height takes away
    float  _FogBaseHeight;       // world Y where the haze is thickest
    float  _FogHeightFalloff;    // metres over which it thins to about a third
    float  _FogAnisotropy;       // 0 glows evenly all round, towards 1 only looking into the light
    float3 _FogAmbient;          // sky light the haze scatters, already scaled
    float  _FogSunScatter;
    float  _FogLocalScatter;
    float  _FogMarchDistance;    // metres walked in steps; past it the air is added up in one go
    float  _FogSkyDistance;      // how deep the haze is in front of the sky
    float  _FogSteps;

    // Set by the feature for the blur and composite passes.
    float4 _VolumetricTexel;     // xy = one texel of the half-resolution image, in UV
    float4 _BlurDirection;       // xy = (1, 0) or (0, 1)

    /// Eye depth in metres at a UV of the full-resolution depth buffer.
    float SceneEyeDepth(float2 uv)
    {
        return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "March"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            // Hard shadow compares only: the haze is blurred afterwards anyway, and a soft 9-tap
            // shadow at every one of 32 steps would be most of the cost for nothing you could see.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS

            // No _LIGHT_COOKIES here on purpose: with it, GetMainLight reads the cookie with a plain
            // SAMPLE_TEXTURE2D, which picks its mip from screen derivatives - and derivatives do not
            // exist inside a loop, so the shader will not compile. MainLightCookieAt reads it instead.

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            /// The sun's cookie (the cloud shadows) at a point, read at mip 0.
            float MainLightCookieAt(float3 position)
            {
                if (!IsMainLightCookieEnabled()) return 1.0;
                float2 cookieUV = ComputeLightCookieUVDirectional(_MainLightWorldToLight, position, float4(1, 1, 0, 0), URP_TEXTURE_WRAP_MODE_NONE);
                half4 cookie = SAMPLE_TEXTURE2D_LOD(_MainLightCookieTexture, sampler_MainLightCookieTexture, cookieUV, 0);
                return IsMainLightCookieTextureRGBFormat() ? dot(cookie.rgb, 1.0 / 3.0)
                     : IsMainLightCookieTextureAlphaFormat() ? cookie.a
                     : cookie.r;
            }

            /// How thick the air is at a height: thickest at the base, thinning exponentially above it.
            float DensityAt(float y)
            {
                return _FogDensity * exp(-max(y - _FogBaseHeight, 0.0) / max(_FogHeightFalloff, 1.0));
            }

            /// Air thickness added up along a straight stretch of ray, exactly, with no steps.
            /// Used past the march distance, where there are no shadows or lanterns left to resolve.
            float OpticalDepth(float startY, float directionY, float length)
            {
                float falloff = max(_FogHeightFalloff, 1.0);
                float start = DensityAt(startY);
                float climb = directionY / falloff;
                if (abs(climb) < 1e-5) return start * length;
                return start * (1.0 - exp(-length * climb)) / climb;
            }

            /// Henyey-Greenstein phase, scaled so that air scattering evenly gives 1 in every direction.
            /// cosAngle is between the view ray and the direction TO the light: 1 looking straight at it.
            float Phase(float cosAngle, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / pow(max(1.0 + g2 - 2.0 * g * cosAngle, 1e-4), 1.5);
            }

            /// A fixed, fine, patternless offset per pixel. Starting each pixel's steps at a slightly
            /// different point turns the stair-steps of a coarse march into grain, and the blur that
            /// follows turns grain into smooth haze.
            float InterleavedGradientNoise(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float deviceDepth = SampleSceneDepth(uv);

                float3 eye = GetCameraPositionWS();
                float3 surface = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                float3 toSurface = surface - eye;
                float surfaceDistance = length(toSurface);
                float3 direction = toSurface / max(surfaceDistance, 1e-4);

            #if UNITY_REVERSED_Z
                bool sky = deviceDepth <= 1e-7;
            #else
                bool sky = deviceDepth >= 1.0 - 1e-7;
            #endif
                // The sky has no surface, and the skybox already paints the far atmosphere, so the
                // haze in front of it stops at _FogSkyDistance - about where the farthest hills are,
                // so a hill on the horizon and the sky beside it get the same haze.
                float rayEnd = sky ? _FogSkyDistance : surfaceDistance;
                float marchEnd = min(rayEnd, _FogMarchDistance);

                float3 scattered = 0.0;
                float transmittance = 1.0;

                int steps = max((int)_FogSteps, 1);
                float jitter = InterleavedGradientNoise(input.positionCS.xy);

                for (int i = 0; i < steps; i++)
                {
                    // Steps grow with distance (quadratically): shadows and lanterns close by get the
                    // detail, the far haze - which is smooth anyway - gets the long strides.
                    float from = marchEnd * Sq(i / (float)steps);
                    float to = marchEnd * Sq((i + 1) / (float)steps);
                    float at = marchEnd * Sq((i + jitter) / (float)steps);
                    float stride = to - from;

                    float3 position = eye + direction * at;
                    float density = DensityAt(position.y);
                    if (density < 1e-8) continue;

                    // The sun: the cloud cookie says whether a cloud is in the way, the shadow map
                    // whether anything on the ground is.
                    Light sun = GetMainLight(TransformWorldToShadowCoord(position), position, half4(1, 1, 1, 1));
                    float sunlit = sun.shadowAttenuation * MainLightCookieAt(position);
                    float3 light = sun.color * (sunlit * Phase(dot(direction, sun.direction), _FogAnisotropy) * _FogSunScatter);
                    light += _FogAmbient;

                #if USE_CLUSTER_LIGHT_LOOP
                    // Lanterns, torches, windows. The same clustered light list URP's lit shaders
                    // walk, asked about this point in the air instead of a surface: which patch of
                    // the screen it is in (the same for every step of this pixel) and how far away.
                    InputData inputData = (InputData)0;
                    inputData.normalizedScreenSpaceUV = uv;
                    inputData.positionWS = position;
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light lamp = GetAdditionalLight(lightIndex, position, half4(1, 1, 1, 1));
                        // Right next to a bulb the inverse square runs away to infinity, and one
                        // unlucky step would paint a white speck. Cap it at ~30 cm from the light.
                        float falloff = min(lamp.distanceAttenuation, 10.0);
                        light += lamp.color * (falloff * lamp.shadowAttenuation
                                             * Phase(dot(direction, lamp.direction), _FogAnisotropy * 0.5) * _FogLocalScatter);
                    LIGHT_LOOP_END
                #endif

                    // How much of the light behind gets through this stride, and how much this stride
                    // adds of its own. Written this way it stays right however thick the stride is.
                    float through = exp(-density * stride);
                    scattered += transmittance * light * (1.0 - through);
                    transmittance *= through;
                }

                // Past the march: all of the remaining air at once, lit by the open sky and an
                // unshadowed sun. This is what gives distant hills and the horizon their haze.
                if (rayEnd > marchEnd)
                {
                    float3 start = eye + direction * marchEnd;
                    float through = exp(-OpticalDepth(start.y, direction.y, rayEnd - marchEnd));
                    Light sun = GetMainLight();
                    float3 light = sun.color * (Phase(dot(direction, sun.direction), _FogAnisotropy) * _FogSunScatter) + _FogAmbient;
                    scattered += transmittance * light * (1.0 - through);
                    transmittance *= through;
                }

                return half4(scattered, transmittance);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            // Seven taps of a gaussian along one axis. A tap counts less the further its depth is
            // from the centre's, so the haze in front of a blade of grass never bleeds onto the hill
            // behind it.
            half4 Frag(Varyings input) : SV_Target
            {
                static const float Weights[4] = { 0.324, 0.232, 0.085, 0.021 };

                float2 uv = input.texcoord;
                float2 stride = _VolumetricTexel.xy * _BlurDirection.xy;
                float centreDepth = SceneEyeDepth(uv);

                half4 sum = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0) * Weights[0];
                float total = Weights[0];

                for (int i = 1; i < 4; i++)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float2 tapUV = uv + stride * (i * side);
                        float depth = SceneEyeDepth(tapUV);
                        float weight = Weights[i] * exp(-abs(depth - centreDepth) / (centreDepth * 0.04 + 0.2));
                        sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, tapUV, 0) * weight;
                        total += weight;
                    }
                }

                return sum / total;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"

            // Out = light + frame * transmittance, which is exactly One / SrcAlpha blending with the
            // light in RGB and the transmittance in alpha.
            Blend One SrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            // Back up to full resolution. A plain bilinear stretch would smear the haze of the sky
            // over every blade of grass standing against it, so each of the four half-resolution
            // texels around this pixel counts only as much as its depth agrees with this pixel's.
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 size = 1.0 / _VolumetricTexel.xy;
                float depth = SceneEyeDepth(uv);

                float2 position = uv * size - 0.5;
                float2 corner = floor(position);
                float2 blend = position - corner;

                half4 sum = 0.0;
                float total = 0.0;
                for (int y = 0; y <= 1; y++)
                {
                    for (int x = 0; x <= 1; x++)
                    {
                        float2 texelUV = (corner + float2(x, y) + 0.5) * _VolumetricTexel.xy;
                        float bilinear = (x == 0 ? 1.0 - blend.x : blend.x) * (y == 0 ? 1.0 - blend.y : blend.y);
                        float agree = exp(-abs(SceneEyeDepth(texelUV) - depth) / (depth * 0.04 + 0.2));
                        float weight = bilinear * agree + 1e-5;
                        sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, texelUV, 0) * weight;
                        total += weight;
                    }
                }

                return sum / total;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
