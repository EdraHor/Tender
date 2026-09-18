Shader "Tender/SummerSky"
{
    // A summer sky in one skybox material: gradient, sun, and a layer of fair-weather cumulus.
    //
    // The clouds are not volumetric. They are one flat layer at a fixed altitude, sampled by
    // projecting the view ray onto it - the same trick a painted backdrop uses, and for something
    // a kilometre away it is indistinguishable from a raymarch at a thousandth of the cost.
    //
    // Because Unity builds the ambient light by rendering the skybox into a cubemap, everything
    // here also lights the world: raise the cloud cover and the whole field dims, exactly as it
    // should on a day when the sun goes behind a cloud.
    //
    // The cloud pattern itself lives in CloudLayer.hlsl, because CloudShadows reads the very same
    // layer to put each cloud's shadow on the ground. Tune the clouds here; the shadows follow.

    Properties
    {
        [Header(Sky)]
        _ZenithColor ("Zenith", Color) = (0.16, 0.36, 0.72, 1)
        _HorizonColor ("Horizon", Color) = (0.72, 0.84, 0.92, 1)
        _GroundColor ("Below the horizon", Color) = (0.30, 0.29, 0.25, 1)
        _HorizonFalloff ("How tightly the horizon band hugs the ground", Range(0.5, 8)) = 2.2

        [Header(Sun)]
        _SunColor ("Sun", Color) = (1, 0.97, 0.90, 1)
        _SunSize ("Sun size (degrees)", Range(0.2, 8)) = 1.4
        _SunGlow ("Glow around the sun", Range(0, 1)) = 0.35

        [Header(Clouds)]
        _CloudColor ("Sunlit side", Color) = (1, 0.99, 0.97, 1)
        _CloudShadeColor ("Shaded side", Color) = (0.60, 0.65, 0.74, 1)
        _Coverage ("Cover", Range(0, 1)) = 0.42
        _Softness ("Edge softness", Range(0.01, 0.6)) = 0.18
        _CloudScale ("Cloud size (m)", Float) = 900
        _CloudHeight ("Cloud base, world Y (m)", Float) = 1200
        _Shading ("Shading depth", Range(0, 1)) = 0.75
        _Silver ("Silver lining", Range(0, 2)) = 0.8
        _WindSpeed ("Wind speed (m/s)", Float) = 6
        _WindAngle ("Wind heading (degrees)", Range(0, 360)) = 135
        _HorizonFade ("Clouds fade out this close to the horizon", Range(0.01, 0.4)) = 0.10
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/_Tender/Art/Clouds/CloudLayer.hlsl"   // the cloud pattern, shared with CloudShadows

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _GroundColor;
                float _HorizonFalloff;

                float4 _SunColor;
                float _SunSize;
                float _SunGlow;

                float4 _CloudColor;
                float4 _CloudShadeColor;
                float _Coverage;
                float _Softness;
                float _CloudScale;
                float _CloudHeight;
                float _Shading;
                float _Silver;
                float _WindSpeed;
                float _WindAngle;
                float _HorizonFade;
            CBUFFER_END

            // Written by SkySunBinder. Falling back to straight up keeps the material previewable
            // on its own, and keeps the ambient bake from going dark if the binder is missing.
            float3 _SkySunDirection;

            // Written by TimeOfDay: this hour's colours, painted over the material's own. With
            // _SkyTimeOfDay at 0 (no TimeOfDay in the scene) the material's colours are used as they
            // are, so the material still looks right on its own and in its preview.
            float  _SkyTimeOfDay;
            float4 _SkyZenithNow;
            float4 _SkyHorizonNow;
            float4 _SkyGroundNow;
            float4 _SkySunColorNow;
            float4 _SkyCloudColorNow;
            float4 _SkyCloudShadeNow;

            float3 Now(float4 materialColour, float4 timeOfDayColour)
            {
                return lerp(materialColour.rgb, timeOfDayColour.rgb, saturate(_SkyTimeOfDay));
            }

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                // Unity draws the skybox as a box centred on the camera, so a vertex's object-space
                // position IS the direction you are looking in.
                OUT.direction = IN.positionOS.xyz;
                return OUT;
            }

            /// How much cloud sits along this direction, 0 clear sky to 1 solid.
            float CloudDensity(float3 direction, float2 drift)
            {
                // Where the view ray crosses the cloud layer - in WORLD space, from the camera's real
                // position, so the clouds stay put over the ground as you walk and CloudShadows can
                // find the same cloud from below. Near the horizon this runs away to infinity, which
                // is why the caller fades the result out down there.
                float t = max(_CloudHeight - _WorldSpaceCameraPos.y, 1.0) / max(direction.y, 0.02);
                float2 layerXZ = _WorldSpaceCameraPos.xz + direction.xz * t;

                return CloudLayerDensity(layerXZ, drift, _CloudScale, _Coverage, _Softness);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 direction = normalize(IN.direction);

                float3 sun = _SkySunDirection;
                if (dot(sun, sun) < 1e-4) sun = float3(0, 1, 0);
                sun = normalize(sun);

                // ---- Sky ------------------------------------------------------------------------
                float up = saturate(direction.y);
                float3 sky = lerp(Now(_HorizonColor, _SkyHorizonNow), Now(_ZenithColor, _SkyZenithNow), pow(up, 1.0 / _HorizonFalloff));
                sky = lerp(Now(_GroundColor, _SkyGroundNow), sky, saturate(direction.y * 12.0 + 0.5));

                // Rain (RainZone.cs, _SkyRain from CloudLayer.hlsl): the sky goes a flat grey, the sun
                // hides behind the cover, the clouds lose their sunlit sides.
                float rain = saturate(_SkyRain);
                sky = lerp(sky, lerp(float3(0.52, 0.55, 0.60), float3(0.38, 0.42, 0.48), up), rain);

                // ---- Sun ------------------------------------------------------------------------
                float3 sunColour = Now(_SunColor, _SkySunColorNow);
                float toSun = saturate(dot(direction, sun));
                float angle = degrees(acos(clamp(toSun, -1.0, 1.0)));
                float disc = 1.0 - smoothstep(_SunSize * 0.5, _SunSize * 0.5 + 0.25, angle);
                float glow = pow(toSun, 220.0) * 0.6 + pow(toSun, 8.0) * _SunGlow * 0.25;
                sky += sunColour * (disc * 12.0 * (1.0 - rain) + glow * (1.0 - 0.6 * rain));

                // ---- Clouds ---------------------------------------------------------------------
                float2 drift = CloudDrift(_WindAngle, _WindSpeed, _Time.y);

                float density = CloudDensity(direction, drift);

                // Self-shadowing on the cheap: look a short way towards the sun. If there is more
                // cloud that way, this bit is in its own shadow. It is one extra sample and it is
                // the whole difference between clouds and white blobs.
                float towardsSun = CloudDensity(direction + sun * 0.25, drift);
                float shade = saturate(density * 0.6 + towardsSun * 0.7) * _Shading;

                float3 lit = lerp(Now(_CloudColor, _SkyCloudColorNow), float3(0.62, 0.64, 0.68), rain);
                float3 dark = lerp(Now(_CloudShadeColor, _SkyCloudShadeNow), float3(0.34, 0.37, 0.42), rain);
                float3 cloud = lerp(lit, dark, shade);
                // Thin edges facing the sun light up from behind.
                cloud += sunColour * pow(toSun, 6.0) * (1.0 - density) * _Silver * density * (1.0 - rain);

                float fade = smoothstep(0.0, _HorizonFade, direction.y);
                float3 colour = lerp(sky, cloud, density * fade);

                return half4(colour, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
