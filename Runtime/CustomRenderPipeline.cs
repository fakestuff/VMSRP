using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.CustomRenderPipeline
{
    public sealed class CustomRenderPipeline : RenderPipeline
    {
        private RTHandle[] m_GBuffers;
        private RenderTargetIdentifier[] m_GBufferRTIDs;
        private RTHandle m_DepthBuffer;
        private RenderTargetIdentifier m_DepthBufferRTID;
        private int m_GBufferCount = 3;
        private MSAASamples m_MSAASample = MSAASamples.None;
        public CustomRenderPipeline(CustomRenderPipelineAsset asset)
        {
            RTHandles.Initialize(1920, 1080, false, MSAASamples.None);

            CreateGBuffers();
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            int maxWidth = 1;
            int maxHeight = 1;
            foreach (var camera in cameras)
            {
                maxWidth = Mathf.Max(maxWidth, camera.pixelWidth);
                maxHeight = Mathf.Max(maxHeight, camera.pixelHeight);
            }
            RTHandles.SetReferenceSize(maxWidth,maxHeight,m_MSAASample);
            ShaderBindings.SetPerFrameShaderVariables(context);
            foreach (Camera camera in cameras)
            {
#if UNITY_EDITOR
                if (camera.cameraType == CameraType.SceneView)
                    ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif

                CullingResults cullingResults = Cull(context, camera);
                ShaderBindings.SetPerCameraShaderVariables(context, camera);
                DrawCamera(context, cullingResults, camera);
            }
        }

        CullingResults Cull(ScriptableRenderContext context, Camera camera)
        {
            // Culling. Adjust culling parameters for your needs. One could enable/disable
            // per-object lighting or control shadow caster distance.
            camera.TryGetCullingParameters(out var cullingParameters);
            return context.Cull(ref cullingParameters);
        }

        void DrawCamera(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
        {
            bool enableDynamicBatching = false;
            bool enableInstancing = false;
            
            context.SetupCameraProperties(camera);
            
            PerObjectData perObjectData = PerObjectData.None;

            FilteringSettings opaqueFilteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            FilteringSettings transparentFilteringSettings = new FilteringSettings(RenderQueueRange.transparent);

            SortingSettings opaqueSortingSettings = new SortingSettings(camera);
            opaqueSortingSettings.criteria = SortingCriteria.CommonOpaque;

            // ShaderTagId must match the "LightMode" tag inside the shader pass.
            // If not "LightMode" tag is found the object won't render.
            DrawingSettings opaqueDrawingSettings = new DrawingSettings(ShaderPassTag.GBuffer, opaqueSortingSettings);
            opaqueDrawingSettings.enableDynamicBatching = enableDynamicBatching;
            opaqueDrawingSettings.enableInstancing = enableInstancing;
            opaqueDrawingSettings.perObjectData = perObjectData;

            // Sets active render target and clear based on camera background color.
            var cmd = CommandBufferPool.Get();
            cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            cmd.ClearRenderTarget(true, true, camera.backgroundColor);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            context.DrawRenderers(cullingResults, ref opaqueDrawingSettings, ref opaqueFilteringSettings);
            // Renders skybox if required
            if (camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
                context.DrawSkybox(camera);
            // Render Opaque objects given the filtering and settings computed above.
            // This functions will sort and batch objects.
            
            // DrawGBuffers
            DrawingSettings gBufferDrawingSettings = new DrawingSettings(ShaderPassTag.GBuffer, opaqueSortingSettings);
            gBufferDrawingSettings.enableDynamicBatching = enableDynamicBatching;
            gBufferDrawingSettings.enableInstancing = enableInstancing;
            gBufferDrawingSettings.perObjectData = perObjectData;
            cmd = CommandBufferPool.Get("Gbuffer");
            cmd.SetRenderTarget(m_GBufferRTIDs,m_DepthBufferRTID);
            cmd.ClearRenderTarget(true, true, camera.backgroundColor);
            //CoreUtils.SetRenderTarget(cmd, m_GBufferRTIDs, m_DepthBufferRTID, ClearFlag.All);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            context.DrawRenderers(cullingResults, ref opaqueDrawingSettings, ref opaqueFilteringSettings);

            

            // Submit commands to GPU. Up to this point all commands have been enqueued in the context.
            // Several submits can be done in a frame to better controls CPU/GPU workload.
            context.Submit();
        }

        void CreateGBuffers()
        {
            m_GBuffers = new RTHandle[m_GBufferCount];
            m_GBufferRTIDs = new RenderTargetIdentifier[m_GBufferCount];
            m_GBuffers[0] = RTHandles.Alloc(Vector2.one, TextureXR.slices, colorFormat: GraphicsFormat.R8G8B8A8_UNorm, dimension: TextureXR.dimension, useDynamicScale: true, name: string.Format("GBuffer{0}", 0), enableRandomWrite: false);
            m_GBuffers[1] = RTHandles.Alloc(Vector2.one, TextureXR.slices, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureXR.dimension, useDynamicScale: true, name: string.Format("GBuffer{0}", 1), enableRandomWrite: false);
            m_GBuffers[2] = RTHandles.Alloc(Vector2.one, TextureXR.slices, colorFormat: GraphicsFormat.R8G8B8A8_UNorm, dimension: TextureXR.dimension, useDynamicScale: true, name: string.Format("GBuffer{0}", 1), enableRandomWrite: false);

            for (var i = 0;i<m_GBufferCount;i++)
                m_GBufferRTIDs[i] = m_GBuffers[i].nameID;
            
            m_DepthBuffer = RTHandles.Alloc(Vector2.one, TextureXR.slices, DepthBits.Depth32, dimension: TextureXR.dimension, useDynamicScale: true, name: "CameraDepthStencil");
            m_DepthBufferRTID = m_DepthBuffer.nameID;
        }

        void DestroyGBuffers()
        {
            for (var i = 0; i < m_GBufferCount; i++)
            {
                RTHandles.Release(m_GBuffers[i]);
                m_GBuffers[i] = null;
            }
            RTHandles.Release(m_DepthBuffer);
            m_DepthBuffer = null;

        }
        protected override void Dispose(bool disposing)
        {
            DestroyGBuffers();
        }

    }
    
    
}
