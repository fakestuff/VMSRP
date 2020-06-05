using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.Rendering.CustomRenderPipeline
{
    
    public class ShaderData : IDisposable
    {
        static ShaderData m_Instance = null;
        ComputeBuffer m_LightDataBuffer = null;
        ComputeBuffer m_LightIndicesBuffer = null;
        ComputeBuffer m_TileLightCountBuffer = null;
        ComputeBuffer m_TileLightIndicesMinMaxBuffer = null;
        
        ShaderData()
        {
        }
        
        internal static ShaderData instance
        {
            get
            {
                if (m_Instance == null)
                    m_Instance = new ShaderData();

                return m_Instance;
            }
        }
        
        public void Dispose()
        {
            DisposeBuffer(ref m_LightDataBuffer);
            DisposeBuffer(ref m_LightIndicesBuffer);
            DisposeBuffer(ref m_TileLightCountBuffer);
            DisposeBuffer(ref m_TileLightIndicesMinMaxBuffer);
        }
        
        internal ComputeBuffer GetLightDataBuffer(int size)
        {
            return GetOrUpdateBuffer<ShaderInput.LightData>(ref m_LightDataBuffer, size);
        }

        internal ComputeBuffer GetLightIndicesBuffer(int size)
        {
            return GetOrUpdateBuffer<uint>(ref m_LightIndicesBuffer, size);
        }

        internal ComputeBuffer GetTileLightCountBuffer(int size)
        {
            return GetOrUpdateBuffer<int>(ref m_TileLightCountBuffer, size);
        }

        internal ComputeBuffer GetTileLightIndicesMinMaxBuffer(int size)
        {
            return GetOrUpdateBuffer<int2>(ref m_TileLightIndicesMinMaxBuffer, size);
        }
        
        ComputeBuffer GetOrUpdateBuffer<T>(ref ComputeBuffer buffer, int size) where T : struct
        {
            if (buffer == null)
            {
                buffer = new ComputeBuffer(size, Marshal.SizeOf<T>());
            }
            else if (size > buffer.count)
            {
                buffer.Dispose();
                buffer = new ComputeBuffer(size, Marshal.SizeOf<T>());
            }

            return buffer;
        }

        void DisposeBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Dispose();
                buffer = null;
            }
        }
        
    }
}
