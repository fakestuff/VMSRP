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

    
    public struct LightIndex
    { 
        uint4 IndexBitArry; // support up to 32 light per tile
        //high end
        //1000000000...00000 -> 1
        //0100000000...00000 -> 2
        public void Reset()
        {
            IndexBitArry = 0;
        }
        public LightIndex SetIndex(int id)
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

            return this;


        }

        public LightIndex ResetIndex(int id)
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

            return this;
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
    
    public struct ClusterInfo
    {
        
        public NativeArray<float3> ClusterNormal;//native container or not
        public NativeArray<NativeArray<float3>> ClusterPoints;
        public float3[,,] FrustumBoundingPoints; // this should not be used directly

        public void Update(int3 tileCount, Camera camera)
        {
            NewOrReallocate(tileCount);
            FillNormalAndPoints(tileCount, camera);
        }

        void NewOrReallocate(int3 tileCount)
        {
            if (FrustumBoundingPoints == null)
            {
                FrustumBoundingPoints = new float3[2,2,2];
            }

            ClusterNormal = new NativeArray<float3>(3,Allocator.Temp);
            ClusterPoints = new NativeArray<NativeArray<float3>>(3, Allocator.Temp);
            ClusterPoints[0] = new NativeArray<float3>(tileCount.x+1, Allocator.Temp);
            ClusterPoints[0] = new NativeArray<float3>(tileCount.x+1, Allocator.Temp);
            ClusterPoints[0] = new NativeArray<float3>(tileCount.x+1, Allocator.Temp);
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
                        FrustumBoundingPoints[x, y, z] =
                            camera.ScreenToWorldPoint(new Vector3(x, y, z == 0 ? camera.nearClipPlane : camera.farClipPlane));
                    }
                }
            }
            
            // x，就是在x方向分割cluster的平面
            //[x][tileIndexX]
            //[y][tileIndexY]
            //[z][tileIndexZ]
            
            

            float3 clusterFwdStep = (FrustumBoundingPoints[0,0,1]-FrustumBoundingPoints[0,0,0])/tileCount.z;
            float3 clusterRightStep = FrustumBoundingPoints[1,0,0]/FrustumBoundingPoints[0,0,0]/tileCount.x;
            float3 clusterDownStep = FrustumBoundingPoints[0,1,0]/FrustumBoundingPoints[0,0,0]/tileCount.y;
            
            //  x,  y,  z
            // -x, -y, -z
            float3[] clusterNormal = new float3[3];
            float3[] clusterNegNormal = new float3[3];
            ClusterNormal[0] = normalize(clusterRightStep);
            ClusterNormal[1] = normalize(clusterDownStep);
            ClusterNormal[2] = normalize(clusterFwdStep);
            // clusterNegNormal[0] = -clusterNormal[0];
            // clusterNegNormal[1] = -clusterNormal[1];
            // clusterNegNormal[2] = -clusterNormal[2];
            // Generate points
            
            for (int x = 0; x <= tileCount.x; x++)
            {
                ClusterPoints[0].ReinterpretStore(x,FrustumBoundingPoints[0, 0, 0] + clusterRightStep * x);
            }

            for (int y = 0; y <= tileCount.y; y++)
            {
                ClusterPoints[1].ReinterpretStore(y,FrustumBoundingPoints[0, 0, 0] + clusterDownStep * y);
            }
            for (int z = 0; z <= tileCount.z; z++)
            {
                ClusterPoints[1].ReinterpretStore(z,FrustumBoundingPoints[0, 0, 0] + clusterFwdStep * z);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < 3; i++)
            {
                ClusterPoints[i].Dispose();
            }
            ClusterPoints.Dispose();
            ClusterNormal.Dispose();
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

            ZBinningPoints = new NativeArray<float3>(binningCount+1,Allocator.Temp);
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
    public struct LightCullingNarrowPhaseJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<VisibleLight> m_LightList;

        public NativeArray<LightIndex> m_lightIndices;
        public int3 tileCount;
        public ClusterInfo m_ClusterInfo;
        // TODO:CTOR
        bool PlaneCircleCollision(float3 planeNormal, float3 pointOnPlane, float3 circleCenter, float radius)
        {
            return dot(circleCenter - pointOnPlane, planeNormal) < radius;
        }
        public void Execute(int index) //row major
        {
            int3 tileIndex;
            LightIndex lightIndex = m_lightIndices[index];
            lightIndex.Reset();
            tileIndex.x = index / tileCount.x;
            tileIndex.y = index % tileCount.y;
            
            for (int i = 0; i < m_LightList.Length; i++)
            {
                float3 lightPosition = m_LightList[i].light.transform.position;
                var range = m_LightList[i].light.range;
                var tileIsLit = PlaneCircleCollision(-m_ClusterInfo.ClusterNormal[0],
                                     m_ClusterInfo.ClusterPoints[0][tileIndex.x], lightPosition, range) // plane left
                                 && PlaneCircleCollision(m_ClusterInfo.ClusterNormal[0],
                                     m_ClusterInfo.ClusterPoints[0][tileIndex.x + 1], lightPosition, range) // plane right
                                 && PlaneCircleCollision(m_ClusterInfo.ClusterNormal[0],
                                     m_ClusterInfo.ClusterPoints[0][tileIndex.x + 1], lightPosition, range) // plane top
                                 && PlaneCircleCollision(m_ClusterInfo.ClusterNormal[0],
                                     m_ClusterInfo.ClusterPoints[0][tileIndex.x + 1], lightPosition, range);
                if (tileIsLit)
                {
                    lightIndex.SetIndex(i);
                }
            }
            m_lightIndices[index] = lightIndex;
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
        
        [ReadOnly]
        public NativeArray<VisibleLight> m_LightList;

        public NativeArray<LightIndex> m_lightIndices;
        public int3 tileCount;
        public ZBinningInfo m_ZBinningInfo;

        // TODO:CTOR

        bool PlaneCircleCollision(float3 planeNormal, float3 pointOnPlane, float3 circleCenter, float radius)
        {
            return dot(circleCenter - pointOnPlane, planeNormal) < radius;
        }
        public void Execute(int zBinningIndex) //16 by default
        {
            int3 tileIndex;
            LightIndex lightIndex = m_lightIndices[zBinningIndex];
            lightIndex.Reset();
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
                    lightIndex.SetIndex(i);
                }
                

                
            }

            m_lightIndices[zBinningIndex] = lightIndex;


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
            m_LightComparer.CameraForward = camera.transform.forward;
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
