using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using Unity.Collections;
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

        //private Shader m_DeferredLitShader;
        private Material m_DeferredLightingMat;
        private Material m_DebugLightCountMat;
        private Texture2D m_NumberCharts;
        private bool m_DebugTileCount;
        
        
        private LightCullingPass m_LightCullingPass;
        MaterialPropertyBlock m_LightPropertiesBlock = new MaterialPropertyBlock();
        public CustomRenderPipeline(CustomRenderPipelineAsset asset)
        {
            RTHandles.Initialize(1, 1, false, MSAASamples.None);
            CreateSharedBuffer();
            CreateGBuffers();
            m_LightCullingPass = new LightCullingPass();
            m_DeferredLightingMat = CoreUtils.CreateEngineMaterial(Shader.Find("CustomSRP/DeferredLighting"));
            m_DebugLightCountMat = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/DebugLightCountShader"));
            m_NumberCharts = asset.NumberChartTexture;
            m_DebugTileCount = asset.DebugTileCount;
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
            //RTHandles.SetReferenceSize(maxWidth,maxHeight,m_MSAASample);
            RTHandles.SetReferenceSize(1920,1080,m_MSAASample);
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
            DrawingSettings opaqueDrawingSettings = new DrawingSettings(ShaderPassTag.Forward, opaqueSortingSettings);
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
            
            
            
            

            
            m_LightCullingPass.Execute(context,cullingResults,camera);
            DeferredLightPass(context, cullingResults, camera);
            //context.DrawSkybox(camera);
            cmd = CommandBufferPool.Get("FinalBlit");
            cmd.Blit(m_ColorBufferRTID, BuiltinRenderTextureType.CameraTarget);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            
            //
            if (m_DebugTileCount)
                DebugPass(context, cullingResults, camera);
            // Submit commands to GPU. Up to this point all commands have been enqueued in the context.
            // Several submits can be done in a frame to better controls CPU/GPU workload.
            context.Submit();
        }

        void DeferredLightPass(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
        {
            var cmd = CommandBufferPool.Get("SetupGlobalLights");
            
            SortingSettings opaqueSortingSettings = new SortingSettings(camera);
            opaqueSortingSettings.criteria = SortingCriteria.CommonOpaque;
            FilteringSettings opaqueFilteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            if (cullingResults.visibleLights.Length > 0)
            {
                VisibleLight lightData = cullingResults.visibleLights[0];
                Vector4 dir = -lightData.localToWorldMatrix.GetColumn(2);
                Vector4 lightPos = new Vector4(dir.x, dir.y, dir.z, 0.0f);
                cmd.SetGlobalVector("_MainLightPosition", lightPos);
                cmd.SetGlobalVector("_MainLightColor", lightData.finalColor);
            }
            if ( cullingResults.visibleReflectionProbes.Length>0)
                cmd.SetGlobalTexture("unity_SpecCube0", cullingResults.visibleReflectionProbes[0].texture);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            bool useRenderPass = false;
            if (!useRenderPass)
            {
                bool enableDynamicBatching = false;
                bool enableInstancing = false;
                PerObjectData perObjectData = PerObjectData.None;
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
                context.DrawRenderers(cullingResults, ref gBufferDrawingSettings, ref opaqueFilteringSettings);
                cmd = CommandBufferPool.Get("DeferredLightingPass");
                cmd.SetGlobalVector("unity_SpecCube0_HDR", new Vector4(1,1,0,0));
                // Bind buffers
                cmd.SetGlobalTexture("_GBufferAlbedo", m_GBufferRTIDs[0]);
                cmd.SetGlobalTexture("_GBufferNormal", m_GBufferRTIDs[1]);
                cmd.SetGlobalTexture("_GBufferMetallicOcclusionSmoothness", m_GBufferRTIDs[2]);
                cmd.SetGlobalTexture("_GBufferDepth", m_DepthBufferRTID);
                cmd.SetGlobalInt("_TileCountX", (camera.scaledPixelWidth + 64 - 1) / 64);
                cmd.SetGlobalInt("_TileCountY", (camera.scaledPixelHeight + 64 - 1) / 64);
                cmd.SetGlobalVector("unity_LightData", new Vector4(6,0,1,0));
                //Set RenderTarget
                cmd.SetRenderTarget(m_ColorBuffer,RenderBufferLoadAction.DontCare,RenderBufferStoreAction.Store);
                cmd.ClearRenderTarget(true,true,Color.black,0.0f);
                //cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                //cmd.SetViewport(camera.pixelRect);
                cmd.DrawMesh(CustomRenderPipeline.fullscreenMesh, Matrix4x4.identity, m_DeferredLightingMat, 0, 0);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                //cmd.SetViewport(new Rect(0,0,camera.scaledPixelWidth, camera.scaledPixelHeight));
            }
            else
            {
                // var colorBuffer = new AttachmentDescriptor(RenderTextureFormat.ARGBHalf); // 64 bit
                // var depthBuffer = new AttachmentDescriptor(RenderTextureFormat.Depth); // 32 bit
                // var albedoGBuffer = new AttachmentDescriptor(RenderTextureFormat.ARGB32); // 32 bit
                // var normalGBuffer = new AttachmentDescriptor(RenderTextureFormat.ARGBHalf); // 64 bit
                // var pbrGBuffer = new AttachmentDescriptor(RenderTextureFormat.ARGB32); // 32
                // var depthGBuffer = new AttachmentDescriptor(RenderTextureFormat.RFloat); // 32 bit
                //
                // colorBuffer.ConfigureClear(new Color(0.0f, 0.0f, 0.0f, 0.0f), 1.0f, 0);
                // depthBuffer.ConfigureClear(new Color(), 1.0f, 0);
                // colorBuffer.ConfigureTarget(BuiltinRenderTextureType.CameraTarget, true, true);
                //
                // var attachments = new NativeArray<AttachmentDescriptor>(6, Allocator.Temp);
                // const int colorBufferId = 0, depthBufferId = 1, albedoGBufferId = 2,  normalGBufferId = 3, pbrGbufferId = 4, depthGBufferId = 5;
                // //const int colorBufferId = 0,  albedoGBufferId = 2,  normalGBufferId = 3, pbrGbufferId = 4, depthGBufferId = 5;
                // attachments[colorBufferId] = colorBuffer;
                // attachments[depthBufferId] = depthBuffer;
                // attachments[albedoGBufferId] = albedoGBuffer;
                // attachments[normalGBufferId] = normalGBuffer;
                // attachments[pbrGbufferId] = pbrGBuffer;
                // attachments[depthGBufferId] = depthGBuffer;
                //
                // using (context.BeginScopedRenderPass(camera.scaledPixelWidth, camera.scaledPixelHeight, 1, attachments, depthBufferId))
                // {
                //     attachments.Dispose();
                //     var gBuffers = new NativeArray<int>(4, Allocator.Temp);
                //
                //     //gBuffers[0] = colorBufferId;
                //     gBuffers[0] = albedoGBufferId;
                //     gBuffers[1] = normalGBufferId;
                //     gBuffers[2] = pbrGbufferId;
                //     gBuffers[3] = depthGBufferId;
                //     using (context.BeginScopedSubPass(gBuffers))
                //     {
                //         RenderGbufferSubPass(context, cullingResults, camera);
                //     }
                //     var lightingColors = new NativeArray<int>(1, Allocator.Temp);
                //     lightingColors[0] = colorBufferId;
                //     var lightingInputs = new NativeArray<int>(4, Allocator.Temp);
                //     lightingInputs[0] = albedoGBufferId;
                //     //lightingInputs[1] = albedoGBufferId;
                //     lightingInputs[1] = normalGBufferId;
                //     lightingInputs[2] = pbrGbufferId;
                //     lightingInputs[3] = depthGBufferId;
                //     using (context.BeginScopedSubPass(lightingColors, lightingInputs, true))
                //     {
                //         lightingColors.Dispose();
                //         lightingInputs.Dispose();
                //         
                //         // RenderLighting(camera, cullResults, context);
                //         RenderDeferredLightingSubPass(context, cullingResults, camera);
                //     }
                //
                //
                // }
                var albedo = new AttachmentDescriptor(RenderTextureFormat.ARGB32);
                var specRough = new AttachmentDescriptor(RenderTextureFormat.ARGB32);
                var normal = new AttachmentDescriptor(RenderTextureFormat.ARGB2101010);
                var emission = new AttachmentDescriptor(RenderTextureFormat.ARGBHalf);
                var depth = new AttachmentDescriptor(RenderTextureFormat.Depth);
                emission.ConfigureClear(new Color(0.0f, 0.0f, 0.0f, 0.0f), 1.0f, 0);
                depth.ConfigureClear(new Color(), 1.0f, 0);
                albedo.ConfigureTarget(BuiltinRenderTextureType.CameraTarget, false, true);
                var attachments = new NativeArray<AttachmentDescriptor>(5, Allocator.Temp);
                const int depthIndex = 0, albedoIndex = 1, specRoughIndex = 2, normalIndex = 3, emissionIndex = 4;
                attachments[depthIndex] = depth;
                attachments[albedoIndex] = albedo;
                attachments[specRoughIndex] = specRough;
                attachments[normalIndex] = normal;
                attachments[emissionIndex] = emission;
                using (context.BeginScopedRenderPass(camera.pixelWidth, camera.pixelHeight, 1, attachments, depthIndex))
                {
                    attachments.Dispose();

                    // Start the first subpass, GBuffer creation: render to albedo, specRough, normal and emission, no need to read any input attachments
                    var gbufferColors = new NativeArray<int>(4, Allocator.Temp);
                    gbufferColors[0] = albedoIndex;
                    gbufferColors[1] = specRoughIndex;
                    gbufferColors[2] = normalIndex;
                    gbufferColors[3] = emissionIndex;
                    using (context.BeginScopedSubPass(gbufferColors))
                    {
                        gbufferColors.Dispose();

                        // Render the deferred G-Buffer
                        RenderGbufferSubPass(context, cullingResults, camera);
                    }

                    // Second subpass, lighting: Render to the emission buffer, read from albedo, specRough, normal and depth.
                    // The last parameter indicates whether the depth buffer can be bound as read-only.
                    // Note that some renderers (notably iOS Metal) won't allow reading from the depth buffer while it's bound as Z-buffer,
                    // so those renderers should write the Z into an additional FP32 render target manually in the pixel shader and read from it instead
                    var lightingColors = new NativeArray<int>(1, Allocator.Temp);
                    lightingColors[0] = emissionIndex;
                    var lightingInputs = new NativeArray<int>(4, Allocator.Temp);
                    lightingInputs[0] = albedoIndex;
                    lightingInputs[1] = specRoughIndex;
                    lightingInputs[2] = normalIndex;
                    lightingInputs[3] = depthIndex;
                    using (context.BeginScopedSubPass(lightingColors, lightingInputs, true))
                    {
                        lightingColors.Dispose();
                        lightingInputs.Dispose();
                    
                        // PushGlobalShadowParams(context);
                        RenderDeferredLightingSubPass(context, cullingResults, camera);
                    }

                    // Third subpass, tonemapping: Render to albedo (which is bound to the camera target), read from emission.
                    // var tonemappingColors = new NativeArray<int>(1, Allocator.Temp);
                    // tonemappingColors[0] = albedoIndex;
                    // var tonemappingInputs = new NativeArray<int>(1, Allocator.Temp);
                    // tonemappingInputs[0] = emissionIndex;
                    // using (context.BeginScopedSubPass(tonemappingColors, tonemappingInputs, true))
                    // {
                    //     tonemappingColors.Dispose();
                    //     tonemappingInputs.Dispose();
                    //
                    //     // present frame buffer.
                    //     // FinalPass(context);
                    // }
                }
            }
        }

        void RenderGbufferSubPass(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
        {
            bool enableDynamicBatching = false;
            bool enableInstancing = false;
            SortingSettings opaqueSortingSettings = new SortingSettings(camera);
            PerObjectData perObjectData = PerObjectData.None;
            // DrawGBuffers
            DrawingSettings gBufferDrawingSettings = new DrawingSettings(ShaderPassTag.GBuffer, opaqueSortingSettings);
            FilteringSettings opaqueFilteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            
            gBufferDrawingSettings.enableDynamicBatching = enableDynamicBatching;
            gBufferDrawingSettings.enableInstancing = enableInstancing;
            gBufferDrawingSettings.perObjectData = perObjectData;
            var cmd = CommandBufferPool.Get("GbufferSubpass");
            cmd.SetRenderTarget(m_GBufferRTIDs,m_DepthBufferRTID);
            cmd.ClearRenderTarget(true, true, camera.backgroundColor);
            
            //CoreUtils.SetRenderTarget(cmd, m_GBufferRTIDs, m_DepthBufferRTID, ClearFlag.All);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            context.DrawRenderers(cullingResults, ref gBufferDrawingSettings, ref opaqueFilteringSettings);
        }

        void RenderDeferredLightingSubPass(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
        {
            var cmd = CommandBufferPool.Get("DeferredLightingPass");
            cmd.SetGlobalVector("unity_SpecCube0_HDR", new Vector4(1,1,0,0));
            // Bind buffers
            cmd.SetGlobalTexture("_GBufferAlbedo", m_GBufferRTIDs[0]);
            cmd.SetGlobalTexture("_GBufferNormal", m_GBufferRTIDs[1]);
            cmd.SetGlobalTexture("_GBufferMetallicOcclusionSmoothness", m_GBufferRTIDs[2]);
            cmd.SetGlobalTexture("_GBufferDepth", m_DepthBufferRTID);
            cmd.SetGlobalInt("_TileCountX", (camera.scaledPixelWidth + 64 - 1) / 64);
            cmd.SetGlobalInt("_TileCountY", (camera.scaledPixelHeight + 64 - 1) / 64);
            cmd.SetGlobalVector("unity_LightData", new Vector4(6,0,1,0));
            //Set RenderTarget
            cmd.SetRenderTarget(m_ColorBuffer,RenderBufferLoadAction.DontCare,RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(true,true,Color.black,0.0f);
            //cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            //cmd.SetViewport(camera.pixelRect);
            cmd.DrawMesh(CustomRenderPipeline.fullscreenMesh, Matrix4x4.identity, m_DeferredLightingMat, 0, 0);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        void DebugPass(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
        {
            ShaderBindings.SetPerCameraShaderVariables(context, camera, false);
            var cmd = CommandBufferPool.Get("DebugLightDraw");
            cmd.SetGlobalTexture("_NumberChart",m_NumberCharts);
            cmd.DrawMesh(CustomRenderPipeline.fullscreenMesh, Matrix4x4.identity, m_DebugLightCountMat, 0, 0);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        void CreateGBuffers()
        {
            m_GBuffers = new RTHandle[m_GBufferCount];
            m_GBufferRTIDs = new RenderTargetIdentifier[m_GBufferCount];
            m_GBuffers[0] = RTHandles.Alloc(Vector2.one,  colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension:TextureDimension.Tex2D, useDynamicScale: true, name: "Albedo", enableRandomWrite: false);
            m_GBuffers[1] = RTHandles.Alloc(Vector2.one,  colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureDimension.Tex2D, useDynamicScale: true, name: "Normal", enableRandomWrite: false);
            m_GBuffers[2] = RTHandles.Alloc(Vector2.one,  colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureDimension.Tex2D, useDynamicScale: true, name: "MSO", enableRandomWrite: false);

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
            m_LightCullingPass.Dispose();
            DestroyGBuffers();
            DestroySharedBuffers();
        }

    }
    
    
}
