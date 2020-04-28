Shader "CustomSRP/DeferredLighting"
{
    SubShader
    {
        Tags{"RenderType" = "Opaque" "RenderPipeline" = ""}
        LOD 100

        Pass
        {
            Name "DeferredLighting"

            Blend One One
            ZTest Always
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM
            #pragma exclude_renderers gles d3d11_9x

            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.render-pipelines.custom/ShaderLibrary/Core.hlsl"
            
            TEXTURE2D(_GBufferAlbedo);
            TEXTURE2D(_GBufferNormal);
            TEXTURE2D(_GBufferMetallicOcclusionSmoothness); //R Metallic,G Occlusion, B None, A Smoothness
            TEXTURE2D(_CameraDepth);
            SAMPLER(sampler_GBufferAlbedo);
            SAMPLER(sampler_GBufferNormal);
            SAMPLER(sampler_GBufferMetallicOcclusionSmoothness);
            SAMPLER(sampler_CameraDepth);
            
            CBUFFER_START(UnityPerMaterial)
            float4 _GBufferAlbedo_ST;
            CBUFFER_END
            
            
            float4 Vertex(float4 vertexPosition : POSITION) : SV_POSITION
            {
                return vertexPosition;
            }
            
            half4 Fragment(float4 pos : SV_POSITION) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_GBufferAlbedo,sampler_GBufferAlbedo, pos).rgb;
                float3 normal = SAMPLE_TEXTURE2D(_GBufferNormal,sampler_GBufferNormal, pos).rgb;
                half4 mgs = SAMPLE_TEXTURE2D(_GBufferMetallicOcclusionSmoothness, sampler_GBufferMetallicOcclusionSmoothness, pos);
                /*half4 specRoughness = UNITY_READ_FRAMEBUFFER_INPUT(1, pos);
                half3 normalWS = normalize((UNITY_READ_FRAMEBUFFER_INPUT(2, pos).rgb * 2.0h - 1.0h));
                float depth = UNITY_READ_FRAMEBUFFER_INPUT(3, pos).r;

                float2 positionNDC = pos.xy * _ScreenSize.zw;
                float3 positionWS = ComputeWorldSpacePosition(positionNDC, depth, UNITY_MATRIX_I_VP);

                half3 viewDirection = half3(normalize(GetCameraPositionWS() - positionWS));

                Light mainLight = GetMainLight();
                half3 specular = specRoughness.rgb;
                half roughness = specRoughness.a;

                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 radiance = mainLight.color * (mainLight.attenuation * NdotL);
                half reflectance = BDRF(roughness, normalWS, mainLight.direction, viewDirection);
                half3 color = (albedo + specular * reflectance) * radiance;*/
                return half4(-pos);
            }
            
            ENDHLSL
        }
    }
}
