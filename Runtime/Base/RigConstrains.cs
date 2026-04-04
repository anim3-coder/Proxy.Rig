using static Proxy.Rig.Constrains;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Jobs;
using Unity.Jobs;
using Unity.Burst;
using System.Collections.Generic;
using System;
using Unity.Collections.LowLevel.Unsafe;

namespace Proxy.Rig
{
    public class RigConstrains : IRigBase, IDisposable
    {
        public NativeArray<ConstrainsData> constrains;
        public NativeArray<Vector3> positions;
        public NativeArray<int>[] dependeces;
        private Transform[] constrainsTransformArray;
        private TransformAccessArray constrainsAccessArray;

        public void OnInit(Rig rig)
        {
            var m_constrains = rig.GetComponentsInChildren<Constrains>();
            SortByPostOrder(m_constrains);
            constrains = new NativeArray<ConstrainsData>(m_constrains.Length, Allocator.Persistent);
            constrainsTransformArray = new Transform[m_constrains.Length];
            positions = new NativeArray<Vector3>(m_constrains.Length, Allocator.Persistent);
            dependeces = new NativeArray<int>[m_constrains.Length];
            for (int i = 0; i < m_constrains.Length; i++)
            {
                constrainsTransformArray[i] = m_constrains[i].transform;
                constrains[i] = m_constrains[i].GetConstrainsData();
            }
            for(int i = 0; i < m_constrains.Length; i++)
            {
                dependeces[i] = m_constrains[i].GetDependenesIndices(this);
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
            for(int i = 0; i < dependeces.Length; i++)
                dependeces[i].Dispose();
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
            dependsOn = MultiPass(dependsOn, constrains.Length, ConstrainsUpdateJob);
            dependsOn = new ConstrainsSet()
            {
                data = constrains,
            }.Schedule(constrainsAccessArray, dependsOn);
            return dependsOn;
        }

        public JobHandle ConstrainsUpdateJob(int index, JobHandle dependsOn)
        {
            return new ConstrainsUpdate()
            {
                positions = positions,
                data = constrains,
                index = index,
                indices = dependeces[index]
            }.Schedule(dependsOn);
        }

        public JobHandle MultiPass(JobHandle dependsOn, int length, System.Func<int, JobHandle, JobHandle> Action)
        {
            NativeArray<JobHandle> jobs = new NativeArray<JobHandle>(length, Allocator.TempJob);
            for (int i = 0; i < length; i++)
            {
                jobs[i] = Action.Invoke(i, dependsOn);
            }
            return jobs.Dispose(JobHandle.CombineDependencies(jobs));
        }

        public static void SortByPostOrder(Constrains[] constraints)
        {
            // Map each GameObject to its Constrains component for quick lookup
            var goToConstrains = new Dictionary<GameObject, Constrains>();
            foreach (var c in constraints)
                goToConstrains[c.gameObject] = c;

            // Set of all GameObjects that have a Constrains component
            var constrainedGOs = new HashSet<GameObject>(goToConstrains.Keys);

            // Find roots: constrained objects whose parent is either null or not constrained
            var roots = new List<GameObject>();
            foreach (var go in constrainedGOs)
            {
                var parent = go.transform.parent;
                if (parent == null || !constrainedGOs.Contains(parent.gameObject))
                    roots.Add(go);
            }

            // Perform post-order traversal on each root and collect components
            var sortedList = new List<Constrains>();
            foreach (var root in roots)
                PostOrderTraverse(root, constrainedGOs, goToConstrains, sortedList);

            // Overwrite the original array with the sorted order
            for (int i = 0; i < sortedList.Count; i++)
                constraints[i] = sortedList[i];
        }

        private static void PostOrderTraverse(GameObject current,
                                              HashSet<GameObject> constrainedGOs,
                                              Dictionary<GameObject, Constrains> goToConstrains,
                                              List<Constrains> sortedList)
        {
            // First, recursively process all constrained children
            foreach (Transform child in current.transform)
            {
                if (constrainedGOs.Contains(child.gameObject))
                    PostOrderTraverse(child.gameObject, constrainedGOs, goToConstrains, sortedList);
            }
            // Then add the current component
            sortedList.Add(goToConstrains[current]);
        }

        [BurstCompile]
        public struct ConstrainsUpdate : IJob
        {
            [ReadOnly] public int index;
            [ReadOnly] public NativeArray<int> indices;
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] public NativeArray<ConstrainsData> data;
            [ReadOnly] public NativeArray<Vector3> positions;
            public void Execute()
            {
                float distance;
                ConstrainsData d = data[index];
                Vector3 position = positions[index];
                foreach (int i in indices)
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