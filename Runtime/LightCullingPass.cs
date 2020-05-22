using System;
using System.Collections;
using System.Collections.Generic;
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
namespace UnityEngine.Rendering.CustomRenderPipeline
{
    
    
    
    
    public class LightViewSpaceDistanceComparer : IComparer<VisibleLight>
    {
        public Camera ViewCamera { get; set; }
        // Call CaseInsensitiveComparer.Compare with the parameters reversed.
        public int Compare(VisibleLight x, VisibleLight y)
        {
            //try to switch to 
            Vector3 xDepthMin = ViewCamera.WorldToScreenPoint(x.light.transform.position - ViewCamera.transform.forward*x.light.range);
            Vector3 yDepthMin = ViewCamera.WorldToScreenPoint(y.light.transform.position - ViewCamera.transform.forward*y.light.range);
            return xDepthMin.z == yDepthMin.z? 0:xDepthMin.z<yDepthMin.z? -1:1; // screen space depth
        }
    }

    public struct LightCullingJob : IJobParallelFor
    {
        public void Execute(int index)
        {
            //TODO: Do Actual culling
        }
    }
    
    public struct LightCullingBroadPhaseJob : IJobParallelFor
    {
        public void Execute(int bigTileIndex)
        {
            //TODO: Do Actual culling
        }
    }
    
    public struct LightCullingZBinningJob : IJobParallelFor
    {
        public void Execute(int zBinningIndex)
        {
            
        }
    
    }
    public struct LightIndex
    { 
        uint4 IndexBitArry; // support up to 32 light per tile
        //high end
        //1000000000...00000 -> 1
        //0100000000...00000 -> 2
        void Reset()
        {
            IndexBitArry = 0;
        }
        void SetIndex(int id)
        {
            if (id < 32)
            {
                IndexBitArry.x |= 1u << id;
            }
            else if (id < 64)
            {
                IndexBitArry.y |= 1u << (id-32);
            }
            else if (id < 96)
            {
                IndexBitArry.z |= 1u << (id - 64);
            }
            else
            {
                IndexBitArry.w |= 1u << (id - 96);
            }
            
            
        }

        void ResetIndex(int id)
        {
            if (id < 32)
            {
                IndexBitArry.x |= 1u << id;
            }
            else if (id < 64)
            {
                IndexBitArry.y |= 1u << (id-32);
            }
            else if (id < 96)
            {
                IndexBitArry.z |= 1u << (id - 64);
            }
            else
            {
                IndexBitArry.w |= 1u << (id - 96);
            }
        }

        bool GetIndex(int id)
        {
            if (id < 32)
            {
                return (IndexBitArry.x & 1u << id) != 0u;
            }
            else if (id < 64)
            {
                return (IndexBitArry.y & 1u << (id-32)) != 0u;
            }
            else if (id < 96)
            {
                return (IndexBitArry.z & 1u << (id - 64)) != 0u;
            }
            else
            {
                return (IndexBitArry.w |= 1u << (id - 96)) != 0u;
            }
        }
        
        
    }
    public struct LightCullingNarrowPhaseJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<VisibleLight> m_LightList;

        public NativeArray<float4> m_lightIndices;
        public int3 tileCount;
        public ClusterInfo m_ClusterInfo;
        public void Execute(int index) //row major
        {
            int3 tileIndex;
            LightIndex lightIndex;
            tileIndex.x = index / tileCount.x;
            tileIndex.y = index % tileCount.y;
            for (int i = 0; i < m_LightList.Length; i++)
            {
                //planeLeft
                dot(m_LightList[i].light.transform.position - m_ClusterInfo.
                //planeRight
                //PlaneTop
                //PlaneBot
                //PlaneBack
                //PlaneForward
            }
            
            
        }
    }

    

    
    
    public struct ClusterInfo
    {
        
        NativeArray<float3> m_ClusterNormal;//native container or not
        NativeArray<NativeArray<float3>> m_ClusterPoints;
        float3[,,] m_FrustumBoundingPoints; // this should not be used directly

        public void Update(int3 tileCount, Camera camera)
        {
            NewOrReallocate(tileCount);
            FillNormalAndPoints(tileCount, camera);
        }

        void NewOrReallocate(int3 tileCount)
        {
            if (m_FrustumBoundingPoints == null)
            {
                m_FrustumBoundingPoints = new float3[2,2,2];
            }

            m_ClusterNormal = new NativeArray<float3>(3,Allocator.Temp);
            m_ClusterPoints = new NativeArray<NativeArray<float3>>(3, Allocator.Temp);
            m_ClusterPoints[0] = new NativeArray<float3>(tileCount.x+1, Allocator.Temp);
            m_ClusterPoints[0] = new NativeArray<float3>(tileCount.x+1, Allocator.Temp);
            m_ClusterPoints[0] = new NativeArray<float3>(tileCount.x+1, Allocator.Temp);
        }

