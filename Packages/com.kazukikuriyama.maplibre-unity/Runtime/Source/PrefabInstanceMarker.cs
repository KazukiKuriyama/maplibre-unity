using UnityEngine;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Bookkeeping component attached to every spawned PrefabInstance root so
    /// a <see cref="Physics.Raycast"/> hit can be resolved back to the owning
    /// <see cref="PrefabInstance"/> via
    /// <see cref="Component.GetComponentInParent{T}()"/>. Hidden from the
    /// AddComponent menu so users do not add it manually.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class PrefabInstanceMarker : MonoBehaviour
    {
        /// <summary>The instance that owns this GameObject hierarchy.</summary>
        [System.NonSerialized]
        public PrefabInstance Instance;
    }
}
