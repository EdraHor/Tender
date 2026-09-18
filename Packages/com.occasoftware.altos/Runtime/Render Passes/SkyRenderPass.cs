using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;
using static OccaSoftware.Altos.Runtime.SkyRenderPass;

#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace OccaSoftware.Altos.Runtime
{
    internal class SkyRenderPass : ScriptableRenderPass
    {
        private const string profilerTag = "[Altos] Render Sky";
        AltosSkyDirector skyDirector;

        private RTHandle skyTarget;

        private const string skyTargetId = "_SkyTexture";

        private Material atmosphereMaterial;
        private Material backgroundMaterial;
        Stars[] starsArray;

        public void Dispose()
        {
            skyTarget?.Release();
            skyTarget = null;

            CoreUtils.Destroy(atmosphereMaterial);
            CoreUtils.Destroy(backgroundMaterial);
            atmosphereMaterial = null;
            backgroundMaterial = null;
        }

        public SkyRenderPass()
        {
            skyTarget = RTHandles.Alloc(Shader.PropertyToID(skyTargetId), name: skyTargetId);
        }

        public void Setup(AltosSkyDirector skyDirector)
        {
            this.skyDirector = skyDirector;
            if (atmosphereMaterial == null)
                atmosphereMaterial = CoreUtils.CreateEngineMaterial(skyDirector.data?.shaders.atmosphereShader);
            if (backgroundMaterial == null)
                backgroundMaterial = CoreUtils.CreateEngineMaterial(skyDirector.data?.shaders.backgroundShader);
        }

#if UNITY_2023_3_OR_NEWER
        public class PassData
        {
            public TextureHandle Source;
            public TextureHandle Depth;

            public Vector3 CameraPosition;
            public Vector3 WorldSpaceCameraPos;
            public float FarClipPlane;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // Setting up the render pass in RenderGraph
            using (var builder = renderGraph.AddUnsafePass<PassData>(profilerTag, out var passData))
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                TextureHandle rtHandle = UniversalRenderer.CreateRenderGraphTexture(
                   renderGraph,
                   ConfigurePass(cameraData.cameraTargetDescriptor),
                   skyTargetId,
                   false
               );
                //RTHandle(resourceData.cameraColor)
                passData.Source = resourceData.cameraColor;
                passData.Depth = resourceData.cameraDepthTexture;

                passData.CameraPosition = cameraData.camera.transform.position;
                passData.WorldSpaceCameraPos = cameraData.worldSpaceCameraPos;
                passData.FarClipPlane = cameraData.camera.farClipPlane;

                builder.UseTexture(resourceData.cameraColor, AccessFlags.WriteAll);
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.WriteAll);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }

        private void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            ExecutePass(cmd, data.Source, data.Depth, data.CameraPosition, data.WorldSpaceCameraPos, data.FarClipPlane);
        }
#endif

#if !UNITY_6000_4_OR_NEWER
#if UNITY_2023_3_OR_NEWER
        [Obsolete]
#endif

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigurePass(cameraTextureDescriptor);
        }
#endif

#if !UNITY_6000_4_OR_NEWER

#if UNITY_2023_3_OR_NEWER
        [Obsolete]
#endif
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Profiler.BeginSample(profilerTag);
            CommandBuffer cmd = CommandBufferPool.Get(profilerTag);

            var cameraData = renderingData.cameraData;

            ExecutePass(cmd, cameraData.renderer.cameraColorTargetHandle, cameraData.renderer.cameraDepthTargetHandle, cameraData.camera.transform.position, cameraData.worldSpaceCameraPos, cameraData.camera.farClipPlane);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
            Profiler.EndSample();
        }
