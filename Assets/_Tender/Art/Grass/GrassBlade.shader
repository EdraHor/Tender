Shader "Tender/GrassBlade"
{
    // Draws the tall grass. Every blade in the field is one instance read out of a buffer that
    // GrassPlacement.compute filled this frame - there is no per-blade GameObject, transform or
    // draw call anywhere.
    //
    // The blade mesh is straight and vertical. All the shape you see is made here: the instance's
    // facing, its height and width, the splay its clump gives it, and the arc it bends through
    // under wind and passing objects.
    //
    // Colour and light are NOT set on this material. Each blade's colours and seed heads come from
    // its GrassType, and the light from GrassLook.cs, through GrassLook.hlsl - shared with the
    // ground under the grass, so that far away, where there are no blades left to draw, the ground
    // can pass for grass.

    Properties
    {
        [Header(Shape)]
        _BladeNormal ("Shade by the blade vs by its tuft", Range(0, 1)) = 0.35
        _TuftRound ("Shade each tuft as a dome", Range(0, 4)) = 1.5
        _EdgeThicken ("Thicken blades seen edge-on", Range(0, 2)) = 0.8
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "GrassCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _BladeNormal;
            float _TuftRound;
            float _EdgeThicken;
        CBUFFER_END

        // GrassInstance itself is declared in GrassTypes.hlsl, shared with the compute that fills it.
        StructuredBuffer<GrassInstance> _Instances;

        float  _GrassLod;               // which mesh this draw uses: 0, 1 or 2 (GrassField, per draw)
        float4 _GrassLodDistances;      // x: where LOD0 hands over to LOD1, y: LOD1 to LOD2 (GrassField)

        // GrassBladeMesh.Build narrows a blade to this share of its width at the tip. Must match.
        #define GRASS_BLADE_TAPER 0.35

        /// The height up the blade (0..1) of row `row` of a mesh with `segments` quads. The rows are
        /// packed towards the tip, exactly as GrassBladeMesh.Build lays them.
        float GrassRowHeight(float row, float segments)
        {
            return 1.0 - pow(1.0 - row / segments, 1.5);
        }

        /// Where one point of the blade mesh ends up for this instance: sized, turned, bent, thickened.
        ///   mesh      : the point in the straight mesh's own space, x across, y from 0 root to 1 tip
        ///   uvX       : 0 on the left edge, 1 on the right, 0.5 at the tip
        ///   restNormal: the mesh normal at that point, already turned to the blade's facing
        float3 GrassBladePoint(GrassInstance blade, GrassType type, float3 mesh, float uvX,
                               float2 direction, float lean, float3 restNormal, out float3 normalWS)
        {
            // An ear or a plume is the top of the stem grown wide. The blade mesh already narrows to
            // a point, so widening its upper rows turns the tip into a spindle - a wheat ear - with
            // no extra geometry and no second mesh.
            float width = blade.width;
            if ((int)type.headStyle == GRASS_HEAD_EAR && blade.flower > 0.5)
                width *= lerp(1.0, type.headWidth, smoothstep(type.headStart - 0.15, type.headStart + 0.05, mesh.y));

            float3 local = GrassYaw(float3(mesh.x * width, mesh.y * blade.height, mesh.z * width), blade.yaw);
            normalWS = restNormal;
            float3 positionWS = blade.position + GrassBend(local, blade.height, direction, lean, normalWS);

            // A flat blade seen exactly edge-on is a line with no width, and a field of them
            // flickers between green and gaps as the camera turns. Pushing the two edges apart
            // along the screen's horizontal, only as the blade turns edge-on, keeps a sliver of
            // green there. The tip sits on the centre line (uv.x = 0.5) and does not move.
            float2 facing = float2(sin(blade.yaw), cos(blade.yaw));
            float2 toCamera = normalize(_WorldSpaceCameraPos.xz - blade.position.xz + 1e-5);
            float edgeOn = 1.0 - abs(dot(toCamera, facing));
            float3 screenRight = UNITY_MATRIX_V[0].xyz;
            return positionWS + screenRight * ((uvX * 2.0 - 1.0) * width * 0.5 * edgeOn * edgeOn * _EdgeThicken);
        }

        // Shared by every pass: takes a vertex of the flat blade mesh and puts it where this
        // instance's blade actually is, bent and all - at `time`, which is now for every pass but
        // the motion vectors, which also ask where it was a frame ago.
        void GrassVertexAt(float3 positionOS, float3 normalOS, float2 uv, uint instanceID, float time,
                           out GrassInstance blade, out float3 positionWS, out float3 normalWS)
        {
            blade = _Instances[instanceID];
            GrassType type = _GrassTypes[(int)blade.type];

            // The lean belongs to the whole blade, so it is worked out once, whichever points of the
            // blade are asked about below. The facing is also the way its clump splays it.
            float2 facing = float2(sin(blade.yaw), cos(blade.yaw));
            float2 direction;
            float lean = GrassLeanAt(blade.position, facing * blade.splay, type.windResponse, time, direction);

            float3 restNormal = GrassYaw(normalOS, blade.yaw);
            positionWS = GrassBladePoint(blade, type, positionOS, uv.x, direction, lean, restNormal, normalWS);

            // MORPH INTO THE NEXT MESH before switching to it.
            //
            // The meshes nest: every row of the 2-quad blade is also a row of the 4-quad blade, and the
            // 1-quad blade is just the root and the tip. So the finer blade can take the coarser one's
            // exact shape by sliding each of its in-between rows onto the straight line joining the
            // rows either side - which is all the coarser mesh is. Over the last stretch before its
            // switch a blade does exactly that, and at the switch itself the two meshes are the same
            // shape: nothing jumps. Without it, every blade crossing the switch changed its curve at
            // once, and walking forward you could see that band of grass ripple just ahead of you.
            if (_GrassLod < 1.5)
            {
                float segments = _GrassLod < 0.5 ? 4.0 : 2.0;
                float switchAt = _GrassLod < 0.5 ? _GrassLodDistances.x : _GrassLodDistances.y;
                float morph = smoothstep(switchAt * 0.7, switchAt, GrassLodDistance(blade.position, _WorldSpaceCameraPos));
                float row = round(segments * (1.0 - pow(saturate(1.0 - uv.y), 2.0 / 3.0)));

                if (morph > 0.0 && fmod(row, 2.0) > 0.5)
                {
                    float below = GrassRowHeight(row - 1.0, segments);
                    float above = GrassRowHeight(row + 1.0, segments);
                    float side = uv.x * 2.0 - 1.0;
                    float3 unused;

                    float3 lower = GrassBladePoint(blade, type, float3(side * 0.5 * lerp(1.0, GRASS_BLADE_TAPER, below), below, 0.0),
                                                   uv.x, direction, lean, restNormal, unused);

                    // The row above an odd row is either another row, or - on the top row - the tip.
                    bool tip = row + 1.0 >= segments;
                    float3 upperMesh = tip ? float3(0.0, 1.0, 0.0) : float3(side * 0.5 * lerp(1.0, GRASS_BLADE_TAPER, above), above, 0.0);
                    float3 upper = GrassBladePoint(blade, type, upperMesh, tip ? 0.5 : uv.x, direction, lean, restNormal, unused);

                    float3 onCoarse = lerp(lower, upper, (uv.y - below) / (above - below));
                    positionWS = lerp(positionWS, onCoarse, morph);
                }
            }
        }

        void GrassVertex(float3 positionOS, float3 normalOS, float2 uv, uint instanceID,
                         out GrassInstance blade, out float3 positionWS, out float3 normalWS)
        {
            GrassVertexAt(positionOS, normalOS, uv, instanceID, _Time.y, blade, positionWS, normalWS);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off            // a blade is a single strip; both faces are visible
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
            #include "GrassLook.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 rootWS : TEXCOORD3;
                float fogCoord : TEXCOORD4;
                float3 shapeNormal : TEXCOORD5;     // the tuft's dome close up, the ground far away
                float4 blade : TEXCOORD6;           // region, flower, tint, gust
                nointerpolation float2 kind : TEXCOORD7;    // type index, zone
            };

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                GrassInstance blade;
                GrassVertex(IN.positionOS, IN.normalOS, IN.uv, instanceID, blade, OUT.positionWS, OUT.normalWS);

                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                OUT.rootWS = blade.position;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);

                float2 n = blade.groundNormal;
                float3 ground = float3(n.x, sqrt(saturate(1.0 - dot(n, n))), n.y);

                // Shade the TUFT, not just the blade: one normal per clump, tipped outwards the
                // further a blade grows from the clump's centre - which its splay already measures,
                // and it faces outwards already. So a tuft lights like a soft dome with a sunny side
                // and a shaded side, the painted look of a Breath of the Wild field, instead of every
                // blade flickering light and dark on its own. Ghost of Tsushima blends blade normals
                // towards a shared clump normal for the same reason: it stopped distant grass
                // sparkling. Far away it all flattens to the ground, as the terrain beyond is lit.
                float2 outward = float2(sin(blade.yaw), cos(blade.yaw));
                float3 dome = normalize(ground + float3(outward.x, 0.0, outward.y) * (blade.splay * _TuftRound));
                float carpet = GrassCarpet(length(blade.position - _WorldSpaceCameraPos));
                OUT.shapeNormal = normalize(lerp(dome, ground, carpet));

                // The gust once per vertex, not per pixel: it changes over tens of metres.
                OUT.blade = float4(blade.region, blade.flower, blade.tint, GrassGust(blade.position.xz));
                OUT.kind = float2(blade.type, blade.zone);
                return OUT;
            }

            half4 frag(Varyings IN, bool isFront : SV_IsFrontFace) : SV_Target
            {
                float region = IN.blade.x;
                float flower = IN.blade.y;
                float tint = IN.blade.z;
                float gust = IN.blade.w;

                int typeIndex = (int)IN.kind.x;
                GrassType type = _GrassTypes[typeIndex];

                float distance = length(IN.rootWS - _WorldSpaceCameraPos);
                float carpet = GrassCarpet(distance);

                // Root to tip, dark to bright. The root is exactly the ground's colour, so the
                // blade grows out of the terrain instead of standing on it.
                float3 tip = GrassTipAlbedo(IN.rootWS.xz, region, tint, typeIndex, IN.kind.y);
                float3 albedo = lerp(_GrassGroundColor.rgb, tip, smoothstep(0.0, 0.85, IN.uv.y));

                // Near the camera a seed head or a bloom is real geometry drawn by GrassFlower.shader.
                // Further out it would be smaller than a pixel and would only shimmer, so it is not
                // drawn at all and the tip of the blade is simply painted instead. The two hand over
                // across a short band, and both read the same per-blade flag.
                //
                // Seed heads are painted at a third of full strength. The painted band covers far
                // more of the blade than the little disc it stands in for, so painting it solid made
                // distant flowering grass several times whiter than the same grass seen close up. A
                // bloom is bigger than its thin stem, so it keeps its full colour. An ear IS the top
                // of the blade, so it is coloured at every distance.
                int style = (int)type.headStyle;
                float painted = style == GRASS_HEAD_EAR
                    ? 1.0
                    : smoothstep(_GrassFlowerDistance * 0.75, _GrassFlowerDistance, GrassLodDistance(IN.rootWS, _WorldSpaceCameraPos));
                float strength = style == GRASS_HEAD_SEED ? 0.35 : 1.0;
                float head = flower * painted * smoothstep(type.headStart, type.headStart + 0.07, IN.uv.y);

                // An ear darkens towards where it joins the stem, so it reads as a shape with a base
                // and a tip rather than as a flat pale card.
                float alongHead = saturate((IN.uv.y - type.headStart) / max(1.0 - type.headStart, 1e-3));
                float3 headColor = type.headColor.rgb * (style == GRASS_HEAD_EAR ? lerp(0.65, 1.0, alongHead) : 1.0);
                albedo = lerp(albedo, headColor, head * strength);

                // Far away every blade turns into the same carpet colour the ground uses.
                albedo = lerp(albedo, GrassCarpetAlbedo(GrassTypeFar(type, tip)), carpet);

                // Down among the stems it is darker. A seed head sits in the open air, and the
                // carpet is the TOPS of the grass, so neither gets the darkening.
                float ao = lerp(lerp(1.0 - _GrassRootShade, 1.0, IN.uv.y), 1.0, max(head, carpet));

                // Shade partly by the blade's own surface and partly by its tuft's. All blade gives
                // a field that sparkles light and dark blade by blade; all tuft gives soft domes.
                // Far away it is all ground, exactly as the terrain beyond is lit.
                float3 bladeNormal = normalize(IN.normalWS) * (isFront ? 1.0 : -1.0);
                float3 normalWS = normalize(lerp(IN.shapeNormal, bladeNormal, _BladeNormal * (1.0 - carpet)));

                // Heads that shine - ripe wheat, pampas plumes - catch more of everything that rides on
                // "tipness": the sheen, the glow against the sun and the brightening of a gust. That
                // travelling shimmer across a field of heads is most of the Ghost of Tsushima look.
                float tipness = lerp(IN.uv.y, 0.7, carpet) * lerp(1.0, type.headShine, max(head, carpet * flower));
                float3 colour = GrassShade(albedo, normalWS, IN.positionWS, tipness, gust, ao, GetNormalizedScreenSpaceUV(IN.positionCS));
                colour = MixFog(colour, IN.fogCoord);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            float4 vert(Attributes IN, uint instanceID : SV_InstanceID) : SV_POSITION
            {
                GrassInstance blade;
                float3 positionWS, normalWS;
                GrassVertex(IN.positionOS, IN.normalOS, IN.uv, instanceID, blade, positionWS, normalWS);

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));
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
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            float4 vert(Attributes IN, uint instanceID : SV_InstanceID) : SV_POSITION
            {
                GrassInstance blade;
                float3 positionWS, normalWS;
                GrassVertex(IN.positionOS, IN.normalOS, IN.uv, instanceID, blade, positionWS, normalWS);
                return TransformWorldToHClip(positionWS);
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            // How far each pixel of grass moved since the last frame, for temporal anti-aliasing.
            //
            // TAA is the right cure for a field of blades thinner than a pixel - it averages the
            // flicker away over frames - but it averages by following each pixel back to where it
            // was. Grass in the wind moves while the ground under it does not, so without this pass
            // TAA sees the ground's motion, blends each blade with wherever it no longer is, and the
            // whole field smears. Here the blade is simply built twice: at this frame's wind and at
            // the last frame's, and the difference is the motion.
            //
            // URP runs this pass for the grass every frame even though the draw itself never moves as
            // an object. Checked by adding a fixed offset to the result: the grass smeared under
            // URP's object motion blur, and the seed heads - which did not get the offset - did not.
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            Cull Off
            ColorMask RG

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionNow : TEXCOORD0;      // without the TAA jitter
                float4 positionBefore : TEXCOORD1;
            };

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                GrassInstance blade;
                float3 now, before, normalWS;
                // _LastTimeParameters and not _Time minus unity_DeltaTime: stepping a paused Play Mode
                // frame by frame moves time on but reports a delta of zero, and the grass would claim
                // to be standing still. URP keeps this one right; Shader Graph uses it for the same job.
                GrassVertexAt(IN.positionOS, IN.normalOS, IN.uv, instanceID, _Time.y, blade, now, normalWS);
                GrassVertexAt(IN.positionOS, IN.normalOS, IN.uv, instanceID, _LastTimeParameters.x, blade, before, normalWS);

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
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
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
                GrassInstance blade;
                float3 positionWS;
                GrassVertex(IN.positionOS, IN.normalOS, IN.uv, instanceID, blade, positionWS, OUT.normalWS);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            half4 frag(Varyings IN, bool isFront : SV_IsFrontFace) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS) * (isFront ? 1.0 : -1.0);
                return half4(NormalizeNormalPerPixel(normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
