using UnityEngine;

namespace MapLibre.Unity.CameraControl
{
    /// <summary>
    /// Bundle of camera transform parameters used by
    /// <see cref="MapLibreMap.GetFreeCameraOptions"/> /
    /// <see cref="MapLibreMap.SetFreeCameraOptions"/>. Mirrors MapLibre GL JS'
    /// FreeCameraOptions but expressed in Unity world-space coordinates so callers
    /// can interoperate with Cinemachine, dolly tracks, FPS controllers, and any
    /// other system that drives a Unity camera transform directly.
    ///
    /// Setting either field to null leaves the existing camera value untouched.
    /// </summary>
    public struct FreeCameraOptions
    {
        /// <summary>Camera position in Unity world space (relative to map root).</summary>
        public Vector3? Position;

        /// <summary>Camera rotation in Unity world space.</summary>
        public Quaternion? Rotation;

        /// <summary>
        /// Convenience: rotate so the camera at <paramref name="from"/> faces
        /// <paramref name="target"/> with world up. Mirrors GL JS' lookAtPoint().
        /// </summary>
        public static FreeCameraOptions LookAt(Vector3 from, Vector3 target)
        {
            var dir = target - from;
            if (dir.sqrMagnitude < 1e-8f)
                return new FreeCameraOptions { Position = from };
            return new FreeCameraOptions
            {
                Position = from,
                Rotation = Quaternion.LookRotation(dir.normalized, Vector3.up),
            };
        }
    }
}
