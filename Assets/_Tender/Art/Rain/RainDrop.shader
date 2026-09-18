Shader "Tender/RainDrop"
{
    // The drops you see: thin streaks falling through a box that travels with the camera.
    //
    // There are no particles. Every drop is a number: its index picks, through a hash, a column of
    // the world it falls down and how far along its fall it is, and time moves it. The columns
    // repeat every _RainBox metres, so the box can follow the camera without a single drop popping
    // in or out - the drops were always there, the box just shows the ones near you. A drop ends
    // where the rain map says the ground or a roof is (RainMap.hlsl) and is not drawn below it.
    //
    // Each drop is a quad stretched along its velocity - a real drop at 9 m/s smears over the
    // exposure of a frame - and lit by whatever light is in the air where it is (AirLight.hlsl),
    // which is what keeps the rain from glowing at night and makes it flash in a lamp's beam.

    Properties
    {
        _Exposure ("Brightness per unit of light", Range(0, 2)) = 0.1
        _Length ("Streak length (m)", Range(0.02, 0.5)) = 0.15
        _Width ("Streak width (m)", Range(0.001, 0.02)) = 0.004
        _Box ("Box around the camera (m)", Range(8, 64)) = 24
        _Height ("Box height (m)", Range(4, 40)) = 16
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+50" }

        Pass
        {
            Name "Drops"
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
            #include "RainMap.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Exposure;
                float _Length;
                float _Width;
                float _Box;
                float _Height;
            CBUFFER_END

            #define SPLASH_FALL 0.6    // metres of "fall" past the landing point during which the splash shows

            float _RainIntensity;      // set by Rain.cs
            float4 _RainVelocity;      // m/s, set by Rain.cs
            float _RainSeed;           // set by RainDrops.cs: rises with time so the drops keep falling

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 fade : TEXCOORD1;      // x: along the streak (1 at the drop, 0 at its tail), y: across, z: up a splash
                float fogCoord : TEXCOORD2;
            };

            float3 Hash3(uint n)
            {
                float3 p = float3(n * 0.1031, n * 0.1030, n * 0.0973);
                p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.3));
                p += dot(p, p.yzx + 33.33);
                return frac((p.xxy + p.yzz) * p.zyx);
            }

            Varyings vert(uint vertexId : SV_VertexID, uint drop : SV_InstanceID)
            {
                Varyings OUT;
                float3 seed = Hash3(drop + 1);

                // The column this drop falls down, in a field that repeats every _Box metres,
                // shown at the copy nearest the camera.
                float3 eye = _WorldSpaceCameraPos;
                float2 column = seed.xy * _Box;
                float2 xz = eye.xz + fmod(column - eye.xz + _Box * 1000.5, _Box) - _Box * 0.5;

                // How far down its fall it is: time plus its own offset, wrapping at the box height.
                float top = eye.y + _Height * 0.6;
                float fallen = fmod(_RainSeed * -_RainVelocity.y + seed.z * _Height, _Height);
                float y = top - fallen;

                // Slanting rain drifts sideways as it falls.
                float3 velocity = _RainVelocity.xyz;
                float2 drift = velocity.xz / max(-velocity.y, 0.1) * fallen;
                float3 position = float3(xz.x + drift.x, y, xz.y + drift.y);

                // Landed? Then it is not in the air any more.
                float landing = RainLandingHeight(position.xz);
                float alive = position.y > landing ? 1.0 : 0.0;

                // A quad along the velocity: two triangles, six vertices. Corners 0..3 by id.
                uint corner = vertexId % 6;
                float along = (corner == 1 || corner == 2 || corner == 4) ? 1.0 : 0.0;  // 1 = the drop's head
                float side = (corner == 2 || corner == 4 || corner == 5) ? 1.0 : -1.0;
                float3 axis = normalize(velocity);
                float3 toCamera = normalize(eye - position);
                float3 across = normalize(cross(axis, toCamera));
                float3 positionWS = position - axis * _Length * (1.0 - along) + across * (_Width * 0.5 * side);
                positionWS = lerp(position, positionWS, alive);
                float3 fade = float3(along, side, 0.0);

                // The splash IS the drop: for the first half metre of its fall past the landing
                // point the same quad is drawn as a little crown where it hit - growing, fading -
                // so every drop that lands makes a splash and nothing has to be spawned.
                float past = landing - position.y;
                if (past > 0.0 && past < SPLASH_FALL)
                {
                    float grow = past / SPLASH_FALL;
                    float2 landedDrift = velocity.xz / max(-velocity.y, 0.1) * (top - landing);
                    float3 landed = float3(xz.x + landedDrift.x, landing + 0.005, xz.y + landedDrift.y);
                    float3 right = normalize(cross(float3(0, 1, 0), toCamera));
                    float3 up = cross(toCamera, right);
                    float size = lerp(0.01, 0.05, grow);
                    positionWS = landed + right * (side * size) + up * ((along * 2.0 - 1.0) * size);
                    fade = float3(1.0 - grow, side, along * 2.0 - 1.0);
                }

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.fade = fade;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Brightest at the head, fading down the tail; soft across (and round, for a splash).
                // Saturated because with MSAA a pixel centre can lie just outside a streak this thin,
                // where the interpolated "across" runs past 1 and, unclamped, the drop would SUBTRACT
                // light.
                float alpha = IN.fade.x * IN.fade.x * saturate(1.0 - IN.fade.y * IN.fade.y - IN.fade.z * IN.fade.z);
                float3 light = LightInTheAir(IN.positionWS, GetNormalizedScreenSpaceUV(IN.positionCS));
                float3 colour = light * _Exposure * alpha;

                // Added light fades out with distance: into black, not into the fog's colour.
                colour = MixFogColor(colour, half3(0, 0, 0), IN.fogCoord);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
