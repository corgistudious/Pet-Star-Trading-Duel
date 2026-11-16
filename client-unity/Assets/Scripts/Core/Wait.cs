using System.Threading.Tasks;
using UnityEngine;

namespace PetStar.Client.Core
{
    /// <summary>Main-thread delay that works on every platform, including WebGL (no threads, so Task.Delay's timer
    /// is not reliable there). It yields to Unity's synchronization context each frame until the time has passed.</summary>
    public static class Wait
    {
        public static async Task Seconds(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) await Task.Yield();
        }
    }
}
