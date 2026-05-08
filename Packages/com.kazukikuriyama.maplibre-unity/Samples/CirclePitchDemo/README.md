# Circle Pitch Demo

Side-by-side comparison of the four `circle-pitch-alignment` ×
`circle-pitch-scale` combinations from the Style Spec.

| Column        | pitch-alignment | pitch-scale | Visual behaviour                                  |
| ------------- | --------------- | ----------- | ------------------------------------------------- |
| 🔴 red        | `map`           | `map`       | Lays flat on the ground; foreshortens with pitch. |
| 🟠 orange     | `map`           | `viewport`  | Lays flat on the ground; constant screen-px size. |
| 🟢 green      | `viewport`      | `map`       | Billboard facing camera; shrinks with depth (spec default). |
| 🔵 blue       | `viewport`      | `viewport`  | Billboard facing camera; constant screen-px size. |

Scene starts at `pitch=55°` so the differences are immediate. Middle-drag
to vary pitch and watch how each column reacts (shrinks vs holds size,
lays flat vs stays upright).
