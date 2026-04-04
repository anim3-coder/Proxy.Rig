
using Unity.Jobs;

namespace Proxy.Rig
{
    public interface IRigBase
    {
        void OnInit(Rig rig);
        void OnShutdown();
        JobHandle StartJob(JobHandle dependsOn);
        public void OnJobComplete();
    }
}
