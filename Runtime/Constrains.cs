using System.Collections;
using UnityEngine;
using Vertx.Debugging;
using Unity.Burst;
using Unity.Collections;

namespace Proxy.Rig
{
    [BurstCompile]
    public class Constrains : MonoBehaviour
    {
        [field: SerializeField] public Constrains[] dependences { get; private set; }
        [field: SerializeField] public float size { get; private set; } = 0.5f;

        [BurstCompile]
        public struct ConstrainsData
        {
            public Vector3 internalPositionOffset;
            public Quaternion internalRotationOffset;
            public Vector3 initialLocalPosition;
            public Quaternion initialLocalRotation;
            public Matrix4x4 worldToLocalMatrix;
            public float radius;
        }

        public ConstrainsData GetConstrainsData()
        {
            return new ConstrainsData
            {
                internalPositionOffset = Vector3.zero,
                internalRotationOffset = Quaternion.identity,
                initialLocalPosition = transform.localPosition,
                initialLocalRotation = transform.localRotation,
                worldToLocalMatrix = transform.worldToLocalMatrix,
                radius = (1 / transform.lossyScale.x) * size,
            };
        }
        
        public NativeArray<int> GetDependenesIndices(RigConstrains rig)
        {
            var indices = new NativeArray<int>(dependences.Length, Allocator.Persistent);
            for(int i = 0; i < indices.Length; i++)
            {
                indices[i] = rig.GetIndex(dependences[i]);
            }
            return indices;
        }

        private void OnDrawGizmos()
        {
            D.raw(new Shape.Box2D(transform.position, Quaternion.Euler(90,90,0) * transform.rotation, (1/transform.lossyScale.x) * size), color: Color.whiteSmoke);                   
        }
    }
}