using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Resources;
using UnityEngine.Rendering;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using static Unity.Mathematics.math;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;
using uint2 = Unity.Mathematics.uint2;

namespace UnityEngine.Rendering.CustomRenderPipeline
{
    
    
    
    
    public class LightViewSpaceDistanceComparer : IComparer<VisibleLight>
    {
        public Camera ViewCamera { get; set; }
        public Vector3 CameraForward { get; set; }
        // Call CaseInsensitiveComparer.Compare with the parameters reversed.
        public int Compare(VisibleLight x, VisibleLight y)
        {
            //try to switch to 
            Vector3 xDepthMin = ViewCamera.WorldToScreenPoint(x.light.transform.position - CameraForward*x.light.range);
            Vector3 yDepthMin = ViewCamera.WorldToScreenPoint(y.light.transform.position - CameraForward*y.light.range);
            return xDepthMin.z<yDepthMin.z? -1:1; // screen space depth
        }
    }

    
    public struct TileLightIndex
    { 
        uint2 IndexBitArry; // support up to 32 light per tile
        //high end
        //1000000000...00000 -> 1
        //0100000000...00000 -> 2
        public void Reset()
        {
            IndexBitArry = 0;
        }
        public TileLightIndex SetIndex(int id)
        {
            IndexBitArry[id / 32] |= 0x80000000u >> (id%32);

            return this;
        }

        bool GetIndex(int id)
        {
            return (IndexBitArry[id/32] & 0x80000000u >> (id%32)) != 0u;
        }
        
        
    }

    public struct TileInfo
    {
        public int2 BigTileSize;
        public int2 BigTileCount;
        public int2 SmallTileSize;
        public int2 SmallTileCount;
    }

    public struct ZBinningLightIndexRange
    {
        private int4 m_Index; // waste 2 word to do padding

        public void Reset()
        {
            m_Index.x = Int32.MinValue; // in shader check if m_Index.x < 0, if true then it's a 
            m_Index.y = Int32.MaxValue;
        }
    }
    
    public struct ClusterInfo
    {
        
        public NativeArray<float3> ClusterNormal; // native container or not
        public NativeArray<float3> ClusterXPlaneNormal; // cluster normal in x-right direction
        public NativeArray<float3> ClusterYPlaneNormal; // cluster normal in y-down direction
        public NativeArray<float3> ClusterZPlaneNormal; // cluster normal in x direction
        public NativeArray<float3> ClusterXPlanePoints;
        public NativeArray<float3> ClusterYPlanePoints;
        public NativeArray<float3> ClusterZPlanePoints;
        public NativeArray<float3> FrustumBoundingPoints; // 4*x+2*y+z

        public void Update(int3 tileCount, Camera camera)
        {
            NewOrReallocate(tileCount);
            FillNormalAndPoints(tileCount, camera);
        }

        void NewOrReallocate(int3 tileCount)
        {

            ClusterNormal = new NativeArray<float3>(3,Allocator.TempJob);
            FrustumBoundingPoints = new NativeArray<float3>(8, Allocator.TempJob);
            ClusterXPlanePoints = new NativeArray<float3>(tileCount.x+1, Allocator.TempJob);
            ClusterYPlanePoints = new NativeArray<float3>(tileCount.y+1, Allocator.TempJob);
            ClusterZPlanePoints = new NativeArray<float3>(tileCount.z+1, Allocator.TempJob);
            
            ClusterXPlaneNormal = new NativeArray<float3>(tileCount.x+1, Allocator.TempJob);
            ClusterYPlaneNormal = new NativeArray<float3>(tileCount.y+1, Allocator.TempJob);
            ClusterZPlaneNormal = new NativeArray<float3>(tileCount.z+1, Allocator.TempJob);
        }

        int XYZToIndex(int x, int y, int z)
        {
            return x * 4 + y * 2 + z;
        }

