using static Proxy.Rig.Constrains;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Jobs;
using Unity.Jobs;
using Unity.Burst;
using System.Collections.Generic;
using System;
using Unity.Collections.LowLevel.Unsafe;
using Proxy.Collections;

namespace Proxy.Rig
{
    public class RigConstrains : IRigBase, IDisposable
    {
        public NativeArray<ConstrainsData> constrains;
        public NativeArray<Vector3> positions;
        public NativeJaggedArray<int> dependeces;
        private Transform[] constrainsTransformArray;
        private TransformAccessArray constrainsAccessArray;

        public void OnInit(Rig rig)
        {
            var m_constrains = rig.GetComponentsInChildren<Constrains>();
            SortByPostOrder(m_constrains);
            constrains = new NativeArray<ConstrainsData>(m_constrains.Length, Allocator.Persistent);
            constrainsTransformArray = new Transform[m_constrains.Length];
            positions = new NativeArray<Vector3>(m_constrains.Length, Allocator.Persistent);
            dependeces = new NativeJaggedArray<int>(m_constrains.Length, Allocator.Persistent);
            for (int i = 0; i < m_constrains.Length; i++)
            {
                constrainsTransformArray[i] = m_constrains[i].transform;
                constrains[i] = m_constrains[i].GetConstrainsData();
            }
            for(int i = 0; i < m_constrains.Length; i++)
            {
                dependeces.AllocateRow(i, m_constrains[i].dependences.Length);
                for(int x = 0; x < m_constrains[i].dependences.Length; x++)
                {
                    dependeces[i, x] = GetIndex(m_constrains[i].dependences[x]);
                }
            }

            constrainsAccessArray = new TransformAccessArray(constrainsTransformArray);
        }

        public int GetIndex(Constrains constrains)
        {
            var t = constrains.transform;
            for (int i = 0; i < constrainsTransformArray.Length; i++)
            {
                if (t == constrainsTransformArray[i])
                    return i;
            }
            return -1;
        }

        public void Dispose()
        {
            OnShutdown();
        }

        public void OnShutdown()
        {
            if (constrains.IsCreated) constrains.Dispose();
            if (constrainsAccessArray.isCreated) constrainsAccessArray.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (dependeces.IsCreated) dependeces.Dispose();
        }

        public void OnJobComplete()
        {

        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            dependsOn = new ConstrainsSet()
            {
                data = constrains,
            }.Schedule(constrainsAccessArray, dependsOn);
            dependsOn = new ReadJob()
            {
                positions = positions,
            }.Schedule(constrainsAccessArray, dependsOn);
            dependsOn = new ConstrainsUpdate()
            {
                data = constrains,
                indices = dependeces,
                positions = positions,
            }.Schedule(dependeces.Length, 8, dependsOn);
            dependsOn = new ConstrainsSet()
            {
                data = constrains,
            }.Schedule(constrainsAccessArray, dependsOn);
            return dependsOn;
        }

        public static void SortByPostOrder(Constrains[] constraints)
        {
            var goToConstrains = new Dictionary<GameObject, Constrains>();
            foreach (var c in constraints)
                goToConstrains[c.gameObject] = c;

            var constrainedGOs = new HashSet<GameObject>(goToConstrains.Keys);

            var roots = new List<GameObject>();
            foreach (var go in constrainedGOs)
            {
                var parent = go.transform.parent;
                if (parent == null || !constrainedGOs.Contains(parent.gameObject))
                    roots.Add(go);
            }

            var sortedList = new List<Constrains>();
            foreach (var root in roots)
                PostOrderTraverse(root, constrainedGOs, goToConstrains, sortedList);
            
            for (int i = 0; i < sortedList.Count; i++)
                constraints[i] = sortedList[i];
        }

        private static void PostOrderTraverse(GameObject current,
                                              HashSet<GameObject> constrainedGOs,
                                              Dictionary<GameObject, Constrains> goToConstrains,
                                              List<Constrains> sortedList)
        {
            foreach (Transform child in current.transform)
            {
                if (constrainedGOs.Contains(child.gameObject))
                    PostOrderTraverse(child.gameObject, constrainedGOs, goToConstrains, sortedList);
            }
            sortedList.Add(goToConstrains[current]);
        }

        [BurstCompile]
        public struct ConstrainsUpdate : IJobParallelFor
        {
            [ReadOnly] public NativeJaggedArray<int> indices;
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] public NativeArray<ConstrainsData> data;
            [ReadOnly] public NativeArray<Vector3> positions;
            public void Execute(int index)
            {
                float distance;
                ConstrainsData d = data[index];
                Vector3 position = positions[index];
                for (int i = 0; i < indices.GetRowLength(index); i++)
                {
                    distance = Vector3.Distance(position, positions[i]);
                    if (d.radius > distance)
                    {
                        Vector3 worldCorrection = (d.radius - distance) * (positions[i] - positions[index]).normalized;
                        Vector3 localCorrection = d.worldToLocalMatrix.MultiplyVector(worldCorrection);
                        Vector3 correctionDir = localCorrection.normalized;
                        float proj = Vector3.Dot(d.internalPositionOffset, correctionDir);
                        if (proj > 0)
                        {
                            float reduction = localCorrection.magnitude;
                            float newProj = Mathf.Max(0, proj - reduction);
                            Vector3 perp = d.internalPositionOffset - correctionDir * proj;
                            d.internalPositionOffset = perp + correctionDir * newProj;
                        }
                    }
                    distance = Vector3.Distance(position, positions[i]);
                    if (d.radius > distance)
                    {
                        distance = (d.radius - distance);
                        distance *= d.worldToLocalMatrix.GetColumn(0).magnitude;
                        d.internalPositionOffset = (d.internalPositionOffset.magnitude - distance) * d.internalPositionOffset.normalized;
                    }
                    distance = Vector3.Distance(position, positions[i]);
                    if (d.radius > distance)
                    {
                        distance = (d.radius - distance);
                        distance *= d.worldToLocalMatrix.GetColumn(0).magnitude;
                        d.internalPositionOffset += distance * data[i].internalPositionOffset.normalized;
                    }
                }
                data[index] = d;
            }
        }

        [BurstCompile]
        public struct ReadJob : IJobParallelForTransform
        {
            [WriteOnly] public NativeArray<Vector3> positions;
            public void Execute(int index, TransformAccess access)
            {
                positions[index] = access.position;
            }
        }

        [BurstCompile]
        public struct ConstrainsSet : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<ConstrainsData> data;
            public void Execute(int index, TransformAccess access)
            {
                access.localPosition = data[index].internalPositionOffset + data[index].initialLocalPosition;
                access.localRotation = data[index].initialLocalRotation * data[index].internalRotationOffset;
            }
        }
    }
}