        void FillNormalAndPoints(uint3 tileCount, Camera camera)
        {
            // generate 8 3d points and then lerp
            // try lerping with SIMD
            
           
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        m_FrustumBoundingPoints[x, y, z] =
                            camera.ScreenToWorldPoint(new Vector3(x, y, z == 0 ? camera.nearClipPlane : camera.farClipPlane));
                    }
                }
            }
            
            // x，就是在x方向分割cluster的平面
            //[x][tileIndexX]
            //[y][tileIndexY]
            //[z][tileIndexZ]
            
            

            float3 clusterFwdStep = (m_FrustumBoundingPoints[0,0,1]-m_FrustumBoundingPoints[0,0,0])/tileCount.z;
            float3 clusterRightStep = m_FrustumBoundingPoints[1,0,0]/m_FrustumBoundingPoints[0,0,0]/tileCount.x;
            float3 clusterDownStep = m_FrustumBoundingPoints[0,1,0]/m_FrustumBoundingPoints[0,0,0]/tileCount.y;
            
            //  x,  y,  z
            // -x, -y, -z
            float3[] clusterNormal = new float3[3];
            float3[] clusterNegNormal = new float3[3];
            m_ClusterNormal[0] = normalize(clusterRightStep);
            m_ClusterNormal[1] = normalize(clusterDownStep);
            m_ClusterNormal[2] = normalize(clusterFwdStep);
            // clusterNegNormal[0] = -clusterNormal[0];
            // clusterNegNormal[1] = -clusterNormal[1];
            // clusterNegNormal[2] = -clusterNormal[2];
            // Generate points
            
            for (int x = 0; x <= tileCount.x; x++)
            {
                m_ClusterPoints[0].ReinterpretStore(x,m_FrustumBoundingPoints[0, 0, 0] + clusterRightStep * x);
            }

            for (int y = 0; y <= tileCount.y; y++)
            {
                m_ClusterPoints[1].ReinterpretStore(y,m_FrustumBoundingPoints[0, 0, 0] + clusterDownStep * y);
            }
            for (int z = 0; z <= tileCount.z; z++)
            {
                m_ClusterPoints[1].ReinterpretStore(z,m_FrustumBoundingPoints[0, 0, 0] + clusterFwdStep * z);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < 3; i++)
            {
                m_ClusterPoints[i].Dispose();
            }
            m_ClusterPoints.Dispose();
            m_ClusterNormal.Dispose();
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
        private bool m_IsContainerAllocated = false;
        // Allocator.Persistent
        // should be ok
        // NativeArray<float> result = new NativeArray<float>(1, Allocator.TempJob);
        private LightCullingJob m_LightCullingJob;
        private NativeArray<int> m_clusterIndices;
        private NativeArray<VisibleLight> m_SortedLights;
        private NativeList<NativeList<uint>> m_ClusterIndexOfLights;
        private NativeMultiHashMap<uint, uint> m_LightBufferOutput;


        private ClusterInfo m_ClusterInfo;
        private LightViewSpaceDistanceComparer m_LightComparer;

        private NativeBitArray test;
        //private NativeArray<NativeHashMap<int,int>>w

        private int3 m_TileCount;

        private ComputeBuffer m_LightIndicesBuffer;
        
        // ssbo light structure
        // Start is called before the first frame update
        
        
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
            m_TileCount.x = (1920 + tileSizeX - 1) / tileSizeX;
            m_TileCount.y = (1080 + tileSizeY - 1) / tileSizeY;
            m_TileCount.z = 1;
            int lightIndicesCount = m_TileCount.x * m_TileCount.y * m_TileCount.z;
            m_clusterIndices = new NativeArray<int>(lightIndicesCount, Allocator.Persistent);
            m_LightComparer = new LightViewSpaceDistanceComparer();
            m_ClusterInfo = new ClusterInfo();
            

        }

        // Context

        public void Execute(ScriptableRenderContext context, CullingResults cullingResults,Camera camera)
        {
            int tileSizeX = 64;
            int tileSizeY = 64;
            m_TileCount.x = (camera.scaledPixelWidth + tileSizeX - 1) / tileSizeX;
            m_TileCount.y = (camera.scaledPixelHeight + tileSizeY - 1) / tileSizeY;
            m_TileCount.z = 1;
            int lightIndicesCount = m_TileCount.x * m_TileCount.y * m_TileCount.z;
            // assume 16 point light per cluster at most 
            // at 1080p
            // 1920*1080*8/32/32*16
            // if (m_clusterIndices.Length < lightIndicesCout)
            // {
            //     m_clusterIndices.Dispose();
            //     m_clusterIndices = new NativeArray<int>(lightIndicesCout, Allocator.Persistent);
            // }
            //
            m_LightComparer.ViewCamera = camera;
            m_SortedLights = new NativeArray<VisibleLight>( cullingResults.visibleLights.Length, Allocator.Temp);
            cullingResults.visibleLights.CopyTo(m_SortedLights);
            m_SortedLights.Sort(m_LightComparer); // wish the sorting works well
            
            m_ClusterInfo.Update(m_TileCount, camera);
            

            // BroadPhaseJob();
            //NarrowPhaseJob();
            //FillLightIndex(); // job or not job
            
            CullingJob(cullingResults, camera);
            
            // start jobs

            // wait for jobs to be finished

            // build structured buffer

            BuildSSBO(context);
            // set to command


        }

        
        public void CullingJob( CullingResults cullingResults,Camera camera)
        {
            // Generate planes aligned with x y z plane
            // Dispatch culling per light
            // appending data to buffer
            
            //m_LightData = cullingResults.visibleLights;
            //broad culling
            //just xyz plane
            //JobHandle handle = m_LightCullingJob.Schedule(m_LightData.Length,1);
            // Wait for the job to complele
            //handle.Complete();
            
            //TODO:BROAD PHASE JOB
            
            
            //TODO:NARROW PHASE JOB
            
            
        }

        
        public void BuildSSBO(ScriptableRenderContext context)
        {
            var cmd = CommandBufferPool.Get("SetLightingBuffer");
            
            
            //TODO:Update light data buffer
            //TODO:Update light indices buffer
            
            //Light Indices
            // Light indecies float4 bitmask per tile
            cmd.SetGlobalBuffer("LightDataBuffer",m_LightDataBuffer);
            cmd.SetGlobalBuffer("LightIndicesBuffer",m_LightIndicesBuffer);
        }
        public void Dispose()
        {
            m_clusterIndices.Dispose();
        }

    }
}
