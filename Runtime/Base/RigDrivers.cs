using Proxy.Collections;
using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Proxy.Rig.Constrains;
using static Proxy.Rig.Driver;

namespace Proxy.Rig
{
    public class RigDrivers : IRigBase
    {
        private Driver[] drivers;

        public NativeJaggedArray<LocalDriverData> localDriversData;
        public NativeArray<DriverData> driverData;
        public NativeArray<float> weights;

        public Rig rig { get; private set; }

        public void OnInit(Rig rig)
        {
            this.rig = rig;
            drivers = rig.GetComponentsInChildren<Driver>();
            foreach (var driver in drivers)
            {
                driver.OnStart(rig);
            }

            driverData = new NativeArray<DriverData>(drivers.Length, Allocator.Persistent);
            localDriversData = new NativeJaggedArray<LocalDriverData>(drivers.Length, Allocator.Persistent);
            weights = new NativeArray<float>(drivers.Length, Allocator.Persistent);

            for(int i = 0; i < drivers.Length; i++)
            {
                driverData[i] = drivers[i].GetDriverData();
                localDriversData.AllocateRow(i, drivers[i].Lenght);
                drivers[i].CreateLocalDriverData(localDriversData.GetRow(i));
            }
        }

        public void OnShutdown()
        {
            if (driverData.IsCreated) driverData.Dispose();
            if (localDriversData.IsCreated) localDriversData.Dispose();
            if (weights.IsCreated) weights.Dispose();
        }

        public void OnJobComplete()
        {
            for(int i = 0; i < drivers.Length;i++)
            {
                if (drivers[i].IsDirty)
                {
                    drivers[i].IsDirty = false;
                    driverData[i] = drivers[i].GetDriverData();
                    drivers[i].UpdateLocalDriverData(localDriversData.GetRow(i));
                }
                weights[i] = drivers[i].weight;
            }
        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            return new DriverJob
            {
                drivers = driverData,
                localDriverDatas = localDriversData,
                constrains = rig.Constrains.constrains,
                weights = weights,
            }.Schedule(localDriversData.Length, 16, dependsOn);
        }

        [BurstCompile]
        public struct DriverJob : IJobParallelFor
        {
            [ReadOnly] public NativeJaggedArray<LocalDriverData> localDriverDatas;
            [NativeDisableParallelForRestriction] public NativeArray<ConstrainsData> constrains;
            [ReadOnly] public NativeArray<DriverData> drivers;
            [ReadOnly] public NativeArray<float> weights;
            public void Execute(int index)
            {
                DriverData data = drivers[index];
                float weight = math.min(weights[index], 0.99f);
                for (int i = 0; i < localDriverDatas.GetRowLength(index); i++)
                {
                    Vector3 posOffset = Vector3.Lerp(Vector3.zero, localDriverDatas[index,i].MaxPositionOffset, weight);
                    Quaternion rotOffset = Quaternion.Lerp(Quaternion.identity, localDriverDatas[index, i].MaxRotationOffset, weight);

                    if (localDriverDatas[index, i].isUseParent)
                    {
                        Vector3 rootLocalPos = data.localPosition;
                        Quaternion rootLocalRot = data.localRotation;
                        Vector3 rootLocalScale = data.localScale;

                        Matrix4x4 virtualRootLocalMatrix = Matrix4x4.TRS(
                            rootLocalPos + posOffset,
                            rootLocalRot * rotOffset,
                            rootLocalScale
                        );

                        Matrix4x4 parentToWorld = data.parentLocalToWorldMatrix;
                        Matrix4x4 virtualRootWorldMatrix = parentToWorld * virtualRootLocalMatrix;

                        Matrix4x4 transformLocalToRoot = Matrix4x4.TRS(localDriverDatas[index, i].localPosition, localDriverDatas[index, i].localRotation, Vector3.one);

                        Matrix4x4 transformWorldMatrix = virtualRootWorldMatrix * transformLocalToRoot;

                        Matrix4x4 parentWorldToLocal = data.parentWorldToLocalMatrix;
                        Matrix4x4 transformLocalMatrix = parentWorldToLocal * transformWorldMatrix;

                        Vector3 newLocalPosition = transformLocalMatrix.GetColumn(3);
                        Quaternion newLocalRotation = ExtractRotation(transformLocalMatrix);

                        var r = constrains[localDriverDatas[index, i].index];

                        r.internalPositionOffset += newLocalPosition - r.initialLocalPosition;
                        r.internalRotationOffset *= Quaternion.Inverse(r.initialLocalRotation) * newLocalRotation;

                        constrains[localDriverDatas[index, i].index] = r;
                    }
                    else
                    {
                        var r = constrains[localDriverDatas[index, i].index];

                        r.internalPositionOffset += posOffset;
                        r.internalRotationOffset *= rotOffset;

                        constrains[localDriverDatas[index, i].index] = r;
                    }
                }
            }

            public Quaternion ExtractRotation(Matrix4x4 matrix)
            {
                Vector3 forward = matrix.GetColumn(2).normalized;
                Vector3 up = matrix.GetColumn(1).normalized;
                if (forward.sqrMagnitude < 0.0001f || up.sqrMagnitude < 0.0001f)
                    return Quaternion.identity;
                return Quaternion.LookRotation(forward, up);
            }
        }
    }
}