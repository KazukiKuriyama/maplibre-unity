namespace MapLibre.Unity
{
    /// <summary>
    /// Map event types matching MapLibre GL JS event names.
    /// See: https://maplibre.org/maplibre-gl-js/docs/API/type-aliases/MapEventType/
    /// </summary>
    public enum MapEventType
    {
        // Lifecycle
        Load,
        Idle,

        // Movement (any state change: center, zoom, bearing, pitch)
        MoveStart,
        Move,
        MoveEnd,

        // Zoom
        ZoomStart,
        Zoom,
        ZoomEnd,

        // Rotation (bearing)
        RotateStart,
        Rotate,
        RotateEnd,

        // Pitch
        PitchStart,
        Pitch,
        PitchEnd,

        // Mouse / pointer
        Click,
        DblClick,
        MouseDown,
        MouseUp,
        MouseMove,
        MouseEnter,
        MouseLeave,
        ContextMenu,

        // Touch
        TouchStart,
        TouchEnd,
        TouchMove,
        TouchCancel,

        // Mouse wheel (raw scroll input, distinct from zoom)
        Wheel,

        // Drag (left-button pan)
        DragStart,
        Drag,
        DragEnd,

        // Box zoom (shift + drag rectangle)
        BoxZoomStart,
        BoxZoomEnd,
        BoxZoomCancel,

        // Data lifecycle
        SourceAdd,
        SourceRemove,
        LayerAdd,
        LayerRemove,
        StyleData,
        SourceData,
        DataLoading,

        // Error
        Error,

        // Viewport
        Resize,

        // Map disposal
        Remove,
    }
}
