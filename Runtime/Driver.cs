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
    public class Driver : MonoBehaviour
    {
        [SerializeField] private LocalDriver[] localDrivers;
        [field: SerializeField, Range(0,1)] public float weight { get; set; }

        private void OnValidate()
        {
            IsDirty = true;
        }

        public bool IsDirty { get; set; } = false;

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

        public Rig rig { get; private set; }

        public void OnStart(Rig rig)
        {
            this.rig = rig;

            weight = 0;
        }

        public int Lenght => localDrivers.Length;

        public void CreateLocalDriverData(NativeSlice<LocalDriverData> slice)
        {
            for (int i = 0; i < localDrivers.Length; i++)
            {
                slice[i] = localDrivers[i].GetData(this);
            }
        }

        public void UpdateLocalDriverData(NativeSlice<LocalDriverData> slice)
        {
            for (int i = 0; i < localDrivers.Length; i++)
            {
                slice[i] = slice[i].UpdateData(localDrivers[i]);
            }
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
    }
}
