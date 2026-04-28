# Camera Animation

MapLibre Unity exposes the same camera-control surface as
[MapLibre GL JS][gljs] — `easeTo`, `flyTo`, `fitBounds`, plus
`jumpTo`, `panBy`, `rotateTo`, and `zoomTo`. Each method has an
`Async` sibling returning a Unity 6 `Awaitable` so you can `await`
animations from coroutines or async methods.

[gljs]: https://maplibre.org/maplibre-gl-js/docs/API/classes/Map/

## Durations are in Milliseconds

To match the upstream MapLibre GL JS API, **all `Duration` /
`durationMs` values are milliseconds**. A 600 ms ease is `600f`, not
`0.6f`. This is a common foot-gun when porting Unity coroutine code
that uses seconds.

## EaseTo

Animate to a target camera state along a smooth curve. Any field left
`null` retains its current value.

```csharp
map.EaseTo(new EaseToOptions {
    Center   = new LngLat(139.7670, 35.6814),
    Zoom     = 14f,
    Bearing  = 30f,
    Pitch    = 45f,
    Duration = 800f,
    Easing   = MapAnimator.EasingFunction.EaseInOut,
});
```

Awaitable variant:

```csharp
await map.EaseToAsync(new EaseToOptions {
    Center   = new LngLat(139.7670, 35.6814),
    Zoom     = 14f,
    Duration = 800f,
}, cancellationToken);
```

The task completes when the animation finishes or when it is canceled
(by user input, another animation request, or the cancellation token).
A canceled animation throws `OperationCanceledException` from the
`Async` variant — wrap the await in a try/catch if you need to react.

## FlyTo

Zoom out, fly across the globe, zoom back in — the
[van Wijk easing curve][vanwijk] used by upstream MapLibre. Best for
long-distance moves where a straight ease would feel slow.

[vanwijk]: https://www.win.tue.nl/~vanwijk/zoompan.pdf

```csharp
await map.FlyToAsync(new FlyToOptions {
    Center   = new LngLat(2.3522, 48.8566),  // Paris
    Zoom     = 12f,
    Bearing  = 0f,
    Pitch    = 0f,
    Duration = 4500f,
});
```

If you do not specify a `Duration`, the library picks one proportional
to the great-circle distance — same heuristic as MapLibre GL JS.

## FitBounds / FitPoints

Fit a bounding box (or a point cloud) into the viewport with optional
padding.

```csharp
var bounds = new LngLatBounds(
    sw: new LngLat(139.5, 35.5),
    ne: new LngLat(139.9, 35.8));

await map.FitBoundsAsync(bounds, new FitBoundsOptions {
    Padding = new PaddingOptions { Top = 40, Right = 40, Bottom = 40, Left = 40 },
    MaxZoom = 16f,
});
```

`FitPoints(IReadOnlyList<LngLat>)` is a convenience wrapper that
computes the bounds for you.

## JumpTo / PanBy / ZoomTo / RotateTo

| Method | Use case |
|---|---|
| `JumpTo(JumpToOptions)` | Instant teleport, no animation. |
| `SetCenter`, `SetZoom`, `SetBearing`, `SetPitch` | Single-axis instant set. |
| `PanBy(Vector2 offsetPixels, durationMs)` | Pan by N screen pixels. |
| `ZoomTo(zoom, durationMs)` | Animate zoom only, keep center. |
| `RotateTo(bearing, durationMs)` | Animate bearing only. |
| `ResetNorth(durationMs)` | Shorthand for `RotateTo(0, durationMs)`. |

All animated variants have `Async` siblings on `MapLibreMap`:

```csharp
await map.ZoomToAsync(15f, durationMs: 500f);
await map.RotateToAsync(0f,  durationMs: 800f);
await map.PanToAsync(new LngLat(...), durationMs: 600f);
```

## Cancellation

Animations cancel when:

- The user interacts with the map (drag, scroll, rotate, pitch). The
  `MapInputHandler` raises an interaction event that the `MapAnimator`
  listens for and stops the running animation.
- A new animation is requested. The previous one stops at its current
  pose; the new one takes over.
- The `CancellationToken` passed to an `Async` variant is signaled.
- You call `map.GetMapAnimator().CancelAnimation()` directly.

Subscribe to animation lifecycle events:

```csharp
var animator = map.GetMapAnimator();
animator.OnAnimationStart += () => Debug.Log("animation started");
animator.OnAnimationEnd   += () => Debug.Log("animation ended");
```

`animator.IsAnimating` is true while an animation is running.

## OnceAsync

Wait for a specific map event without writing a manual subscribe /
unsubscribe pair:

```csharp
await map.LoadAsync();                          // wait for first style load
await map.OnceAsync(MapEventType.MoveEnd);      // wait for next move-end
await map.OnceAsync(MapEventType.StyleData);    // wait for next style swap
```

Combined with the camera animation `Async` methods this gives you a
clean script-driven cinematic flow:

```csharp
async Task PlayIntroAsync(CancellationToken ct)
{
    await map.LoadAsync(ct);
    await map.FlyToAsync(new FlyToOptions {
        Center = new LngLat(139.7670, 35.6814),
        Zoom   = 13f,
        Duration = 4000f,
    }, ct);
    await map.RotateToAsync(45f, durationMs: 1500f);
}
```

## Free Camera

For external rigs (orbit cameras, Cinemachine, custom dolly tracks),
drive the camera through `FreeCameraOptions` instead of the animation
helpers. See the **FreeCameraDemo** sample (import via Package Manager) for a
working example.

## See Also

- **CameraAnimationDemo** sample — `EaseTo` / `FlyTo` / `ZoomTo` /
  `RotateTo` / `PanBy` / `JumpTo` side by side.
- **FitBoundsDemo** sample — `FitBounds` with padding.
- [MapLibre GL JS Camera API][gljs] — upstream reference; the Unity
  port mirrors the API surface.
