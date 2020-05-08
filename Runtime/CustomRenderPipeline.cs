using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.CustomRenderPipeline
{
    
    public sealed class CustomRenderPipeline : RenderPipeline
    {
        static Mesh s_FullscreenMesh = null;

        /// <summary>
        /// Returns a mesh that you can use with <see cref="CommandBuffer.DrawMesh(Mesh, Matrix4x4, Material)"/> to render full-screen effects.
        /// </summary>
        public static Mesh fullscreenMesh
        {
            get
            {
                if (s_FullscreenMesh != null)
                    return s_FullscreenMesh;

                float topV = 1.0f;
                float bottomV = 0.0f;

                s_FullscreenMesh = new Mesh { name = "Fullscreen Quad" };
                s_FullscreenMesh.SetVertices(new List<Vector3>
                {
                    new Vector3(-1.0f, -1.0f, 0.0f),
                    new Vector3(-1.0f,  1.0f, 0.0f),
                    new Vector3(1.0f, -1.0f, 0.0f),
                    new Vector3(1.0f,  1.0f, 0.0f)
                });

                s_FullscreenMesh.SetUVs(0, new List<Vector2>
                {
                    new Vector2(0.0f, bottomV),
                    new Vector2(0.0f, topV),
                    new Vector2(1.0f, bottomV),
                    new Vector2(1.0f, topV)
                });

                s_FullscreenMesh.SetIndices(new[] { 0, 1, 2, 2, 1, 3 }, MeshTopology.Triangles, 0, false);
                s_FullscreenMesh.UploadMeshData(true);
                return s_FullscreenMesh;
            }
        }
        private RTHandle[] m_GBuffers;
        private RenderTargetIdentifier[] m_GBufferRTIDs;
        private RTHandle m_ColorBuffer;
        private RenderTargetIdentifier m_ColorBufferRTID;
        private RTHandle m_DepthBuffer;
        private RenderTargetIdentifier m_DepthBufferRTID;
        private int m_GBufferCount = 3;
        private MSAASamples m_MSAASample = MSAASamples.None;

        private Shader m_DeferredLitShader;
        private Material m_DeferredLightingMat;
        MaterialPropertyBlock m_LightPropertiesBlock = new MaterialPropertyBlock();
        public CustomRenderPipeline(CustomRenderPipelineAsset asset)
        {
            RTHandles.Initialize(1, 1, false, MSAASamples.None);
            CreateSharedBuffer();
            CreateGBuffers();
            m_DeferredLightingMat = CoreUtils.CreateEngineMaterial(Shader.Find("CustomSRP/DeferredLighting"));
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

            

            

            DeferredLightPass(context, camera);
            cmd = CommandBufferPool.Get("FinalBlit");
            cmd.Blit(m_ColorBufferRTID, BuiltinRenderTextureType.CameraTarget);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            // Submit commands to GPU. Up to this point all commands have been enqueued in the context.
            // Several submits can be done in a frame to better controls CPU/GPU workload.
            context.Submit();
        }

        void DeferredLightPass(ScriptableRenderContext context, Camera camera)
        {
            var cmd = CommandBufferPool.Get("DeferredLightPass");
            // Bind buffers
            cmd.SetGlobalTexture("_GBufferAlbedo", m_GBufferRTIDs[0]);
            cmd.SetGlobalTexture("_GBufferNormal", m_GBufferRTIDs[1]);
            cmd.SetGlobalTexture("_GBufferMetallicOcclusionSmoothness", m_GBufferRTIDs[2]);
            cmd.SetGlobalTexture("_GBufferDepth", m_DepthBufferRTID);
            //Set RenderTarget
            cmd.SetRenderTarget(m_ColorBuffer,RenderBufferLoadAction.DontCare,RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(true,true,Color.black,0.0f);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.SetViewport(camera.pixelRect);
            cmd.DrawMesh(CustomRenderPipeline.fullscreenMesh, Matrix4x4.identity, m_DeferredLightingMat, 0, 0);
            //cmd.SetViewport(new Rect(0,0,camera.scaledPixelWidth, camera.scaledPixelHeight));
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        void CreateGBuffers()
        {
            m_GBuffers = new RTHandle[m_GBufferCount];
            m_GBufferRTIDs = new RenderTargetIdentifier[m_GBufferCount];
            m_GBuffers[0] = RTHandles.Alloc(Vector2.one,  colorFormat: GraphicsFormat.R8G8B8A8_UNorm, dimension:TextureDimension.Tex2D, useDynamicScale: true, name: "Albedo", enableRandomWrite: false);
            m_GBuffers[1] = RTHandles.Alloc(Vector2.one,  colorFormat: GraphicsFormat.R8G8B8A8_UNorm, dimension: TextureDimension.Tex2D, useDynamicScale: true, name: "Normal", enableRandomWrite: false);
            m_GBuffers[2] = RTHandles.Alloc(Vector2.one,  colorFormat: GraphicsFormat.R8G8B8A8_UNorm, dimension: TextureDimension.Tex2D, useDynamicScale: true, name: "MSO", enableRandomWrite: false);

            for (var i = 0;i<m_GBufferCount;i++)
                m_GBufferRTIDs[i] = m_GBuffers[i].nameID;
        }

        void CreateSharedBuffer()
        {
            m_ColorBuffer = RTHandles.Alloc(Vector2.one, TextureXR.slices, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureDimension.Tex2D, useDynamicScale: true, name: "CameraHDRColor", enableRandomWrite: false);
            m_DepthBuffer = RTHandles.Alloc(Vector2.one, TextureXR.slices, DepthBits.Depth32, dimension: TextureDimension.Tex2D, useDynamicScale: true, name: "CameraDepthStencil");
            m_ColorBufferRTID = m_ColorBuffer.nameID;
            m_DepthBufferRTID = m_DepthBuffer.nameID;
        }

        void DestroyGBuffers()
        {
            for (var i = 0; i < m_GBufferCount; i++)
            {
                RTHandles.Release(m_GBuffers[i]);
                m_GBuffers[i] = null;
            }
        }

        void DestroySharedBuffers()
        {
            RTHandles.Release(m_ColorBuffer);
            RTHandles.Release(m_DepthBuffer);
            m_ColorBuffer = null;
            m_DepthBuffer = null;
        }
        protected override void Dispose(bool disposing)
        {
            DestroyGBuffers();
            DestroySharedBuffers();
        }

    }
    
    
}
