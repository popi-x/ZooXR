# ZooBuilder Scripts Guide

> Auto-reminder: this file should be updated after every commit that modifies `Assets/ZooBuilder/Scripts/`.
> Last updated: 2026-04-07

---

## Recommended GameObject Hierarchy

```
Scene
├── AR Session
├── AR Session Origin
│   └── AR Camera
├── ZooManager          ← ZooManager, EnclosurePlacer, ZooObjectPlacer,
│                          PathCreator, ZooTransformer, UndoRedoManager,
│                          ZooSaveManager, ZooRequirementsChecker,
│                          ZooInputHandler, ZooARPlaneWatcher
├── ZooRoot             ← empty GameObject, parent of all enclosures/objects
└── Canvas
    └── UIManager       ← ZooUIManager
```

---

## Scripts

### ZooEnums.cs
Pure enum definitions — no Inspector setup needed.

| Enum | Values |
|------|--------|
| `BuildPhase` | DetectingPlane → PlacingEnclosures → CreatingPath → PlacingObjects → Complete |
| `EnclosureType` | None, Enclosure1, Enclosure2, Enclosure3 |
| `ZooObjectType` | Fence, Gate, Bin, Animal |
| `AnimalBehaviorType` | Static, Roaming, Hungry |
| `PlacementMode` | None, EnclosureFloor, Fence, Gate, Bin, Animal, Path |

---

### ZooManager.cs
**Attach to:** `ZooManager` GameObject.

| Inspector Field | Description |
|----------------|-------------|
| `Zoo Root` | Empty parent Transform for all zoo objects |
| `Min Enclosure Size` | Minimum enclosure dimension in metres (default 1.0) |

Key API:
- `ZooManager.Instance` — singleton access
- `SetPlacementMode(mode)` — switches/toggles the current tool
- `TryStartPathCreation()` — locks enclosures and enters path phase (needs 3 enclosures)
- `GetEnclosureAt(worldPos)` — returns which enclosure a world point is inside

---

### EnclosureFloor.cs
**Auto-created** by `EnclosurePlacer` — do not add manually.

Prefab requirements: `MeshFilter`, `MeshRenderer`, `MeshCollider`, `EnclosureFloor`.

Key rules enforced automatically:
- Cannot be moved/scaled when it has objects on it
- Cannot be modified after path creation has started
- Overlap detection via point-in-polygon test

---

### EnclosurePlacer.cs
**Attach to:** `ZooManager` GameObject.

| Inspector Field | Description |
|----------------|-------------|
| `AR Raycast Manager` | Scene's ARRaycastManager |
| `AR Camera` | AR Camera reference |
| `Enclosure Floor Prefab 1` | Prefab for Enclosure 1 (static animals) |
| `Enclosure Floor Prefab 2` | Prefab for Enclosure 2 (roaming animals) |
| `Enclosure Floor Prefab 3` | Prefab for Enclosure 3 (hungry animal) |
| `Face Camera` | Rotate enclosure to face camera on placement (default on) |

**Debug / Testing:**
| Inspector Field | Description |
|----------------|-------------|
| `Debug Fallback Placement` | If AR raycast finds no plane, place in front of camera instead |
| `Debug Place Distance` | Distance in front of camera for fallback (default 1.5 m) |

**Placement flow (no ghost — tap to place directly):**
1. `BeginPlacement()` → PlacementMode set to EnclosureFloor
2. Player taps AR plane → enclosure placed at hit position, auto-selected for gesture editing
3. Player gestures to resize/reposition (via `ZooInputHandler`)
4. Player taps **Next** → `BeginPlacement()` called for next type; **Next** becomes **Draw Path** after all 3
5. Once path phase starts, all enclosures are locked

On real device: requires a detected AR horizontal plane.  
In editor/simulator: enable `Debug Fallback Placement` to place without a real plane.

---

### ZooObject.cs
**Attach to:** every fence / gate / bin / animal prefab.

| Inspector Field | Description |
|----------------|-------------|
| `Object Type` | Fence / Gate / Bin / Animal |
| `Prefab Id` | Unique string matching `ZooObjectPlacer` library (e.g. `"fence"`) |

API:
- `Translate(worldDelta)` — moves the object, clamped inside the enclosure
- `RotateY(degrees)` — rotates about vertical axis
- `OverlapsAny()` — true if colliding with a sibling object

