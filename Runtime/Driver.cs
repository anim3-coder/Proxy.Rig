using Proxy.Inspector;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Proxy.Rig.Constrains;

namespace Proxy.Rig
{
    public class Driver : MonoBehaviour, IDisposable
    {
        [SerializeField] private LocalDriver[] localDrivers;
        [field: SerializeField, Range(0,1)] public float weight { get; set; }

        private void OnValidate()
        {
            IsDirty = true;
        }

        private bool IsDirty = false;

        public struct DriverData
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public Matrix4x4 parentLocalToWorldMatrix;
            public Matrix4x4 parentWorldToLocalMatrix;
            public Matrix4x4 worldToLocalMatrix;
        
            public DriverData(Transform transform)
            {
                localPosition = transform.localPosition;
                localRotation = transform.localRotation;
                localScale = transform.localScale;
                worldToLocalMatrix = transform.worldToLocalMatrix;
                parentLocalToWorldMatrix = transform.parent.localToWorldMatrix;
                parentWorldToLocalMatrix = transform.parent.worldToLocalMatrix;
            }
        }

        public DriverData GetDriverData()
        {
            return new DriverData(transform);
        }

        public NativeArray<LocalDriverData> localDriverDatas;

        public Rig rig { get; private set; }

        public void OnStart(Rig rig)
        {
            this.rig = rig;

            weight = 0;

            localDriverDatas = new NativeArray<LocalDriverData>(localDrivers.Length, Allocator.Persistent);
            
            for (int i = 0; i < localDrivers.Length; i++)
            {
                localDriverDatas[i] = localDrivers[i].GetData(this);
            }
        }

        public void OnJobComplete()
        {
            if (IsDirty)
            {
                IsDirty = false;
                for (int i = 0; i < localDrivers.Length; i++)
                {
                    localDriverDatas[i] = localDriverDatas[i].UpdateData(localDrivers[i]);
                }
            }
        }

        public void OnShutdown()
        {
            localDriverDatas.Dispose();
        }

        public void Dispose()
        {
            OnShutdown();
        }

        public JobHandle OnStartJob(JobHandle dependsOn)
        {
            return new DriverJob()
            {
                data = GetDriverData(),
                localDriverDatas = localDriverDatas.AsReadOnly(),
                constrains = rig.Constrains.constrains,
                weight = math.min(weight,0.99f)
            }.Schedule(localDriverDatas.Length, 16, dependsOn);
        }

        public struct LocalDriverData
        {
            public bool isUseParent;
            public int index;
            public Vector3 MaxPositionOffset;
            public Quaternion MaxRotationOffset;
            public Vector3 localPosition;
            public Quaternion localRotation;

            public LocalDriverData UpdateData(LocalDriver driver)
            {
                MaxPositionOffset = driver.MaxPositionOffset;
                MaxRotationOffset = driver.MaxRotationOffset;
                return this;
            }
        }

        [Serializable]
        public class LocalDriver
        {
            public bool isUseParent = true;
            public Constrains constrains;
            public Vector3 MaxPositionOffset;
            public Quaternion MaxRotationOffset;

            public LocalDriverData GetData(Driver root)
            {
                return new LocalDriverData
                {
                    isUseParent = isUseParent,
                    index = root.rig.Constrains.GetIndex(constrains),
                    localPosition = root.transform.InverseTransformPoint(constrains.transform.position),
                    localRotation = Quaternion.Inverse(root.transform.rotation) * constrains.transform.rotation,
                    MaxPositionOffset = MaxPositionOffset,
                    MaxRotationOffset = MaxRotationOffset,
                };
            }
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
        [BurstCompile]
        public struct DriverJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<LocalDriverData>.ReadOnly localDriverDatas;
            [NativeDisableParallelForRestriction] public NativeArray<ConstrainsData> constrains;
            [ReadOnly] public DriverData data;
            [ReadOnly] public float weight;
            public void Execute(int index)
            {
                Vector3 posOffset = Vector3.Lerp(Vector3.zero, localDriverDatas[index].MaxPositionOffset, weight);
                Quaternion rotOffset = Quaternion.Lerp(Quaternion.identity, localDriverDatas[index].MaxRotationOffset, weight);

                if (localDriverDatas[index].isUseParent)
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

                    Matrix4x4 transformLocalToRoot = Matrix4x4.TRS(localDriverDatas[index].localPosition, localDriverDatas[index].localRotation, Vector3.one);

                    Matrix4x4 transformWorldMatrix = virtualRootWorldMatrix * transformLocalToRoot;

                    Matrix4x4 parentWorldToLocal = data.parentWorldToLocalMatrix;
                    Matrix4x4 transformLocalMatrix = parentWorldToLocal * transformWorldMatrix;

                    Vector3 newLocalPosition = transformLocalMatrix.GetColumn(3);
                    Quaternion newLocalRotation = ExtractRotation(transformLocalMatrix);

                    var r = constrains[localDriverDatas[index].index];

                    r.internalPositionOffset += newLocalPosition - r.initialLocalPosition;
                    r.internalRotationOffset *= Quaternion.Inverse(r.initialLocalRotation) * newLocalRotation;

                    constrains[localDriverDatas[index].index] = r;
                }
                else
                {
                    var r = constrains[localDriverDatas[index].index];

                    r.internalPositionOffset += posOffset;
                    r.internalRotationOffset *= rotOffset;

                    constrains[localDriverDatas[index].index] = r;
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
