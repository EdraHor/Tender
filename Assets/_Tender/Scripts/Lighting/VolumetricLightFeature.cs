using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Draws light in the air - haze, sun shafts, the glow round a lantern - over the frame.
///
/// URP has no volumetric lighting of its own (only HDRP does), so this is it. The renderer feature
/// only schedules the passes; what the air looks like is set on the Atmosphere component in the scene,
/// and a scene without one simply gets no haze.
///
/// Four passes over VolumetricLight.shader, after the opaque world and the sky and before anything
/// transparent:
///   1. march every view ray through the air, at half resolution (or quarter);
///   2. and 3. blur that across and then down, without blurring over edges in depth;
///   4. stretch it back to full size over the frame.
///
/// Add it to the renderer asset (PC_Renderer) like any other renderer feature.
/// </summary>
public class VolumetricLightFeature : ScriptableRendererFeature
{
    [Tooltip("Hidden/Tender/VolumetricLight. Found by name if left empty.")]
    [SerializeField] private Shader _shader;

    private Material _material;
    private VolumetricLightPass _pass;

    public override void Create()
    {
        // Before transparents: URP has copied the depth buffer by then, and particles and glass
        // drawn later sit in front of the haze instead of under it.
        _pass = new VolumetricLightPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Atmosphere atmosphere = Atmosphere.Current;
        if (atmosphere == null) return;

        // Material previews, reflection probes and the like get no haze.
        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType != CameraType.Game && cameraType != CameraType.SceneView) return;

        if (_material == null)
        {
            if (_shader == null) _shader = Shader.Find("Hidden/Tender/VolumetricLight");
            if (_shader == null) return;
            _material = CoreUtils.CreateEngineMaterial(_shader);
        }

        _pass.Setup(_material, atmosphere.ResolutionDivisor);
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }

    private class VolumetricLightPass : ScriptableRenderPass
    {
        private const int MarchPass = 0;
        private const int BlurPass = 1;
        private const int CompositePass = 2;

        private static readonly int TexelId = Shader.PropertyToID("_VolumetricTexel");
        private static readonly int BlurDirectionId = Shader.PropertyToID("_BlurDirection");

        private Material _material;
        private int _divisor;

        private class PassData
        {
            public Material Material;
            public TextureHandle Source;
            public int Pass;
            public Vector4 Texel;
            public Vector4 BlurDirection;
        }

        public void Setup(Material material, int divisor)
        {
            _material = material;
            _divisor = Mathf.Max(divisor, 1);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (!resources.cameraDepthTexture.IsValid()) return;

            TextureDesc frame = renderGraph.GetTextureDesc(resources.cameraColor);
            int width = Mathf.Max(frame.width / _divisor, 1);
            int height = Mathf.Max(frame.height / _divisor, 1);
            var texel = new Vector4(1f / width, 1f / height, 0f, 0f);

            var small = new TextureDesc(width, height)
            {
                name = "_VolumetricLight",
                format = GraphicsFormat.R16G16B16A16_SFloat,   // light in RGB can go well past 1
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = false
            };
            TextureHandle marched = renderGraph.CreateTexture(small);
            small.name = "_VolumetricLightBlur";
            TextureHandle blurred = renderGraph.CreateTexture(small);

            AddPass(renderGraph, resources, "Volumetric Light: March", TextureHandle.nullHandle, marched, MarchPass, texel, Vector4.zero);
            AddPass(renderGraph, resources, "Volumetric Light: Blur Across", marched, blurred, BlurPass, texel, new Vector4(1f, 0f));
            AddPass(renderGraph, resources, "Volumetric Light: Blur Down", blurred, marched, BlurPass, texel, new Vector4(0f, 1f));
            AddPass(renderGraph, resources, "Volumetric Light: Composite", marched, resources.activeColorTexture, CompositePass, texel, Vector4.zero);
        }

        private void AddPass(RenderGraph renderGraph, UniversalResourceData resources, string name,
                             TextureHandle source, TextureHandle target, int pass, Vector4 texel, Vector4 blurDirection)
        {
            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(name, out PassData data))
            {
                data.Material = _material;
                data.Source = source;
                data.Pass = pass;
                data.Texel = texel;
                data.BlurDirection = blurDirection;

                if (source.IsValid()) builder.UseTexture(source);
                builder.UseTexture(resources.cameraDepthTexture);

                // The march reads URP's shadow maps and light cookie, which earlier passes published
                // as globals. Declaring the lot is simpler than naming each and missing one.
                builder.UseAllGlobalTextures(true);

                // The composite BLENDS onto the frame, so what is already there has to be kept.
                builder.SetRenderAttachment(target, 0, pass == CompositePass ? AccessFlags.ReadWrite : AccessFlags.Write);

                // The two blurs share one material and differ only in direction. Setting it on the
                // command buffer, not on the material, keeps each value next to its own draw: the
                // buffer may run after both passes have been recorded.
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalVector(TexelId, d.Texel);
                    context.cmd.SetGlobalVector(BlurDirectionId, d.BlurDirection);

                    if (d.Source.IsValid())
                        Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, d.Pass);
                    else
                        Blitter.BlitTexture(context.cmd, new Vector4(1f, 1f, 0f, 0f), d.Material, d.Pass);
                });
            }
        }
    }
}
