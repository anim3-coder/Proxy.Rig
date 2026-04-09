using System;
using Proxy.Mesh;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using static Proxy.Rig.Constrains;
using static Proxy.Rig.Driver;

namespace Proxy.Rig
{
    public class Rig : MonoBehaviour, IProxyChild, IProxyJob, IDisposable
    {
        private ProxyMesh proxy;
        public RigConstrains Constrains { get; private set; } = new RigConstrains();
        public RigDrivers Drivers { get; private set; } = new RigDrivers();
        public bool IsInit => proxy != null;
        public JobType type => JobType.Parallel; 
        public void OnInit(ProxyMesh proxyMesh)
        {
            proxy = proxyMesh;

            Constrains.OnInit(this);
            Drivers.OnInit(this);
        }
        public void Dispose()
        {
            OnShutdown();
        }
        public void OnShutdown(ProxyMesh proxyMesh)
        {
            OnShutdown();
        }
        public void OnShutdown()
        {
            Constrains.OnShutdown();
            Drivers.OnShutdown();
        }
        public JobHandle StartJob(JobHandle dependsOn)
        {
            dependsOn = new Clear()
            {
                data = Constrains.constrains
            }.Schedule(Constrains.constrains.Length, 16, dependsOn);
            dependsOn = Drivers.StartJob(dependsOn);
            dependsOn = Constrains.StartJob(dependsOn);
            return dependsOn;
        }
        public void OnJobComplete()
        {
            Constrains.OnJobComplete();
            Drivers.OnJobComplete();
        }

        [BurstCompile]
        public struct Clear : IJobParallelFor
        {
            public NativeArray<ConstrainsData> data;
            public void Execute(int index)
            {
                var r = data[index];
                r.internalPositionOffset = Vector3.zero;
                r.internalRotationOffset = Quaternion.identity;
                data[index] = r;
            }
        }
    }
}