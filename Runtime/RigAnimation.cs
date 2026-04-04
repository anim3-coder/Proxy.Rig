using System.Collections;
using UnityEngine;

namespace Proxy.Rig
{
    public class RigAnimation : MonoBehaviour
    {
        [field: SerializeField] private LocalAnimation[] drivers;
        [field: SerializeField] public float weight {  get; set; }

        public void Update()
        {
            for(int i = 0; i < drivers.Length; i++)
            {
                drivers[i].Update(weight);
            }
        }

        [System.Serializable]
        public class LocalAnimation
        {
            public AnimationCurve curve;
            public Driver driver;

            public void Update(float weight)
            {
                driver.weight = curve.Evaluate(weight);
            }
        }
    }
}