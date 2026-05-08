using UnityEngine;

namespace MapLibre.Unity.CameraControl
{
    /// <summary>
    /// Pins a Unity <see cref="Transform"/> to a geographic coordinate. The
    /// component repositions its own GameObject every <see cref="LateUpdate"/>
    /// to match <see cref="LngLat"/> in the map's world space.
    /// </summary>
    /// <remarks>
    /// Designed as a generic bridge to any external camera rig that drives a
    /// Unity Camera by referencing a target Transform. The most common use
    /// case is Cinemachine -- assign the GameObject hosting this component to
    /// <c>CinemachineCamera.Target.LookAtTarget</c> /
    /// <c>TrackingTarget</c> and the virtual camera will track a real-world
    /// coordinate. Animator-driven rigs, Timeline tracks, FPS controllers,
    /// and custom orbit scripts all work the same way.
    /// <para>
    /// This component does not move the actual <see cref="UnityEngine.Camera"/>;
    /// it only provides a Transform target. To let an external rig drive the
    /// map camera (so tiles load for the area being looked at), pair this
    /// component with <see cref="CameraDrivenMapState"/>.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)] // After MapLibreMap.Update so we read the latest state
    public class MapCameraTarget : MonoBehaviour
    {
        [Tooltip("Map whose coordinate system the LngLat is resolved against.")]
        [SerializeField] private MapLibreMap _map;

        [Tooltip("Longitude in degrees (-180..180).")]
        [SerializeField] private double _longitude;

        [Tooltip("Latitude in degrees (-90..90).")]
        [SerializeField] private double _latitude;

        [Tooltip("Y offset in Unity world units, applied after the geographic projection.")]
        [SerializeField] private float _heightOffset;

        /// <summary>The map this target is anchored against.</summary>
        public MapLibreMap Map
        {
            get => _map;
            set => _map = value;
        }

        /// <summary>Geographic position the Transform should track.</summary>
        public LngLat LngLat
        {
            get => new LngLat(_longitude, _latitude);
            set { _longitude = value.Longitude; _latitude = value.Latitude; }
        }

        /// <summary>Y offset added in Unity world space after projection.</summary>
        public float HeightOffset
        {
            get => _heightOffset;
            set => _heightOffset = value;
        }

        private void LateUpdate()
        {
            if (_map == null || _map.State == null) return;
            var pos = _map.LngLatToWorld(LngLat);
            if (!pos.HasValue) return;
            var p = pos.Value;
            p.y += _heightOffset;
            transform.position = p;
        }
    }
}
