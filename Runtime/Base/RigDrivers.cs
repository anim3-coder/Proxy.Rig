using System.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Proxy.Rig
{
    public class RigDrivers : IRigBase
    {
        private Driver[] drivers;
        public void OnInit(Rig rig)
        {
            drivers = rig.GetComponentsInChildren<Driver>();
            foreach (var driver in drivers)
            {
                driver.OnStart(rig);
            }
        }

        public void OnShutdown()
        {
            foreach (var driver in drivers)
            {
                driver.OnShutdown();
            }
        }

        public void OnJobComplete()
        {
            foreach (var driver in drivers)
            {
                driver.OnJobComplete();
            }
        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            foreach (var driver in drivers)
            {
                dependsOn = driver.OnStartJob(dependsOn);
            }
            return dependsOn;
        }
    }
}