using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;


#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace OccaSoftware.Altos.Runtime
{
    internal class ScreenShadowsPass : ScriptableRenderPass
    {
        private AltosSkyDirector skyDirector;
        private RTHandle screenShadowsTarget;
        private RTHandle mergeTarget;

        private const string screenShadowsId = "_CloudScreenShadows";
        private const string mergeId = "_CloudShadowsOnScreen";

        private Material screenShadows;
        private Material shadowsToScreen;

        private const string profilerTag = "[Altos] Screenpace Shadows";

        private bool active;
        public bool Active => active;

        public ScreenShadowsPass()
        {
            screenShadowsTarget = RTHandles.Alloc(Shader.PropertyToID(screenShadowsId), name: screenShadowsId);
            mergeTarget = RTHandles.Alloc(Shader.PropertyToID(mergeId), name: mergeId);
        }

        public void Dispose()
        {
            screenShadowsTarget?.Release();
            mergeTarget?.Release();

            screenShadowsTarget = null;
            mergeTarget = null;

            CoreUtils.Destroy(screenShadows);
            CoreUtils.Destroy(shadowsToScreen);

            screenShadows = null;
            shadowsToScreen = null;

            active = false;
        }

        public void Setup(AltosSkyDirector skyDirector)
        {
            active = true;

            this.skyDirector = skyDirector;

            if (screenShadows == null)
                screenShadows = CoreUtils.CreateEngineMaterial(skyDirector.data.shaders.screenShadows);
            if (shadowsToScreen == null)
                shadowsToScreen = CoreUtils.CreateEngineMaterial(skyDirector.data.shaders.renderShadowsToScreen);
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
                     screenShadowsId,
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
            // Setup
            Profiler.BeginSample(profilerTag);
            CommandBuffer cmd = CommandBufferPool.Get(profilerTag);

            ExecutePass(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);

            // Cleanup
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear(); 
            CommandBufferPool.Release(cmd);
            Profiler.EndSample();
        }
#endif

        public RenderTextureDescriptor ConfigurePass(RenderTextureDescriptor cameraTextureDescriptor)
        {
            RenderTextureDescriptor rtDescriptor = cameraTextureDescriptor;
            StaticHelpers.AssignDefaultDescriptorSettings(ref rtDescriptor);
            RenderingUtilsHelper.ReAllocateIfNeeded(
                ref screenShadowsTarget,
                rtDescriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: screenShadowsId
            );

            RenderingUtilsHelper.ReAllocateIfNeeded(ref mergeTarget, rtDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mergeId);

            return rtDescriptor;
        }
        public void ExecutePass(CommandBuffer cmd, RTHandle source)
        {
            // Draw Screen Space Shadows
            cmd.SetGlobalInt(CloudShaderParamHandler.ShaderParams._CastScreenCloudShadows, skyDirector.cloudDefinition.screenShadows ? 1 : 0);
            cmd.SetGlobalTexture(CloudShaderParamHandler.ShaderParams._ScreenTexture, source);
            Blitter.BlitCameraTexture(cmd, source, screenShadowsTarget, screenShadows, 0);

            // Render to Screen
            cmd.SetGlobalTexture(CloudShaderParamHandler.ShaderParams._ScreenTexture, source);
            cmd.SetGlobalTexture(CloudShaderParamHandler.ShaderParams.Shadows._CloudScreenShadows, screenShadowsTarget);
            Blitter.BlitCameraTexture(cmd, source, mergeTarget, shadowsToScreen, 0);
            Blitter.BlitCameraTexture(cmd, mergeTarget, source);
        }
    }
}