        void FillNormalAndPoints(int3 tileCount, Camera camera)
        {
            // generate 8 3d points and then lerp
            // try lerping with SIMD
            
           
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        FrustumBoundingPoints[x*4+ 2*y+z] =
                            camera.ScreenToWorldPoint(new Vector3(x*camera.scaledPixelWidth, (1-y)*camera.scaledPixelHeight, z == 0 ? camera.nearClipPlane : camera.farClipPlane));
                    }
                }
            }
            
            // x，就是在x方向分割cluster的平面
            //[x][tileIndexX]
            //[y][tileIndexY]
            //[z][tileIndexZ]

            int ccc = 0;

            float3 clusterFwdStep = (FrustumBoundingPoints[XYZToIndex(0,0,1)]-FrustumBoundingPoints[0])/tileCount.z;
            float3 clusterRightStep = (FrustumBoundingPoints[XYZToIndex(1,0,0)]-FrustumBoundingPoints[0])/tileCount.x;
            float3 clusterDownStep = (FrustumBoundingPoints[XYZToIndex(0,1,0)]-FrustumBoundingPoints[0])/tileCount.y;
            
            //  x,  y,  z
            ClusterNormal[0] = normalize(clusterRightStep);
            ClusterNormal[1] = normalize(clusterDownStep);
            ClusterNormal[2] = normalize(clusterFwdStep);
            
            for (int x = 0; x <= tileCount.x; x++)
            {
                //ClusterPoints[0].ReinterpretStore(x,FrustumBoundingPoints[0, 0, 0] + clusterRightStep * x);
                ClusterXPlanePoints[x] = FrustumBoundingPoints[0] + clusterRightStep * x;
                ClusterXPlanePoints[x] = lerp(FrustumBoundingPoints[0], FrustumBoundingPoints[XYZToIndex(1, 0, 0)],
                    (float)x / tileCount.x);
            }

            for (int y = 0; y <= tileCount.y; y++)
            {
                //ClusterPoints[1].ReinterpretStore(y,FrustumBoundingPoints[0, 0, 0] + clusterDownStep * y);
                ClusterYPlanePoints[y] = FrustumBoundingPoints[0] + clusterDownStep * y;
            }

            for (int z = 0; z <= tileCount.z; z++)
            {
                //ClusterPoints[1].ReinterpretStore(z,FrustumBoundingPoints[0, 0, 0] + clusterFwdStep * z);
                ClusterZPlanePoints[z] = FrustumBoundingPoints[0] + clusterFwdStep * z;
            }

            for (int x = 0; x <= tileCount.x; x++)
            {
                float3 a = lerp(FrustumBoundingPoints[XYZToIndex(0,0,1)],FrustumBoundingPoints[XYZToIndex(1,0,1)],(float)x/tileCount.x);
                float3 b = lerp(FrustumBoundingPoints[XYZToIndex(0,0,0)],FrustumBoundingPoints[XYZToIndex(1,0,0)],(float)x/tileCount.x);
                float3 c = lerp(FrustumBoundingPoints[XYZToIndex(0,1,0)],FrustumBoundingPoints[XYZToIndex(1,1,0)],(float)x/tileCount.x);
                float3 down = c-b;
                float3 fwd = a - b;
                float3 xNormal = cross(normalize(fwd),normalize(down));
                ClusterXPlaneNormal[x] = normalize(xNormal);
            }
            for (int y = 0; y <= tileCount.y; y++)
            {
                float3 a = lerp(FrustumBoundingPoints[XYZToIndex(0,0,1)],FrustumBoundingPoints[XYZToIndex(0,1,1)],(float)y/tileCount.y);
                float3 b = lerp(FrustumBoundingPoints[XYZToIndex(0,0,0)],FrustumBoundingPoints[XYZToIndex(0,1,0)],(float)y/tileCount.y);
                float3 c = lerp(FrustumBoundingPoints[XYZToIndex(1,0,0)],FrustumBoundingPoints[XYZToIndex(1,1,0)],(float)y/tileCount.y);
                float3 fwd = a - b;
                float3 right = c - b;
                float3 yNormal = cross(normalize(right),normalize(fwd));
                ClusterYPlaneNormal[y] = normalize(yNormal);
            }

            return;
        }

        public void Dispose()
        {
            ClusterXPlanePoints.Dispose();
            ClusterYPlanePoints.Dispose();
            ClusterZPlanePoints.Dispose();
            FrustumBoundingPoints.Dispose();
            ClusterXPlaneNormal.Dispose();
            ClusterYPlaneNormal.Dispose();
            ClusterZPlaneNormal.Dispose();
            ClusterNormal.Dispose();
        }
    }

    public struct CullingUtls
    {
        public static bool PlaneSphereInclusion(float3 planeNormal, float3 pointOnPlane, float3 circleCenter, float radius)
        {
            bool c = dot(circleCenter - pointOnPlane, planeNormal) > -radius;
            return c;
        }
    }
    public struct ZBinningInfo
    {
        public float3 CameraNormal;
        public NativeArray<float3> ZBinningPoints;

        public void Update(int binningCount, Camera camera)
        {
            NewOrReallocate(binningCount);
            FillZBinningPoints(binningCount, camera);
            CameraNormal = camera.transform.forward;
        }

        void NewOrReallocate(int binningCount)
        {

            ZBinningPoints = new NativeArray<float3>(binningCount+1,Allocator.TempJob);
        }

        void FillZBinningPoints(int binningCount, Camera camera)
        {
            for (int i = 0; i <= binningCount; i++)
            {
                camera.ScreenToWorldPoint(new Vector3(camera.scaledPixelWidth*0.5f, camera.scaledPixelHeight*0.5f,  lerp(camera.nearClipPlane, camera.farClipPlane, (float)i/binningCount))); // replace this will avalnch hard coded value
            }
        }
        
        public void Dispose()
        {
            ZBinningPoints.Dispose();
        }
    }
    public struct LightCullingOnePhaseJobData : IJobParallelFor
    {
        

        [Unity.Collections.ReadOnly] private NativeArray<float3> m_LightPos;
        [Unity.Collections.ReadOnly] private NativeArray<float> m_LightRange;
        
        NativeArray<TileLightIndex> m_lightIndices;
        NativeArray<int> m_TileLightCount;
         NativeArray<uint2> m_TileLightIndicesMinMax;
        [Unity.Collections.ReadOnly]
        int3 m_TileCount;
        [Unity.Collections.ReadOnly]
        ClusterInfo m_ClusterInfo;

        public void Prepare(NativeArray<float3> sortedLightPos, NativeArray<float> sortedLightRange, NativeArray<TileLightIndex> lightIndices, NativeArray<int> tileLightCount,NativeArray<uint2> lightIndexMinMax,int3 tileCount, ClusterInfo clusterInfo)
        {
            m_LightPos = sortedLightPos;
            m_LightRange = sortedLightRange;
            m_lightIndices = lightIndices;
            m_TileCount = tileCount;
            m_ClusterInfo = clusterInfo;
            m_TileLightCount = tileLightCount;
            m_TileLightIndicesMinMax = lightIndexMinMax;
        }
        bool PlaneCircleCollision(float3 planeNormal, float3 pointOnPlane, float3 circleCenter, float radius)
        {
            bool c = dot(circleCenter - pointOnPlane, planeNormal) > -radius;
            return c;
        }
        public void Execute(int index) //row major
        {
            int3 tileIndex;
            TileLightIndex tileLightIndex = m_lightIndices[index];
            tileLightIndex.Reset();
            tileIndex.x = index % m_TileCount.x;
            tileIndex.y = index / m_TileCount.x;
            m_TileLightIndicesMinMax[index] = new uint2(1024,0);
            var lightCount = 0;
            for (int i = 0; i < m_LightPos.Length; i++)
            {
                float3 lightPosition = m_LightPos[i];
                var range = m_LightRange[i];
                var tileIsLit = PlaneCircleCollision(m_ClusterInfo.ClusterXPlaneNormal[tileIndex.x],
                                    m_ClusterInfo.ClusterXPlanePoints[tileIndex.x], lightPosition, range) // plane left
                                && PlaneCircleCollision(-m_ClusterInfo.ClusterXPlaneNormal[tileIndex.x+1],
                                    m_ClusterInfo.ClusterXPlanePoints[tileIndex.x + 1], lightPosition, range)// plane right
                                && PlaneCircleCollision(m_ClusterInfo.ClusterYPlaneNormal[tileIndex.y],
                                      m_ClusterInfo.ClusterYPlanePoints[tileIndex.y], lightPosition, range) // plane top
                                && PlaneCircleCollision(-m_ClusterInfo.ClusterYPlaneNormal[tileIndex.y+1],
                                    m_ClusterInfo.ClusterYPlanePoints[tileIndex.y + 1], lightPosition, range);
                uint unsignedi = (uint) i;
                if (tileIsLit)
                {
                    tileLightIndex.SetIndex(i);
                    m_TileLightIndicesMinMax[index] = new uint2(min(unsignedi,m_TileLightIndicesMinMax[index].x),
                        max(unsignedi, m_TileLightIndicesMinMax[index].y));
                    lightCount += 1;
                }
            }
            m_lightIndices[index] = tileLightIndex;
            m_TileLightCount[index] = lightCount;
        }
    }

    // First Phase: BroadPhase
    // Second Phase: NarrowPhase
    public struct LightCullingTwoPhaseJobData : IJobParallelFor
    {
        [Unity.Collections.ReadOnly]
        public NativeArray<float3> m_LightPos;
        [Unity.Collections.ReadOnly]
        public NativeArray<float> m_LightAffectingRange;
        
        [Unity.Collections.ReadOnly]
        public NativeArray<BitField32> m_SrcTileLightIndicesMask;
        public NativeArray<BitField32> m_ResTileLightIndicesMask;
        public NativeArray<uint2> m_TileLightIndicesMinMax;
        public NativeArray<int>m_TileLightCount;
        public int2 m_TileSize; // 64 64 by default
        public int2 m_TileCount;
        public int2 m_LastPhaseToCurTileScale;
        public ClusterInfo m_ClusterInfo;

        
        public void Prepare(NativeArray<float3> lightPos, NativeArray<float> lightAffectingRange, NativeArray<BitField32> srcLightIndicesMask,NativeArray<BitField32> resLightIndicesMask,
            NativeArray<int> tileLightCount,
            NativeArray<uint2> tileLightIndicesMinMax, ClusterInfo bigClusterInfo, int2 tileCount, int2 lastPhaseToCurTileScale)
        {
            m_LightPos = lightPos;
            m_LightAffectingRange = lightAffectingRange;
            m_SrcTileLightIndicesMask = srcLightIndicesMask;
            m_ResTileLightIndicesMask = resLightIndicesMask;
            m_TileLightIndicesMinMax = tileLightIndicesMinMax;
            m_TileLightCount = tileLightCount;
            m_TileCount = tileCount;
            m_ClusterInfo = bigClusterInfo;
            m_LastPhaseToCurTileScale = lastPhaseToCurTileScale;
        }

        public void Execute(int jobId)
        {
            int2 tileId = new int2(jobId%m_TileCount.x, jobId/m_TileCount.x);
            int2 bigTileId = tileId / m_LastPhaseToCurTileScale;
            int2 bigTileCount = (m_TileCount+ m_LastPhaseToCurTileScale - new int2(1,1))/ m_LastPhaseToCurTileScale;
            int bigTileBufferID = bigTileId.x + bigTileId.y * bigTileCount.x;
            int lightCount = 0;
            m_ResTileLightIndicesMask[jobId].Clear();
            BitField32 lightIndicies = new BitField32();
            for (int lightIndex = 0; lightIndex < m_LightPos.Length; lightIndex++)
            {
                if (m_SrcTileLightIndicesMask[bigTileBufferID].IsSet(lightIndex))
                {
                    float3 lightPosition = m_LightPos[lightIndex];
                    var range = m_LightAffectingRange[lightIndex];
                    var tileIsLit = CullingUtls.PlaneSphereInclusion(m_ClusterInfo.ClusterXPlaneNormal[tileId.x],
                                        m_ClusterInfo.ClusterXPlanePoints[tileId.x], lightPosition, range) // plane left
                                    && CullingUtls.PlaneSphereInclusion(-m_ClusterInfo.ClusterXPlaneNormal[tileId.x+1],
                                        m_ClusterInfo.ClusterXPlanePoints[tileId.x + 1], lightPosition, range)// plane right
                                    && CullingUtls.PlaneSphereInclusion(m_ClusterInfo.ClusterYPlaneNormal[tileId.y],
                                        m_ClusterInfo.ClusterYPlanePoints[tileId.y], lightPosition, range) // plane top
                                    && CullingUtls.PlaneSphereInclusion(-m_ClusterInfo.ClusterYPlaneNormal[tileId.y+1],
                                        m_ClusterInfo.ClusterYPlanePoints[tileId.y + 1], lightPosition, range);
                    uint unsignedLightIndex = (uint)lightIndex;
                    if (tileIsLit)
                    {
                        lightIndicies.SetBits(lightIndex,true);
                        
                        m_TileLightIndicesMinMax[jobId] = new uint2(min(unsignedLightIndex,m_TileLightIndicesMinMax[jobId].x),
                            max(unsignedLightIndex, m_TileLightIndicesMinMax[jobId].y));
                        lightCount += 1;
                    }
                }
            }

            m_ResTileLightIndicesMask[jobId] = lightIndicies;
            m_TileLightCount[jobId] = lightCount;
        }
    }
    
    public struct LightCullingZBinningJobData : IJobParallelFor
    {
        
        [Unity.Collections.ReadOnly]
        NativeArray<VisibleLight> m_LightList;

        NativeArray<TileLightIndex> m_lightIndices;
        int m_ZBinningCount;
        ZBinningInfo m_ZBinningInfo;


        bool PlaneCircleCollision(float3 planeNormal, float3 pointOnPlane, float3 circleCenter, float radius)
        {
            return dot(circleCenter - pointOnPlane, planeNormal) < radius;
        }

        public void Prepare(NativeArray<VisibleLight> sortedLightList, NativeArray<TileLightIndex> lightIndices, int zBinningCount, ZBinningInfo zBinningInfo)
        {
            m_LightList = sortedLightList;
            m_lightIndices = lightIndices;
            m_ZBinningCount = zBinningCount;
            m_ZBinningInfo = zBinningInfo;
        }
        public void Execute(int zBinningIndex) //16 by default
        {
            int3 tileIndex;
            TileLightIndex tileLightIndex = m_lightIndices[zBinningIndex];
            tileLightIndex.Reset();
            for (int i = 0; i < m_LightList.Length; i++)
            {
                float3 lightPosition = m_LightList[i].light.transform.position;
                var range = m_LightList[i].light.range;
                var tileIsLit = PlaneCircleCollision(-m_ZBinningInfo.CameraNormal[0],
                                    m_ZBinningInfo.ZBinningPoints[0][i], lightPosition, range) // plane left
                                && PlaneCircleCollision(m_ZBinningInfo.CameraNormal[0],
                                    m_ZBinningInfo.ZBinningPoints[0][i + 1], lightPosition, range); // plane right
                
                if (tileIsLit)
                {
                    tileLightIndex.SetIndex(i);
                }
            }
            m_lightIndices[zBinningIndex] = tileLightIndex;
        }
    }

    
    // x 0 left 1 right
    // y 0 top 1 right
    // z 0 near z far
    //
    //          /  z 1
    //         /
    //        /
    //       /
    //      /
    //     /
    //    /
    //   /
    //  /
    // / xyz 0                                    x 1 
    // /////////////////////////////////////////////
    // /
    // /
    // /
    // /
    // /
    // /
    // /
    // /
    // / y 1 
    public class LightCullingPass
    {
        private const int MaxOnScreenLightCount = 64;
        private const int ZBinningCount = 10;
        // Holds light direction for directional lights or position for punctual lights.
        // When w is set to 1.0, it means it's a punctual light.
        readonly Vector4 k_DefaultLightPosition = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
        readonly Vector4 k_DefaultLightColor = Color.black;

        // Default light attenuation is setup in a particular way that it causes
        // directional lights to return 1.0 for both distance and angle attenuation
        readonly Vector4 k_DefaultLightAttenuation = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
        readonly Vector4 k_DefaultLightSpotDirection = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
        readonly Vector4 k_DefaultLightsProbeChannel = new Vector4(-1.0f, 1.0f, -1.0f, -1.0f);
        
        
        private bool m_IsContainerAllocated = false;
        // Allocator.Persistent
        // should be ok
        // NativeArray<float> result = new NativeArray<float>(1, Allocator.TempJob);
        
        //private NativeArray<int> m_clusterIndices;
        private NativeArray<VisibleLight> m_SortedLights;
        private NativeList<NativeList<uint>> m_ClusterIndexOfLights;
        private NativeArray<TileLightIndex> m_LightTileIndices;
        private NativeArray<BitField32> m_BigTileIndices;
        private NativeArray<BitField32> m_SmallTileIndices;
        private NativeArray<TileLightIndex> m_LightZBinningIndices;

        private ClusterInfo m_BigClusterInfo;
        private ClusterInfo m_SmallClusterInfo;
        private ZBinningInfo m_ZBinningInfo;
        private LightViewSpaceDistanceComparer m_LightComparer;

        private LightCullingTwoPhaseJobData m_BroadPhaseJobData;
        private LightCullingTwoPhaseJobData m_NarrowPhaseJobData;
        private LightCullingOnePhaseJobData _mOnePhaseJobData;
        private LightCullingZBinningJobData m_ZBinningJobData;
        private JobHandle m_BroadPhaseJob;
        private JobHandle m_NarrowPhaseJobHandle;
        private JobHandle m_ZBinningJob;

        
        private int3 m_SmallTileCount;
        private int3 m_BigTileCount;

        //private ComputeBuffer m_LightIndicesBuffer;
        //private ComputeBuffer m_TileLightCountBuffer;
        
        // ssbo light structure
        // Start is called before the first frame update

        private Shader DebugLightCountShader;
        
        
        
        
        // light index array[int]
        // cluster index[int] the offset for each index
        // light pos array[vector4]
        // light color array[vector4]
        // light atten array[vector4]
        public LightCullingPass()
        {
            // do the allocation
            int tileSizeX = 64;
            int tileSizeY = 64;
            int bigTileSizeX = 256;
            int bigTileSizeY = 256;
            m_SmallTileCount.x = (1920 + tileSizeX - 1) / tileSizeX;
            m_SmallTileCount.y = (1080 + tileSizeY - 1) / tileSizeY;
            m_SmallTileCount.z = 1;
            m_BigTileCount.x = (1920 + bigTileSizeX - 1) / bigTileSizeX;
            m_BigTileCount.y = (1920 + bigTileSizeX - 1) / bigTileSizeX;
            m_BigTileCount.z = 1;
            
            m_LightComparer = new LightViewSpaceDistanceComparer();
            m_BigClusterInfo = new ClusterInfo();
            m_SmallClusterInfo = new ClusterInfo();
            m_ZBinningInfo = new ZBinningInfo();
            m_BroadPhaseJobData = new LightCullingTwoPhaseJobData();
            m_NarrowPhaseJobData = new LightCullingTwoPhaseJobData();
            _mOnePhaseJobData = new LightCullingOnePhaseJobData();
            m_ZBinningJobData = new LightCullingZBinningJobData();



        }

        // Context

        public void Execute(ScriptableRenderContext context, CullingResults cullingResults,Camera camera)
        {
            int smallTileSizeX = 64;
            int smallTileSizeY = 64;
            int bigTileSizeX = 256;
            int bigTileSizeY = 256;
            m_SmallTileCount.x = (camera.scaledPixelWidth + smallTileSizeX - 1) / smallTileSizeX;
            m_SmallTileCount.y = (camera.scaledPixelHeight + smallTileSizeY - 1) / smallTileSizeY;
            m_SmallTileCount.z = 1;
            m_BigTileCount.x = (camera.scaledPixelWidth + bigTileSizeX - 1) / bigTileSizeX;
            m_BigTileCount.y = (camera.scaledPixelHeight + bigTileSizeY - 1) / bigTileSizeY;
            m_BigTileCount.z = 1;
            int additionalLightCount = 0;
            int sortedLightIndex = 0;
            for (int i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                if (cullingResults.visibleLights[i].light.type == LightType.Point ||
                    cullingResults.visibleLights[i].lightType == LightType.Spot)
                {
                    additionalLightCount++;
                }
            }
            m_LightComparer.ViewCamera = camera;
            m_LightComparer.CameraForward = camera.transform.forward;
            m_SortedLights = new NativeArray<VisibleLight>(additionalLightCount, Allocator.TempJob);
            m_LightTileIndices = new NativeArray<TileLightIndex>(m_SmallTileCount.x * m_SmallTileCount.y, Allocator.TempJob);
            m_SmallTileIndices = new NativeArray<BitField32>(m_SmallTileCount.x * m_SmallTileCount.y, Allocator.TempJob);
            
            m_BigTileIndices = new NativeArray<BitField32>(m_BigTileCount.x * m_BigTileCount.y, Allocator.TempJob);
            m_LightZBinningIndices = new NativeArray<TileLightIndex>(ZBinningCount, Allocator.TempJob);
            for (int i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                if (cullingResults.visibleLights[i].light.type == LightType.Point ||
                    cullingResults.visibleLights[i].lightType == LightType.Spot)
                {
                    m_SortedLights[sortedLightIndex] = cullingResults.visibleLights[i];
                    sortedLightIndex++;
                }
            }
            
            
            //cullingResults.visibleLights.CopyTo(m_SortedLights);
            m_SortedLights.Sort(m_LightComparer); // wish the sorting works well
            var lightPos = new NativeArray<float3>(m_SortedLights.Length, Allocator.TempJob); // switch to float4 if neeed to use uniform buffer
            var lightRange = new NativeArray<float>(m_SortedLights.Length, Allocator.TempJob);
            var tileLightCount = new NativeArray<int>(m_SmallTileCount.x * m_SmallTileCount.y, Allocator.TempJob);
            var bigTileLightIndicesMinMax = new NativeArray<uint2>(m_BigTileCount.x * m_BigTileCount.y, Allocator.TempJob);
            var tileLightIndicesMinMax = new NativeArray<uint2>(m_SmallTileCount.x * m_SmallTileCount.y, Allocator.TempJob);
            for (var i = 0; i < m_SortedLights.Length; i++)
            {
                lightPos[i] = m_SortedLights[i].light.transform.position;
                lightRange[i] = m_SortedLights[i].range;
            }
             
            m_SmallClusterInfo.Update(m_SmallTileCount, camera);
            m_BigClusterInfo.Update(m_BigTileCount, camera);
            m_ZBinningInfo.Update(ZBinningCount,camera);
            
            TileInfo tileInfo = new TileInfo();
            tileInfo.BigTileCount = m_BigTileCount.xy;
            tileInfo.BigTileSize = new int2(bigTileSizeX, bigTileSizeY);
            tileInfo.SmallTileCount = m_SmallTileCount.xy;
            tileInfo.SmallTileSize = new int2(smallTileSizeX,smallTileSizeY);
            
            // BroadPhaseJob();
            
            // NarrowPhaseJob();
            NativeArray<BitField32> dummpyLightTileIndices = new NativeArray<BitField32>(m_BigTileCount.x * m_BigTileCount.y ,Allocator.TempJob);
            _mOnePhaseJobData.Prepare(lightPos,lightRange,m_LightTileIndices, tileLightCount,tileLightIndicesMinMax, m_SmallTileCount,m_SmallClusterInfo);
            
            // BroadPhase()
            // FillData()
            // NarrowPhase()
            
            var bitFiledOnes = new BitField32(~0U);
            for (var i = 0; i < m_BigTileIndices.Length; i++)
            {
                dummpyLightTileIndices[i] = bitFiledOnes;
                m_BigTileIndices[i] = bitFiledOnes;
            }
            m_BroadPhaseJobData.Prepare(lightPos,lightRange,dummpyLightTileIndices,m_BigTileIndices, tileLightCount,tileLightIndicesMinMax,m_BigClusterInfo, m_BigTileCount.xy, new int2(1,1));
            m_NarrowPhaseJobData.Prepare(lightPos,lightRange,m_BigTileIndices,m_SmallTileIndices ,tileLightCount,tileLightIndicesMinMax,m_SmallClusterInfo,m_SmallTileCount.xy, new int2(bigTileSizeX/smallTileSizeX,bigTileSizeY/smallTileSizeY));
            //m_NarrowPhaseJobHandle = _mOnePhaseJobData.Schedule(m_LightTileIndices.Length, m_LightTileIndices.Length);
            m_NarrowPhaseJobHandle = m_BroadPhaseJobData.Schedule(m_BigTileCount.x * m_BigTileCount.y, m_BigTileCount.x * m_BigTileCount.y);
            m_NarrowPhaseJobHandle.Complete();
            
            m_NarrowPhaseJobHandle = m_NarrowPhaseJobData.Schedule(m_SmallTileCount.x*m_SmallTileCount.y, m_SmallTileCount.x*m_SmallTileCount.y);
            m_NarrowPhaseJobHandle.Complete();

            // start jobs

            // wait for jobs to be finished

            // build structured buffer

            BuildSSBO(context, m_SortedLights, tileLightCount, tileLightIndicesMinMax,
                new float4(smallTileSizeX,smallTileSizeY, 1.0f/smallTileSizeX, 1.0f/smallTileSizeY),m_SmallTileCount.x, m_SmallTileCount.y);
            // set to command

            // Clean Up
            dummpyLightTileIndices.Dispose();
            bigTileLightIndicesMinMax.Dispose();
            tileLightIndicesMinMax.Dispose();
            tileLightCount.Dispose();
            lightPos.Dispose();
            lightRange.Dispose();
            m_SmallTileIndices.Dispose();
            m_ZBinningInfo.Dispose();
            m_BigClusterInfo.Dispose();
            m_SmallClusterInfo.Dispose();
            m_LightZBinningIndices.Dispose();
            m_BigTileIndices.Dispose();
            m_LightTileIndices.Dispose();
            m_SortedLights .Dispose();


        }
        
        void InitializeLightConstants(NativeArray<VisibleLight> lights, int lightIndex, out Vector4 lightPos, out Vector4 lightColor, out Vector4 lightAttenuation, out Vector4 lightSpotDir, out Vector4 lightOcclusionProbeChannel)
        {
            lightPos = k_DefaultLightPosition;
            lightColor = k_DefaultLightColor;
            lightAttenuation = k_DefaultLightAttenuation;
            lightSpotDir = k_DefaultLightSpotDirection;
            lightOcclusionProbeChannel = k_DefaultLightsProbeChannel;

            // When no lights are visible, main light will be set to -1.
            // In this case we initialize it to default values and return
            if (lightIndex < 0)
                return;

            VisibleLight lightData = lights[lightIndex];
            if (lightData.lightType == LightType.Directional)
            {
                Vector4 dir = -lightData.localToWorldMatrix.GetColumn(2);
                lightPos = new Vector4(dir.x, dir.y, dir.z, 0.0f);
            }
            else
            {
                Vector4 pos = lightData.localToWorldMatrix.GetColumn(3);
                lightPos = new Vector4(pos.x, pos.y, pos.z, 1.0f);
            }

            // VisibleLight.finalColor already returns color in active color space
            lightColor = lightData.finalColor;

            // Directional Light attenuation is initialize so distance attenuation always be 1.0
            if (lightData.lightType != LightType.Directional)
            {
                // Light attenuation in universal matches the unity vanilla one.
                // attenuation = 1.0 / distanceToLightSqr
                // We offer two different smoothing factors.
                // The smoothing factors make sure that the light intensity is zero at the light range limit.
                // The first smoothing factor is a linear fade starting at 80 % of the light range.
                // smoothFactor = (lightRangeSqr - distanceToLightSqr) / (lightRangeSqr - fadeStartDistanceSqr)
                // We rewrite smoothFactor to be able to pre compute the constant terms below and apply the smooth factor
                // with one MAD instruction
                // smoothFactor =  distanceSqr * (1.0 / (fadeDistanceSqr - lightRangeSqr)) + (-lightRangeSqr / (fadeDistanceSqr - lightRangeSqr)
                //                 distanceSqr *           oneOverFadeRangeSqr             +              lightRangeSqrOverFadeRangeSqr

                // The other smoothing factor matches the one used in the Unity lightmapper but is slower than the linear one.
                // smoothFactor = (1.0 - saturate((distanceSqr * 1.0 / lightrangeSqr)^2))^2
                float lightRangeSqr = lightData.range * lightData.range;
                float fadeStartDistanceSqr = 0.8f * 0.8f * lightRangeSqr;
                float fadeRangeSqr = (fadeStartDistanceSqr - lightRangeSqr);
                float oneOverFadeRangeSqr = 1.0f / fadeRangeSqr;
                float lightRangeSqrOverFadeRangeSqr = -lightRangeSqr / fadeRangeSqr;
                float oneOverLightRangeSqr = 1.0f / Mathf.Max(0.0001f, lightData.range * lightData.range);

                // On mobile: Use the faster linear smoothing factor.
                // On other devices: Use the smoothing factor that matches the GI.
                lightAttenuation.x = Application.isMobilePlatform ? oneOverFadeRangeSqr : oneOverLightRangeSqr;
                lightAttenuation.y = lightRangeSqrOverFadeRangeSqr;
            }

            if (lightData.lightType == LightType.Spot)
            {
                Vector4 dir = lightData.localToWorldMatrix.GetColumn(2);
                lightSpotDir = new Vector4(-dir.x, -dir.y, -dir.z, 0.0f);

                // Spot Attenuation with a linear falloff can be defined as
                // (SdotL - cosOuterAngle) / (cosInnerAngle - cosOuterAngle)
                // This can be rewritten as
                // invAngleRange = 1.0 / (cosInnerAngle - cosOuterAngle)
                // SdotL * invAngleRange + (-cosOuterAngle * invAngleRange)
                // If we precompute the terms in a MAD instruction
                float cosOuterAngle = Mathf.Cos(Mathf.Deg2Rad * lightData.spotAngle * 0.5f);
                // We neeed to do a null check for particle lights
                // This should be changed in the future
                // Particle lights will use an inline function
                float cosInnerAngle;
                if (lightData.light != null)
                    cosInnerAngle = Mathf.Cos(lightData.light.innerSpotAngle * Mathf.Deg2Rad * 0.5f);
                else
                    cosInnerAngle = Mathf.Cos((2.0f * Mathf.Atan(Mathf.Tan(lightData.spotAngle * 0.5f * Mathf.Deg2Rad) * (64.0f - 18.0f) / 64.0f)) * 0.5f);
                float smoothAngleRange = Mathf.Max(0.001f, cosInnerAngle - cosOuterAngle);
                float invAngleRange = 1.0f / smoothAngleRange;
                float add = -cosOuterAngle * invAngleRange;
                lightAttenuation.z = invAngleRange;
                lightAttenuation.w = add;
            }

            Light light = lightData.light;

            // Set the occlusion probe channel.
            int occlusionProbeChannel = light != null ? light.bakingOutput.occlusionMaskChannel : -1;

            // If we have baked the light, the occlusion channel is the index we need to sample in 'unity_ProbesOcclusion'
            // If we have not baked the light, the occlusion channel is -1.
            // In case there is no occlusion channel is -1, we set it to zero, and then set the second value in the
            // input to one. We then, in the shader max with the second value for non-occluded lights.
            lightOcclusionProbeChannel.x = occlusionProbeChannel == -1 ? 0f : occlusionProbeChannel;
            lightOcclusionProbeChannel.y = occlusionProbeChannel == -1 ? 1f : 0f;

            // TODO: Add support to shadow mask
            // if (light != null && light.bakingOutput.mixedLightingMode == MixedLightingMode.Subtractive && light.bakingOutput.lightmapBakeType == LightmapBakeType.Mixed)
            // {
            //     if (m_MixedLightingSetup == MixedLightingSetup.None && lightData.light.shadows != LightShadows.None)
            //     {
            //         m_MixedLightingSetup = MixedLightingSetup.Subtractive;
            //     }
            // }
        }

        

        
        public void BuildSSBO(ScriptableRenderContext context, NativeArray<VisibleLight> sortedLights, NativeArray<int> tileLightCount, NativeArray<uint2> tileLightIndicesMinMax,float4 tileSize, int tileCountX, int tileCountY)
        {
            var cmd = CommandBufferPool.Get("SetLightingBuffer");

            var maxAdditionalLightsCount = 64;
            var additionalLightCount = min(sortedLights.Length, maxAdditionalLightsCount);
            var tileLightCountBuffer = ShaderData.instance.GetTileLightCountBuffer(tileLightCount.Length);
            tileLightCountBuffer.SetData(tileLightCount);
            //TODO:Update light data buffer
            NativeArray<ShaderInput.LightData> additionalLightsData = new NativeArray<ShaderInput.LightData>(additionalLightCount, Allocator.Temp);
            for (int i = 0, lightIter = 0; i < sortedLights.Length && lightIter < maxAdditionalLightsCount; ++i)
            {
                VisibleLight light = sortedLights[i];
                ShaderInput.LightData data;
                InitializeLightConstants(sortedLights, i,
                    out data.position, out data.color, out data.attenuation,
                    out data.spotDirection, out data.occlusionProbeChannels);
                additionalLightsData[lightIter] = data;
                lightIter++;
            }

            var lightDataBuffer = ShaderData.instance.GetLightDataBuffer(additionalLightCount);
            lightDataBuffer.SetData(additionalLightsData);
            
            
            //TODO:Update light indices buffer
            //var lightIndicesBuffer = ShaderData.instance.GetLightIndicesBuffer(tileCountX*tileCountY);
            //lightIndicesBuffer.SetData(m_LightTileIndices);
            var lightIndicesBuffer = ShaderData.instance.GetLightIndicesBuffer(tileCountX*tileCountY);
            lightIndicesBuffer.SetData(m_SmallTileIndices);

            var tileLightIndicesMinMaxBuffer =
                ShaderData.instance.GetTileLightIndicesMinMaxBuffer(tileCountX * tileCountY);
            tileLightIndicesMinMaxBuffer.SetData(tileLightIndicesMinMax);
            
            //cmd.SetGlobalVector("_TileSize", tileSize);
            cmd.SetGlobalInt("_TileCountX", tileCountX);
            cmd.SetGlobalInt("_TileCountY", tileCountY);
            // Light Indices
            // Light indices float4 bitmask per tile
            cmd.SetGlobalBuffer("LightDataBuffer",lightDataBuffer);
            cmd.SetGlobalBuffer("TileLightCountBuffer", tileLightCountBuffer);
            cmd.SetGlobalBuffer("TileLightIndicesBuffer",lightIndicesBuffer);
            cmd.SetGlobalBuffer("TileLightIndicesMinMaxBuffer",tileLightIndicesMinMaxBuffer);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            additionalLightsData.Dispose();
        }
        public void Dispose()
        {
            //m_clusterIndices.Dispose();
        }

    }
}
