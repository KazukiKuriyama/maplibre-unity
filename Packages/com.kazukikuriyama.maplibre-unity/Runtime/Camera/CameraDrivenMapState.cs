using UnityEngine;

namespace MapLibre.Unity.CameraControl
{
    /// <summary>
    /// Lets an external camera rig (Cinemachine, Timeline, FPS controllers,
    /// custom orbit scripts) drive the map's camera transform while keeping
    /// <see cref="MapState"/> -- and therefore tile loading -- synchronised
    /// every frame.
    /// </summary>
    /// <remarks>
    /// Without this component, calling <see cref="MapLibreMap.SetFreeCameraOptions"/>
    /// only derives map state once. Continuous external control (Cinemachine
    /// blends, Timeline animation, smooth-follow rigs) leaves the map's
    /// center / zoom / bearing / pitch frozen at the moment of the call, so
    /// tiles are loaded for the wrong area.
    /// <para>
    /// Adding this component to the same GameObject as a
    /// <see cref="MapLibreMap"/> tells the map to derive state from the
    /// camera transform once per frame in <see cref="LateUpdate"/>, after
    /// the external rig has finished writing the camera. The map's own
    /// <see cref="MapCamera"/> automatically goes into
    /// <see cref="MapCamera.ExternallyDriven"/> mode so it does not fight
    /// the rig.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(MapLibreMap))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // After Cinemachine / Animator / Timeline writes
    public class CameraDrivenMapState : MonoBehaviour
    {
        [Tooltip("When false, the component is dormant and the map keeps " +
                 "managing its camera normally. Toggle this from a director " +
                 "script (cutscene start / end) to swap control on the fly.")]
        [SerializeField] private bool _enabled = true;

        private MapLibreMap _map;

        /// <summary>Enable / disable continuous derivation. Mirrors the
        /// inspector toggle but works at runtime.</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (!_enabled && _map != null) _map.ResumeMapDrivenCamera();
            }
        }

        private void Awake()
        {
            _map = GetComponent<MapLibreMap>();
        }

        private void LateUpdate()
        {
            if (!_enabled || _map == null || !_map.IsInitialized) return;

            // SetFreeCameraOptions(default) flips MapCamera.ExternallyDriven
            // on and re-derives center / zoom / bearing / pitch from the
            // current camera transform. Calling it every frame keeps state
            // synchronised even when Cinemachine smoothly blends across
            // virtual cameras.
            _map.SetFreeCameraOptions(default);
        }

        private void OnDisable()
        {
            // Restore map-driven camera so toggling the component off mid-play
            // doesn't strand the camera in external mode.
            if (_map != null) _map.ResumeMapDrivenCamera();
        }
    }
}