---

### FenceSegment.cs
**Attach to:** fence prefabs (alongside `ZooObject`).

| Inspector Field | Description |
|----------------|-------------|
| `Min Length` | Shortest allowed length in metres (default 0.3) |
| `Max Length` | Longest allowed length in metres (default 5.0) |
| `Base Length` | Prefab's natural length when scale X = 1 |

Only the **X axis** is scaled — height and width stay fixed.

API:
- `SetLength(float metres)` — set absolute length
- `AdjustLength(float delta)` — increase/decrease length

---

### AnimalController.cs
**Attach to:** animal prefabs (alongside `ZooObject`).

| Enclosure | Behavior | Description |
|-----------|----------|-------------|
| Enclosure 1 | Static | Stands still |
| Enclosure 2 | Roaming | Random waypoint wandering inside enclosure |
| Enclosure 3 | Hungry | Aggressive back-and-forth pacing |

Behavior is set automatically at placement via `SetBehaviorFromEnclosure()`.

| Inspector Field | Description |
|----------------|-------------|
| `Roam Speed` | Movement speed for roaming animals (default 0.3 m/s) |
| `Roam Radius` | Max wander distance from spawn point (default 1.5 m) |
| `Hungry Speed` | Speed for hungry animal pacing (default 0.5 m/s) |

---

### ZooObjectPlacer.cs
**Attach to:** `ZooManager` GameObject.

| Inspector Field | Description |
|----------------|-------------|
| `Prefab Library` | List of `{id, prefab}` entries — one per placeable object |
| `Fence/Gate/Bin Prefab Id` | IDs matching library entries (default: `"fence"`, `"gate"`, `"bin"`) |
| `Animal Enc1/2/3 Id` | Animal prefab IDs per enclosure type |
| `Enclosure Layer Mask` | Layer used by enclosure floor colliders |
| `Ghost Object` | Optional placement preview (crosshair/shadow) |

Objects can only be placed **on an enclosure floor** and must not overlap existing objects.

---

### PathCreator.cs
**Attach to:** `ZooManager` GameObject.

| Inspector Field | Description |
|----------------|-------------|
| `AR Raycast Manager` | Scene's ARRaycastManager |
| `Path Line` | LineRenderer for the drawn path |
| `Path Segment Prefab` | Optional discrete segment prefab (tap-to-place alternative) |
| `Connection Radius` | How close a path point must be to an enclosure to count as connected (default 0.5 m) |

**Workflow:**
1. Tap "Begin Draw" → drag finger across AR plane
2. OR tap repeatedly to place discrete segments
3. "Finalize" button activates once all 3 enclosures are reached

---

### ZooTransformer.cs
**Attach to:** `ZooManager` GameObject.

Gestures are driven by `ZooInputHandler` — no longer has its own Update loop.

| Inspector Field | Description |
|----------------|-------------|
| `Min/Max Scale` | Scale range (default 0.1 – 5.0) |
| `Rotate Sensitivity` | Twist gesture sensitivity |
| `Scale Sensitivity` | Pinch gesture sensitivity |

Public API called by `ZooInputHandler`:
- `HandleTwoFingerGesture(pinchDelta, twistDelta, midpoint)` — apply pinch/twist to whole zoo

⚠ Transformations affect AR preview only — they do not change saved layout data.

---

### UndoRedoManager.cs
**Attach to:** `ZooManager` GameObject.

| Inspector Field | Description |
|----------------|-------------|
| `Max History` | Undo levels (default 10) |

Undo/redo **is tracked** for: place object, delete object, translate object, rotate object.  
Undo/redo **is NOT tracked** for: enclosure/path creation-deletion, floor transforms, whole-zoo transforms.

Wire UI buttons to `UndoRedoManager.Instance.Undo()` / `.Redo()`.

---

### ZooSaveManager.cs
**Attach to:** `ZooManager` GameObject.

| Inspector Field | Description |
|----------------|-------------|
| `File Name` | Output filename (default `zoo_layout.json`) |
| `Path Creator` | Reference to the scene's PathCreator |

Saves to `Application.persistentDataPath/zoo_layout.json`.  
Format: JSON, 1 Unity unit = 1 metre, positions in ZooRoot-local space.

