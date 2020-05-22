Shader "CustomSRP/DeferredLighting"
{
    SubShader
    {
        Tags{"RenderType" = "Opaque" "RenderPipeline" = ""}
        LOD 100

        Pass
        {
            Name "DeferredLighting"

            Blend One Zero
            ZTest Always
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM
            #pragma exclude_renderers gles d3d11_9x

            #pragma vertex Vert
            #pragma fragment Fragment
            
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.render-pipelines.custom/ShaderLibrary/Core.hlsl"
            #include "Packages/com.render-pipelines.custom/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.render-pipelines.custom/ShaderLibrary/Lighting.hlsl"
            #define UNITY_USE_NATIVE_HDR 1
            
            TEXTURE2D(_GBufferAlbedo);
            TEXTURE2D(_GBufferNormal);
            TEXTURE2D(_GBufferMetallicOcclusionSmoothness); //R Metallic,G Occlusion, B None, A Smoothness
            TEXTURE2D(_GBufferDepth);
            SAMPLER(sampler_GBufferAlbedo);
            SAMPLER(sampler_GBufferNormal);
            SAMPLER(sampler_GBufferMetallicOcclusionSmoothness);
            SAMPLER(sampler_GBufferDepth);
            
            CBUFFER_START(UnityPerMaterial)
            float4 _GBufferAlbedo_ST;
            CBUFFER_END
            float4x4 unity_MatrixInvVP;
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionCS    : SV_POSITION;
                float2 uv            : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            float4x4 _FullscreenProjMat;

            float4 TransformFullscreenMesh(half3 positionOS)
            {
                return mul(_FullscreenProjMat, half4(positionOS, 1));
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformFullscreenMesh(input.positionOS.xyz);
             #if UNITY_UV_STARTS_AT_TOP
                input.uv.y = 1-input.uv.y;
             #endif
                output.uv = input.uv;
                return output;
            }
            // Material Data
            inline void InitializeDeferredLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
            {
                half4 albedo = SAMPLE_TEXTURE2D(_GBufferAlbedo,sampler_GBufferAlbedo, uv);
                half4 specGloss = SAMPLE_TEXTURE2D(_GBufferMetallicOcclusionSmoothness, sampler_GBufferMetallicOcclusionSmoothness, uv);
                //outSurfaceData.alpha = albedoAlpha.a;//Alpha(albedoAlpha.a, _BaseColor, _Cutoff);
                outSurfaceData.alpha = 1.0h;// Deferred do not require alpha value
                outSurfaceData.albedo = albedo.rgb;
                outSurfaceData.metallic = specGloss.r;
                outSurfaceData.specular = half3(0.0h, 0.0h, 0.0h);
            
                outSurfaceData.smoothness = specGloss.a;
                outSurfaceData.normalTS = half4(0,0,0,0); // not used anyway
                outSurfaceData.occlusion = specGloss.g;
                outSurfaceData.emission  = half4(0,0,0,0);//
                //outSurfaceData.emission = SampleEmission(uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap)); // add this later
            }
            
            void InitializeDeferredInputData(Varyings input, float2 uv, out InputData inputData)
            {
                inputData = (InputData)0;
                float depth = SAMPLE_TEXTURE2D(_GBufferDepth, sampler_GBufferDepth, uv).r;
                float2 positionNDC = uv.xy;// / _ScreenSize.zw; // BUG!!!!!
                float3 positionWS = ComputeWorldSpacePosition(positionNDC, depth, unity_MatrixInvVP);
                inputData.positionWS = float3(positionWS);
                //inputData.positionWS = positionWS;
                inputData.normalWS = SafeNormalize(SAMPLE_TEXTURE2D(_GBufferNormal, sampler_GBufferNormal, uv));
                //inputData.normalWS = NormalizeNormalPerPixel(SAMPLE_TEXTURE2D(_GBufferNormal, sampler_GBufferNormal, uv));
                float3 viewDirWS = SafeNormalize((GetCameraPositionWS() - positionWS));
            
                inputData.viewDirectionWS = viewDirWS;
            //#if defined(_MAIN_LIGHT_SHADOWS) && !defined(_RECEIVE_SHADOWS_OFF)
            //    inputData.shadowCoord = input.shadowCoord;
            //#else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            //#endif
                //inputData.fogCoord = input.fogFactorAndVertexLight.x;
                //inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
                //inputData.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, inputData.normalWS);
            }
            
            half4 Fragment(Varyings IN) : SV_Target
            {
                
                // Albedo Alpha is used for material Mask
                // use stnecil to 
                /*half materialID = albedo.a;
                if (materialID > 0.01)
                {
                    return half4(0,0,0,0);
                }*/
                
                
                SurfaceData surfaceData;
                InitializeDeferredLitSurfaceData(IN.uv, surfaceData);
               
                InputData inputData;
                InitializeDeferredInputData(IN, IN.uv, inputData);
                
                //float depth = SAMPLE_TEXTURE2D(_GBufferDepth, sampler_GBufferDepth, IN.uv).r;
                //return half4(ComputeWorldSpacePosition(IN.uv, depth, unity_MatrixInvVP),0);
                //return half4(IN.uv,0,1);
                //return half4(inputData.viewDirectionWS,1.0);
                half4 color = UniversalFragmentPBR(inputData, surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.occlusion, surfaceData.emission, surfaceData.alpha);
                return color;
            }
            
            ENDHLSL
        }
    }
}
