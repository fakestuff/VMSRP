Shader "CustomSRP/Lit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1.0, 1.0, 1.0, 1.0)
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
        [HideInInspector] _StencilRefGBuffer("_StencilRefGBuffer", Int) = 2 // StencilLightingUsage.RegularLighting
        [HideInInspector] _StencilWriteMaskGBuffer("_StencilWriteMaskGBuffer", Int) = 3 // StencilMask.Lighting

        // Blending state
        [HideInInspector] _Surface("__surface", Float) = 0.0
        [HideInInspector] _Blend("__blend", Float) = 0.0
        [HideInInspector] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _Cull("__cull", Float) = 2.0

        _ReceiveShadows("Receive Shadows", Float) = 1.0
    }

    SubShader
    {
        Tags{"RenderPipeline" = "CustomSRP"}

        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "GBuffer" } // This will be only for opaque object based on the RenderQueue index

            Blend[_SrcBlend][_DstBlend]
            ZWrite[_ZWrite]
            Cull[_Cull]
            
            Stencil
            {
                WriteMask [_StencilWriteMaskGBuffer]
                Ref [_StencilRefGBuffer]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            // -------------------------------------
            // Material Keywords
            #pragma shader_feature _NORMALMAP
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
            #pragma multi_compile _ LIGHTMAP_ON

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
        
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.render-pipelines.custom/ShaderLibrary/Core.hlsl"
            //#include "LWRP/ShaderLibrary/InputSurfacePBR.hlsl"
            //#include "LWRP/ShaderLibrary/LightweightPassLit.hlsl"
            
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            CBUFFER_END
            
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };
            
            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float4 positionCS : SV_POSITION;
            };
            
            Varyings Vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            void Frag(Varyings IN,
                out half4 GBuffer0 : SV_Target0,
                out half4 GBuffer1 : SV_Target1,
                out half4 GBuffer2 : SV_Target2)
            {
                //SurfaceData surfaceData;
                //InitializeStandardLitSurfaceData(IN.uv, surfaceData);

                //InputData inputData;
                //InitializeInputData(IN, surfaceData.normalTS, inputData);

                //BRDFData brdfData;
                //InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.alpha, brdfData);

                GBuffer0 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                //GBuffer1 = half4(brdfData.specular, brdfData.roughness);
                //GBuffer2 = half4(inputData.normalWS * 0.5h + 0.5h, 1.0h);
                //GBuffer3 = half4(GlobalIllumination(brdfData, inputData.bakedGI, surfaceData.occlusion, inputData.normalWS, inputData.viewDirectionWS) + surfaceData.emission, 1.0h);
            }
            ENDHLSL
        }

        //UsePass "LightweightPipeline/Standard (Physically Based)/Meta"
    }
    FallBack "Hidden/InternalErrorShader"
    //CustomEditor "LightweightStandardGUI"
}
