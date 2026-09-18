using System;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace OccaSoftware.Altos.Runtime
{
    internal class AtmosphereBlendingPass : ScriptableRenderPass
    {
        AltosSkyDirector skyDirector;
        private Material blendingMaterial;
        private RTHandle blendingTarget;

        private const string blendingTargetId = "_AltosFogTarget";
        private const string profilerTag = "[Altos] Atmosphere Blending";

        public AtmosphereBlendingPass()
        {
            blendingTarget = RTHandles.Alloc(Shader.PropertyToID(blendingTargetId), name: blendingTargetId);
        }

        public void Dispose()
        {
            blendingTarget?.Release();
            blendingTarget = null;
            CoreUtils.Destroy(blendingMaterial);
            blendingMaterial = null;
        }

        public void Setup(AltosSkyDirector skyDirector)
        {
            this.skyDirector = skyDirector;

            if (blendingMaterial == null)
                blendingMaterial = CoreUtils.CreateEngineMaterial(skyDirector.data.shaders.atmosphereBlending);
        }

#if UNITY_2023_3_OR_NEWER
        public class PassData
        {
            public TextureHandle Source;
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
                     blendingTargetId,
                     false
                 );

                passData.Source = resourceData.cameraColor;

                builder.UseTexture(passData.Source, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }

        private void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            ExecutePass(cmd, data.Source);
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

            ExecutePass(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);

            Profiler.EndSample();
        }
#endif

        public RenderTextureDescriptor ConfigurePass(RenderTextureDescriptor cameraTextureDescriptor)
        {
            RenderTextureDescriptor rtDescriptor = cameraTextureDescriptor;
            rtDescriptor.msaaSamples = 1;
            rtDescriptor.depthBufferBits = 0;

            RenderingUtilsHelper.ReAllocateIfNeeded(ref blendingTarget, rtDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: blendingTargetId);

            return rtDescriptor;
        }

        public void ExecutePass(CommandBuffer cmd, RTHandle source)
        {
            cmd.SetGlobalFloat(ShaderParams._Density, skyDirector.atmosphereDefinition.GetDensity());
            cmd.SetGlobalFloat(ShaderParams._BlendStart, skyDirector.atmosphereDefinition.start);
            cmd.SetGlobalTexture(CloudShaderParamHandler.ShaderParams._ScreenTexture, source);
            Blitter.BlitCameraTexture(cmd, source, blendingTarget, blendingMaterial, 0);
            Blitter.BlitCameraTexture(cmd, blendingTarget, source);
        }


        public static class ShaderParams
        {
            public static int _Density = Shader.PropertyToID("_Density");
            public static int _BlendStart = Shader.PropertyToID("_BlendStart");
        }
    }
}
