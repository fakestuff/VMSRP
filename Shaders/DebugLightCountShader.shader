Shader "Hidden/DebugLightCountShader"
{
    SubShader
    {
        Tags{"RenderType" = "Opaque" "RenderPipeline" = ""}
        LOD 100

        Pass
        {
            Name "TileCountDebug"

            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM
            #pragma exclude_renderers gles d3d11_9x

            #pragma vertex Vert
            #pragma fragment Fragment
            
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            
            TEXTURE2D(_NumberChart);
            SAMPLER(sampler_NumberChart);
            float4x4 unity_MatrixInvVP;
            float4 tileInfo; //tileSizeX, tileSizeY, 1/tileSizeX, 1/tileSizeY
            float4 _ScreenSize; //width, height
            //float4 _TileSize;
            int _TileCountX; // x, y, z
            int _TileCountY;
            
            
            struct LightData
            {
                float4 position;
                float4 color;
                float4 attenuation;
                float4 spotDirection;
                float4 occlusionProbeChannels;
            };
            //#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
            StructuredBuffer<LightData> LightDataBuffer;
            StructuredBuffer<uint2> TileLightIndicesBuffer;
            StructuredBuffer<uint2> TileLightIndicesMinMaxBuffer;
            /*#else
            float4 _AdditionalLightsPosition[MAX_VISIBLE_LIGHTS];
            half4 _AdditionalLightsColor[MAX_VISIBLE_LIGHTS];
            half4 _AdditionalLightsAttenuation[MAX_VISIBLE_LIGHTS];
            half4 _AdditionalLightsSpotDir[MAX_VISIBLE_LIGHTS];
            half4 _AdditionalLightsOcclusionProbes[MAX_VISIBLE_LIGHTS];
            #endif*/
            StructuredBuffer<int> TileLightCountBuffer;
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionCS    : SV_POSITION;
                float2 uv            : TEXCOORD0;
                //UNITY_VERTEX_OUTPUT_STEREO
            };
            float4x4 _FullscreenProjMat;

            float4 TransformFullscreenMesh(half3 positionOS)
            {
                return mul(_FullscreenProjMat, half4(positionOS, 1));
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                //UNITY_SETUP_INSTANCE_ID(input);
                //UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformFullscreenMesh(input.positionOS.xyz);
             #if UNITY_UV_STARTS_AT_TOP
                input.uv.y = 1-input.uv.y;
             #endif
                
                output.uv = input.uv;
                return output;
            }
            
            float2 NumberToUV(uint num,  float2 tileUV)
            {
                //tileUV.y = 1-tileUV.y;
                return float2(num%10*0.1f,num/10*0.1)+tileUV;
            }
            // screen space uv : v down
            
            // texture space uv  : v up
            half4 Fragment(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv; //start at top left
                //uv.y = (int)(_ScreenSize.y*uv.y/64);
                int2 tileIndex;
                tileIndex = (int2)(_ScreenSize.xy*uv.xy/64);
                uint num = TileLightCountBuffer[tileIndex.x+tileIndex.y*_TileCountX];
                int bufferID = tileIndex.x+tileIndex.y*_TileCountX;
                //int num = tileIndex.x+tileIndex.y;
                float2 tileUV = (_ScreenSize.xy*uv - tileIndex.xy * 64)/_ScreenSize.xy/(float2(64,64)/_ScreenSize.xy/0.1);
                tileUV.y = 0.1-tileUV.y;
                float2 numUV = NumberToUV(num,tileUV);
                //uv.y = (int)(_ScreenSize.y*uv.y%64);
                float3 c = float3(0,0,0);
                //return float4(LightDataBuffer[bufferID].color.xyz,1);
                uint lightIndexStart = TileLightIndicesMinMaxBuffer[bufferID][0];
                uint lightIndexEnd = TileLightIndicesMinMaxBuffer[bufferID][1];
                for (uint i = lightIndexStart;i<=lightIndexEnd;i++)
                {
                    if (TileLightIndicesBuffer[bufferID][i/32] & (0x80000000u >> (i%32)))
                    {
                        c += LightDataBuffer[i].color.xyz;
                        //c += 0.3;
                    }
                }
                //c += TileLightIndicesBuffer[bufferID][0/32] & (0x80000000u >> (0%32));
                //if (num > 0)
                //{
                //    return float4(c,1.0f);
                //}
                //numUV = NumberToUV(lightIndexEnd,tileUV);
                return float4(SAMPLE_TEXTURE2D(_NumberChart, sampler_NumberChart, numUV));
            }
            
            ENDHLSL
        }
    }
}
