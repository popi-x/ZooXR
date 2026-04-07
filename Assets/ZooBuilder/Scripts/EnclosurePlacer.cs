using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ZooBuilder
{
    /// <summary>
    /// Places enclosure floor prefabs directly on the AR horizontal plane with a single tap.
    /// Each enclosure type (1/2/3) uses its own prefab and is placed in order.
    ///
    /// After placement the enclosure can be moved, rotated, and scaled
    /// (as long as no objects have been placed on it and path creation hasn't started).
    /// </summary>
    public class EnclosurePlacer : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] ARRaycastManager m_RaycastManager;
        [SerializeField] Camera m_ARCamera;

        [Header("Enclosure Prefabs")]
        [Tooltip("Prefab for Enclosure 1 (static animals). Must have EnclosureFloor, MeshFilter, MeshRenderer, MeshCollider.")]
        [SerializeField] GameObject m_EnclosureFloorPrefab1;

        [Tooltip("Prefab for Enclosure 2 (roaming animals).")]
        [SerializeField] GameObject m_EnclosureFloorPrefab2;

        [Tooltip("Prefab for Enclosure 3 (hungry animal).")]
        [SerializeField] GameObject m_EnclosureFloorPrefab3;

        [Header("Placement Settings")]
        [Tooltip("If true the enclosure faces the AR camera when placed.")]
        [SerializeField] bool m_FaceCamera = true;

        [Header("Debug / Testing")]
        [Tooltip("If AR raycast fails, place enclosure at a fixed distance in front of the camera. Useful for editor/simulator testing.")]
        [SerializeField] bool m_DebugFallbackPlacement = false;
        [SerializeField] float m_DebugPlaceDistance = 1.5f;

        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        bool m_IsActive = false;

        // ── Activation ────────────────────────────────────────────────────────

        /// <summary>Starts placement mode for the next enclosure type.</summary>
        public void BeginPlacement()
        {
            if (ZooManager.Instance == null) return;
            EnclosureType next = ZooManager.Instance.GetNextEnclosureType();
            if (next == EnclosureType.None)
            {
                Debug.LogWarning("[EnclosurePlacer] All 3 enclosures already placed.");
                return;
            }
            m_IsActive = true;
            ZooManager.Instance.SetPlacementModeExplicit(PlacementMode.EnclosureFloor);
        }

        /// <summary>Cancels placement mode and hides the ghost.</summary>
        public void CancelPlacement()
        {
            m_IsActive = false;
        }

        // ── Tap to place ──────────────────────────────────────────────────────

        /// <summary>
        /// Called by ZooInputHandler when the user taps while in EnclosureFloor placement mode.
        /// Instantiates the correct prefab at the hit position.
        /// Returns the created EnclosureFloor, or null on failure.
        /// </summary>
        public EnclosureFloor TryPlace(Vector2 screenPos)
        {
            if (!m_IsActive)
            {
                Debug.LogWarning("[EnclosurePlacer] TryPlace called but m_IsActive is false. Was BeginPlacement() called?");
                return null;
            }

            Vector3 spawnPos;
            Quaternion spawnRot;

            bool arHit = m_RaycastManager != null &&
                         m_RaycastManager.Raycast(screenPos, s_Hits, TrackableType.PlaneWithinPolygon) &&
                         IsHorizontal(s_Hits[0].trackable as ARPlane);

            if (arHit)
            {
                spawnPos = s_Hits[0].pose.position;
                spawnRot = m_FaceCamera ? FacingCamera(spawnPos) : s_Hits[0].pose.rotation;
                Debug.Log($"[EnclosurePlacer] AR raycast hit at {spawnPos}");
            }
            else if (m_DebugFallbackPlacement && m_ARCamera != null)
            {
                spawnPos = m_ARCamera.transform.position +
                           m_ARCamera.transform.forward * m_DebugPlaceDistance;
                spawnPos.y = 0f;
                spawnRot = m_FaceCamera ? FacingCamera(spawnPos) : Quaternion.identity;
                Debug.Log($"[EnclosurePlacer] Using debug fallback at {spawnPos}");
            }
            else
            {
                Debug.LogWarning($"[EnclosurePlacer] No AR hit at {screenPos}. " +
                    $"RaycastManager={(m_RaycastManager != null ? "OK" : "NULL")}. " +
                    "Enable Debug Fallback Placement if testing without a real plane.");
                return null;
            }

            EnclosureType type = ZooManager.Instance.GetNextEnclosureType();
            if (type == EnclosureType.None) return null;

            GameObject prefab = GetPrefabForType(type);
            if (prefab == null)
            {
                Debug.LogError($"[EnclosurePlacer] Prefab for {type} is not assigned.");
                return null;
            }

            if (WouldOverlap(prefab, spawnPos, spawnRot))
            {
                Debug.LogWarning("[EnclosurePlacer] Placement would overlap an existing enclosure.");
                return null;
            }

            // Instantiate
            var floorGO = Object.Instantiate(prefab, spawnPos, spawnRot);
            floorGO.name = $"Enclosure_{(int)type}";

            if (ZooManager.Instance.zooRoot != null)
                floorGO.transform.SetParent(ZooManager.Instance.zooRoot, true);

            // Initialize EnclosureFloor with the prefab's mesh vertices as the polygon
            var floor = floorGO.GetComponent<EnclosureFloor>();
            if (floor == null) floor = floorGO.AddComponent<EnclosureFloor>();

            Vector3[] worldVerts = GetMeshWorldVertices(floorGO);
            floor.Initialize(worldVerts, type);

            ZooManager.Instance.RegisterEnclosure(floor);

            m_IsActive = false;
            ZooManager.Instance.SetPlacementModeExplicit(PlacementMode.None);

            return floor;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        GameObject GetPrefabForType(EnclosureType type) => type switch
        {
            EnclosureType.Enclosure1 => m_EnclosureFloorPrefab1,
            EnclosureType.Enclosure2 => m_EnclosureFloorPrefab2,
            EnclosureType.Enclosure3 => m_EnclosureFloorPrefab3,
            _ => null
        };

        bool IsHorizontal(ARPlane plane)
        {
            if (plane == null) return true; // allow if can't determine
            return plane.alignment == PlaneAlignment.HorizontalUp ||
                   plane.alignment == PlaneAlignment.HorizontalDown;
        }

        Quaternion FacingCamera(Vector3 pos)
        {
            Vector3 dir = m_ARCamera.transform.position - pos;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(dir.normalized)
                : Quaternion.identity;
        }

        /// <summary>
        /// Rough overlap check using the prefab's renderer bounds at the proposed position.
        /// </summary>
        bool WouldOverlap(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;

            // Estimate bounds from the prefab's renderers
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            // Offset bounds to proposed position
            b.center = pos + (b.center - prefab.transform.position);

            foreach (var enc in ZooManager.Instance.Enclosures)
            {
                if (enc.GetWorldBounds().Intersects(b))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns 4 world-space corners of the object's renderer bounds at floor Y.
        /// Using bounds corners avoids degenerate triangles from complex meshes.
        /// </summary>
        Vector3[] GetMeshWorldVertices(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            Bounds bounds;

            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            }
            else
            {
                bounds = new Bounds(go.transform.position, Vector3.one);
            }

            float y = go.transform.position.y;
            float minX = bounds.min.x, maxX = bounds.max.x;
            float minZ = bounds.min.z, maxZ = bounds.max.z;

            // CCW order when viewed from above
            return new Vector3[]
            {
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ),
            };
        }

        // ── Public state ──────────────────────────────────────────────────────

        public bool IsActive => m_IsActive;

        /// <summary>Legacy – no longer used but kept so ZooUIManager compiles.</summary>
        public int CornerCount => 0;

        /// <summary>Legacy – no longer used.</summary>
        public bool CanConfirm() => false;

        /// <summary>Legacy – no longer used.</summary>
        public EnclosureFloor ConfirmPlacement() => null;

        /// <summary>Legacy – no longer used.</summary>
        public void RemoveLastCorner() { }
    }
}
