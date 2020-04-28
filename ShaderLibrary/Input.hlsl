#ifndef INPUT_HLSL
#define INPUT_HLSL

// TODO: Handle Light type
// LightData
// Shadow LightData
// ShaderType as example
#define MAX_VISIBLE_LIGHTS_SSBO 256
#define MAX_VISIBLE_LIGHTS_UBO  32

//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderTypes.cs.hlsl"

// There are some performance issues by using SSBO in mobile.
// Also some GPUs don't supports SSBO in vertex shader.
#if !defined(SHADER_API_MOBILE) && (defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN) || defined(SHADER_API_PS4) || defined(SHADER_API_XBOXONE))
    #define USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA 0
    #define MAX_VISIBLE_LIGHTS MAX_VISIBLE_LIGHTS_SSBO
// We don't use SSBO in D3D because we can't figure out without adding shader variants if platforms is D3D10.
// We don't use SSBO on Nintendo Switch as UBO path is faster.
// However here we use same limits as SSBO path. 
#elif defined(SHADER_API_D3D11) || defined(SHADER_API_SWITCH)
    #define MAX_VISIBLE_LIGHTS MAX_VISIBLE_LIGHTS_SSBO
    #define USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA 0
// We use less limits for mobile as some mobile GPUs have small SP cache for constants
// Using more than 32 might cause spilling to main memory.
#else
    #define MAX_VISIBLE_LIGHTS MAX_VISIBLE_LIGHTS_UBO
    #define USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA 0
#endif

struct InputData
{
    float3  positionWS;
    half3   normalWS;
    half3   viewDirectionWS;
    float4  shadowCoord;
    half    fogCoord;
    half3   vertexLighting;
    half3   bakedGI;
};
///////////////////////////////////////////////////////////////////////////////
//                      Constant Buffers                                     //
///////////////////////////////////////////////////////////////////////////////

half4 _GlossyEnvironmentColor;
half4 _SubtractiveShadowColor;

float4x4 _InvCameraViewProj;
float4 _ScaledScreenParams;

float4 _MainLightPosition;
half4 _MainLightColor;

half4 _AdditionalLightsCount;

#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
StructuredBuffer<LightData> _AdditionalLightsBuffer;
StructuredBuffer<int> _AdditionalLightsIndices;
#else
float4 _AdditionalLightsPosition[MAX_VISIBLE_LIGHTS];
half4 _AdditionalLightsColor[MAX_VISIBLE_LIGHTS];
half4 _AdditionalLightsAttenuation[MAX_VISIBLE_LIGHTS];
half4 _AdditionalLightsSpotDir[MAX_VISIBLE_LIGHTS];
half4 _AdditionalLightsOcclusionProbes[MAX_VISIBLE_LIGHTS];
#endif


#endif