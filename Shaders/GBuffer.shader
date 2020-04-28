Shader "CustomSRP/GBuffer"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _MetallicGlossMap("MetallicOcSmoothness", 2D) = "black" {}
        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}
        
//        // Specular vs Metallic workflow
//        [HideInInspector] _WorkflowMode("WorkflowMode", Float) = 1.0
//
//        _Color("Color", Color) = (0.5,0.5,0.5,1)
//        _MainTex("Albedo", 2D) = "white" {}
//
//        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
//
//        _Glossiness("Smoothness", Range(0.0, 1.0)) = 0.5
//        _GlossMapScale("Smoothness Scale", Range(0.0, 1.0)) = 1.0
//        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
//
//        [Gamma] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
//        _MetallicGlossMap("Metallic", 2D) = "white" {}
//
//        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
//        _SpecGlossMap("Specular", 2D) = "white" {}
//
//        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
//        [ToggleOff] _GlossyReflections("Glossy Reflections", Float) = 1.0
//
//        _BumpScale("Scale", Float) = 1.0
//        _BumpMap("Normal Map", 2D) = "bump" {}
//
//        _Parallax("Height Scale", Range(0.005, 0.08)) = 0.02
//        _ParallaxMap("Height Map", 2D) = "black" {}
//
//        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
//        _OcclusionMap("Occlusion", 2D) = "white" {}
//
//        _EmissionColor("Color", Color) = (0,0,0)
//        _EmissionMap("Emission", 2D) = "white" {}
        
        // GBuffer
        /*[HideInInspector] _StencilRefGBuffer("_StencilRefGBuffer", Int) = 2 // StencilLightingUsage.RegularLighting
        [HideInInspector] _StencilWriteMaskGBuffer("_StencilWriteMaskGBuffer", Int) = 3 // StencilMask.Lighting

        // Blending state
        [HideInInspector] _Surface("__surface", Float) = 0.0
        [HideInInspector] _Blend("__blend", Float) = 0.0
        [HideInInspector] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _Cull("__cull", Float) = 2.0

        _ReceiveShadows("Receive Shadows", Float) = 1.0*/
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = ""}
        LOD 100
        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "GBuffer" } // This will be only for opaque object based on the RenderQueue index

            /*Blend[_SrcBlend][_DstBlend]
            ZWrite[_ZWrite]
            Cull[_Cull]*/
            
            /*Stencil
            {
                WriteMask [_StencilWriteMaskGBuffer]
                Ref [_StencilRefGBuffer]
                Comp Always
                Pass Replace
            }*/

            HLSLPROGRAM
            // -------------------------------------
            // Material Keywords
            /*#pragma shader_feature _NORMALMAP
            #pragma shader_feature _ALPHATEST_ON
            #pragma shader_feature _ALPHAPREMULTIPLY_ON
            #pragma shader_feature _EMISSION
            #pragma shader_feature _METALLICSPECGLOSSMAP
            #pragma shader_feature _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature _OCCLUSIONMAP

            #pragma shader_feature _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature _GLOSSYREFLECTIONS_OFF
            #pragma shader_feature _SPECULAR_SETUP
            #pragma shader_feature _RECEIVE_SHADOWS_OFF

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON*/

            //--------------------------------------
            // GPU Instancing
            //#pragma multi_compile_instancing
        
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.render-pipelines.custom/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            //#include "LWRP/ShaderLibrary/InputSurfacePBR.hlsl"
            //#include "LWRP/ShaderLibrary/LightweightPassLit.hlsl"
            
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);
            SAMPLER(sampler_MetallicGlossMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            CBUFFER_END
            
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 texcoord     : TEXCOORD0;
                float2 lightmapUV   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float2 uv                       : TEXCOORD0;
                //DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 1);
                float4 normalWS                 : TEXCOORD3;    // xyz: normal, w: viewDir.x
                float4 tangentWS                : TEXCOORD4;    // xyz: tangent, w: viewDir.y
                float4 bitangentWS              : TEXCOORD5;    // xyz: bitangent, w: viewDir.z
#ifdef _MAIN_LIGHT_SHADOWS
                float4 shadowCoord              : TEXCOORD7;
#endif
                float4 positionCS               : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            
            
            Varyings Vert (Attributes input)
            {
                Varyings output = (Varyings)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                half3 viewDirWS = GetCameraPositionWS() - vertexInput.positionWS;
    
    
                
                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                
                output.normalWS = half4(normalInput.normalWS, viewDirWS.x);
                output.tangentWS = half4(normalInput.tangentWS, viewDirWS.y);
                output.bitangentWS = half4(normalInput.bitangentWS, viewDirWS.z);
                
                //OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                //OUTPUT_SH(output.normalWS.xyz, output.vertexSH);
                
#if defined(_MAIN_LIGHT_SHADOWS) && !defined(_RECEIVE_SHADOWS_OFF)
                output.shadowCoord = GetShadowCoord(vertexInput);
#endif
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                
                return output;
            }

            void Frag(Varyings IN,
                out half4 GBuffer0 : SV_Target0,
                out half4 GBuffer1 : SV_Target1,
                out half4 GBuffer2 : SV_Target2)
            //half4 Frag (Varyings IN) : SV_Target
            {
            
                GBuffer0 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                
                // Translate normal into world space
                GBuffer1 = half4(UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale),1.0);
                GBuffer2 = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, IN.uv);
            }
            //{
                //SurfaceData surfaceData;
                //InitializeStandardLitSurfaceData(IN.uv, surfaceData);

                //InputData inputData;
                //InitializeInputData(IN, surfaceData.normalTS, inputData);

                //BRDFData brdfData;
                //InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.alpha, brdfData);

                //GBuffer0 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                //GBuffer1 = half4(brdfData.specular, brdfData.roughness);
                //GBuffer2 = half4(inputData.normalWS * 0.5h + 0.5h, 1.0h);
                //GBuffer3 = half4(GlobalIllumination(brdfData, inputData.bakedGI, surfaceData.occlusion, inputData.normalWS, inputData.viewDirectionWS) + surfaceData.emission, 1.0h);
            //}
            ENDHLSL
        }

        //UsePass "LightweightPipeline/Standard (Physically Based)/Meta"
    }
    //FallBack "Hidden/InternalErrorShader"
    //CustomEditor "LightweightStandardGUI"
}