---

### ZooRequirementsChecker.cs
**Attach to:** `ZooManager` GameObject.

| Inspector Field | Description |
|----------------|-------------|
| `Path Creator` | Reference to the scene's PathCreator |

`CheckAll()` returns a `RequirementResult` listing every pass/fail.  
`AllRequirementsMet()` — convenience bool used by `ZooUIManager` to gate saving.

Checks performed:
- 3 enclosures placed
- Path finalized and connects all 3 enclosures
- Each enclosure: fence segments, ≥1 gate, ≥1 bin
- Animal counts (Enc1: ≥2 static, Enc2: ≥2 roaming, Enc3: ≥1 hungry)
- Different animal species across enclosures

---

### ZooUIManager.cs
**Attach to:** a GameObject under Canvas.

Connect all `[SerializeField]` references in the Inspector:

**Phase panels:** `m_PanelDetecting`, `m_PanelPlacingEnclosures`, `m_PanelPathCreation`, `m_PanelPlacingObjects`, `m_PanelComplete`

**Enclosure panel buttons:**
| Button | Field | Description |
|--------|-------|-------------|
| Next / Draw Path | `Btn Next Or Path` | While < 3 placed: advance to next enclosure ghost. After all 3: start path phase |
| Cancel | `Btn Cancel Enclosure` | Cancel current ghost placement |
| Delete Enclosure | `Btn Delete Enclosure` | Delete the selected enclosure (move/rotate/scale via gesture) |

**Other buttons:** Begin/Stop/Finalize Path, Open/Close Object Menu, Fence/Gate/Bin/Animal selectors, Delete Object, Undo, Redo, Check Requirements, Save

**Debug / Testing toggles** (Inspector):
- `Debug Skip Detection` — bypass plane detection, start in PlacingEnclosures phase
- `Debug Enclosure Only` — suppress path/object panels for enclosure-only testing

**System refs:** `EnclosurePlacer`, `PathCreator`, `ZooObjectPlacer`, `ZooSaveManager`, `ZooRequirementsChecker`, `ZooTransformer`

Panels switch automatically as `ZooManager.CurrentPhase` changes.

---

### ZooInputHandler.cs
**Attach to:** `ZooManager` GameObject.

Routes all touch input. Single-finger and two-finger gestures are handled here; `ZooTransformer` no longer has its own Update.

**Tap routing (PlacementMode):**

| Mode | Tap action |
|------|-----------|
| EnclosureFloor | Place enclosure → auto-select it for gesture editing |
| Fence/Gate/Bin/Animal | `ZooObjectPlacer.TryPlaceObject()` |
| Path | `PathCreator.PlacePathSegment()` |
| None | Select/deselect object or enclosure |

**Gesture routing (based on where fingers touch down):**

| Gesture | Fingers on enclosure | Fingers on empty space |
|---------|---------------------|----------------------|
| 1-finger drag | Move selected enclosure | (no action) |
| 2-finger pinch | Scale enclosure | Scale whole zoo |
| 2-finger twist | Rotate enclosure | Rotate whole zoo |

> Two-finger target is decided on the **first frame** of the gesture — moving fingers off the enclosure won't switch targets mid-gesture.

| Inspector Field | Description |
|----------------|-------------|
| `Drag Threshold` | Pixels moved before tap becomes drag (default 10) |
| `Enclosure Move/Rotate/Scale Sensitivity` | Gesture sensitivity for enclosure editing |
| `AR Camera` | Used to convert screen delta to world-space movement |
| `Zoo Transformer` | Reference for whole-zoo gestures |

> **Note:** Uses `touch.fingerId` for UI overlap check — panels don't block AR plane taps.

---

### ZooARPlaneWatcher.cs
**Attach to:** the same GameObject as `ARPlaneManager`.

Detects the first horizontal AR plane and:
1. Calls `ZooManager.OnPlaneDetected()` → transitions to PlacingEnclosures phase
2. Calls `ZooTransformer.SetPlaneY(y)` → keeps floors clamped to plane height during scaling

| Inspector Field | Description |
|----------------|-------------|
| `Plane Manager` | Scene's ARPlaneManager |
| `Zoo Transformer` | Scene's ZooTransformer |