#endif

        private static List<Stars> managedStars = new List<Stars>();

        public static void AddStars(Stars stars)
        {
            if (!managedStars.Contains(stars))
            {
                managedStars.Add(stars);
            }
        }
        public static void RemoveStars(Stars stars)
        {
            if (managedStars.Contains(stars))
            {
                managedStars.Remove(stars);
            }
        }

        public RenderTextureDescriptor ConfigurePass(RenderTextureDescriptor cameraTextureDescriptor)
        {
            RenderTextureDescriptor skyTargetDescriptor = cameraTextureDescriptor;
            skyTargetDescriptor.msaaSamples = 1;
            skyTargetDescriptor.depthBufferBits = 0;

            float value = 0.5f;
            if (skyDirector.cloudDefinition.resolutionOptions == ResolutionOptions.Half)
            {
                value = 0.125f;
            }
            skyTargetDescriptor.width = (int)(skyTargetDescriptor.width * value);// 0.125f);
            skyTargetDescriptor.width = Mathf.Max(skyTargetDescriptor.width, 1);
            skyTargetDescriptor.height = (int)(skyTargetDescriptor.height * value);// 0.125f);
            skyTargetDescriptor.width = Mathf.Max(skyTargetDescriptor.height, 1);

            RenderingUtilsHelper.ReAllocateIfNeeded(ref skyTarget, skyTargetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: skyTargetId);

            return skyTargetDescriptor;
        }

        public void ExecutePass(CommandBuffer cmd, RTHandle source, RTHandle depth, Vector3 cameraPosition, Vector3 worldSpaceCameraPos, float farClipPlane)
        {
            cmd.SetRenderTarget(source);

            #region Draw Background
            Matrix4x4 m = Matrix4x4.identity;
            m.SetTRS(worldSpaceCameraPos, Quaternion.identity, Vector3.one * farClipPlane);
            cmd.DrawMesh(skyDirector.data.meshes.skyboxMesh, m, backgroundMaterial, 0);
            #endregion

            #region Draw Stars
            foreach (Stars star in managedStars)
            {

                if (star.starDefinition != null && star.renderPass != null && star.isActiveAndEnabled)
                {
                    star.renderPass.Setup(skyDirector, star.starDefinition);
                    star.renderPass.Draw(cmd, skyDirector.skyDefinition);
                }
            }
            #endregion

            #region Draw Sun and Moon
            int skyObjectCount = Mathf.Min(skyDirector.SkyObjects.Count, _MAX_SKY_OBJECT_COUNT);
            int validSkyObjectCount = 0;
            objectsToRemove.Clear();

            for (int i = 0; i < skyObjectCount; i++)
            {
                skyObject = skyDirector.SkyObjects[i];
                if (skyObject == null || !skyObject.isActiveAndEnabled)
                {
                    objectsToRemove.Add(skyObject);
                    continue;
                }

                m.SetTRS(
                    skyObject.positionRelative + worldSpaceCameraPos,
                    skyObject.GetRotation(),
                    Vector3.one * skyObject.CalculateSize()
                );

                cmd.DrawMesh(skyObject.Quad, m, skyObject.GetMaterial());
                directions[i] = skyObject.GetDirection();
                colors[i] = skyObject.GetColor();
                falloffs[i] = skyObject.GetFalloff();
                skyObject = null;
                validSkyObjectCount++;
            }

            foreach (SkyObject o in objectsToRemove)
            {
                skyDirector.SkyObjects.Remove(o);
            }

            cmd.SetGlobalVectorArray(SkyShaderParams._Direction, directions);
            cmd.SetGlobalVectorArray(SkyShaderParams._Color, colors);
            cmd.SetGlobalFloatArray(SkyShaderParams._Falloff, falloffs);
            cmd.SetGlobalInteger(SkyShaderParams._Count, validSkyObjectCount);
            #endregion

            #region Draw Sky
            if (skyDirector.skyDefinition != null)
            {
                m.SetTRS(
                     cameraPosition,
                    Quaternion.identity,
                    Vector3.one * farClipPlane
                );

                cmd.SetGlobalColor(ShaderParams._HorizonColor, skyDirector.skyDefinition.SkyColors.equatorColor);
                cmd.SetGlobalColor(ShaderParams._ZenithColor, skyDirector.skyDefinition.SkyColors.skyColor);
                cmd.SetGlobalFloat(ShaderParams._SkyColorBlend, skyDirector.skyDefinition.skyColorBlend);
                cmd.DrawMesh(skyDirector.data.meshes.skyboxMesh, m, atmosphereMaterial, 0);

                // Draw Sky Target

                cmd.SetRenderTarget(skyTarget);
                cmd.ClearRenderTarget(true, true, Color.black);

                cmd.SetGlobalInteger(SkyShaderParams._Count, 0);
                cmd.DrawMesh(skyDirector.data.meshes.skyboxMesh, m, atmosphereMaterial, 0);
                cmd.SetGlobalInt(ShaderParams._HasSkyTexture, 1);
                cmd.SetGlobalTexture("_SkyTexture", skyTarget);

                cmd.SetRenderTarget(source, depth);
            }
            #endregion
        }

        internal static class SkyShaderParams
        {
            public static int _Direction = Shader.PropertyToID("_Direction");
            public static int _Color = Shader.PropertyToID("_Color");
            public static int _Falloff = Shader.PropertyToID("_Falloff");
            public static int _Count = Shader.PropertyToID("_SkyObjectCount");
        }

        private static int _MAX_SKY_OBJECT_COUNT = 8;
        Vector4[] directions = new Vector4[_MAX_SKY_OBJECT_COUNT];
        Vector4[] colors = new Vector4[_MAX_SKY_OBJECT_COUNT];
        float[] falloffs = new float[_MAX_SKY_OBJECT_COUNT];
        SkyObject skyObject;
        List<SkyObject> objectsToRemove = new List<SkyObject>();

        private static class ShaderParams
        {
            public static int _MainTex = Shader.PropertyToID("_MainTex");
            public static int _Color = Shader.PropertyToID("_Color");
            public static int _SunFalloff = Shader.PropertyToID("_SunFalloff");
            public static int _SunColor = Shader.PropertyToID("_SunColor");
            public static int _SunForward = Shader.PropertyToID("_SunForward");
            public static int _HorizonColor = Shader.PropertyToID("_HorizonColor");
            public static int _ZenithColor = Shader.PropertyToID("_ZenithColor");
            public static int _HasSkyTexture = Shader.PropertyToID("_HasSkyTexture");
            public static int _SkyColorBlend = Shader.PropertyToID("_SkyColorBlend");
        }
    }
}
