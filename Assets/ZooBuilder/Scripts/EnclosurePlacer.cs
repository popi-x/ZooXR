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

        [Header("Ghost Preview")]
        [Tooltip("Transparent preview shown at the tap position before confirming placement.")]
        [SerializeField] GameObject m_GhostPrefab1;
        [SerializeField] GameObject m_GhostPrefab2;
        [SerializeField] GameObject m_GhostPrefab3;

        [Header("Placement Settings")]
        [Tooltip("If true the enclosure faces the AR camera when placed.")]
        [SerializeField] bool m_FaceCamera = true;

        [Header("Debug / Testing")]
        [Tooltip("If AR raycast fails, place enclosure at a fixed distance in front of the camera. Useful for editor/simulator testing.")]
        [SerializeField] bool m_DebugFallbackPlacement = false;
        [SerializeField] float m_DebugPlaceDistance = 1.5f;

        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        bool m_IsActive = false;
        GameObject m_ActiveGhost = null;

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
            ShowGhost(next);
        }

        /// <summary>Cancels placement mode and hides the ghost.</summary>
        public void CancelPlacement()
        {
            m_IsActive = false;
            HideGhost();
        }

        // ── Per-frame ghost tracking ──────────────────────────────────────────

        void Update()
        {
            if (!m_IsActive || m_ActiveGhost == null) return;

            // Move ghost to wherever the centre of the screen (or last touch) hits the AR plane
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (m_RaycastManager.Raycast(screenCenter, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                var hit = s_Hits[0];
                if (IsHorizontal(hit.trackable as ARPlane))
                {
                    m_ActiveGhost.SetActive(true);
                    m_ActiveGhost.transform.position = hit.pose.position;
                    if (m_FaceCamera && m_ARCamera != null)
                        m_ActiveGhost.transform.rotation = FacingCamera(hit.pose.position);
                }
            }
            else
            {
                m_ActiveGhost.SetActive(false);
            }
        }

        // ── Tap to place ──────────────────────────────────────────────────────

        /// <summary>
        /// Called by ZooInputHandler when the user taps while in EnclosureFloor placement mode.
        /// Instantiates the correct prefab at the hit position.
        /// Returns the created EnclosureFloor, or null on failure.
        /// </summary>
        public EnclosureFloor TryPlace(Vector2 screenPos)
        {
            if (!m_IsActive) return null;

            Vector3 spawnPos;
            Quaternion spawnRot;

            bool arHit = m_RaycastManager != null &&
                         m_RaycastManager.Raycast(screenPos, s_Hits, TrackableType.PlaneWithinPolygon) &&
                         IsHorizontal(s_Hits[0].trackable as ARPlane);

            if (arHit)
            {
                spawnPos = s_Hits[0].pose.position;
                spawnRot = m_FaceCamera ? FacingCamera(spawnPos) : s_Hits[0].pose.rotation;
            }
            else if (m_DebugFallbackPlacement && m_ARCamera != null)
            {
                // Place at a fixed distance in front of the camera on Y=0
                spawnPos = m_ARCamera.transform.position +
                           m_ARCamera.transform.forward * m_DebugPlaceDistance;
                spawnPos.y = 0f;
                spawnRot = m_FaceCamera ? FacingCamera(spawnPos) : Quaternion.identity;
            }
            else
            {
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

            // Hide ghost and switch to None mode so user can gesture-edit the placed enclosure.
            // The UI "Next" button will call BeginPlacement() for the next type.
            HideGhost();
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

        GameObject GetGhostForType(EnclosureType type) => type switch
        {
            EnclosureType.Enclosure1 => m_GhostPrefab1,
            EnclosureType.Enclosure2 => m_GhostPrefab2,
            EnclosureType.Enclosure3 => m_GhostPrefab3,
            _ => null
        };

        void ShowGhost(EnclosureType type)
        {
            HideGhost();

            // Use dedicated ghost prefab if assigned, otherwise fall back to the real prefab
            var ghostPrefab = GetGhostForType(type) ?? GetPrefabForType(type);
            if (ghostPrefab == null) return;

            m_ActiveGhost = Object.Instantiate(ghostPrefab);
            m_ActiveGhost.SetActive(false);

            // Make all renderers semi-transparent so it looks like a ghost preview
            foreach (var r in m_ActiveGhost.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in r.materials)
                {
                    // Works for URP Lit and Standard shaders
                    mat.SetFloat("_Surface", 1);          // 1 = Transparent
                    mat.SetFloat("_Blend", 0);            // Alpha blend
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = 3000;

                    Color c = mat.color;
                    c.a = 0.4f;
                    mat.color = c;
                }
            }

            // Disable all colliders so the ghost doesn't interfere with raycasts
            foreach (var col in m_ActiveGhost.GetComponentsInChildren<Collider>())
                col.enabled = false;
        }

        void HideGhost()
        {
            if (m_ActiveGhost != null)
            {
                Object.Destroy(m_ActiveGhost);
                m_ActiveGhost = null;
            }
        }

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
        /// Returns the world-space vertices of the first MeshFilter found on the GameObject.
        /// Falls back to a default quad if no mesh is found.
        /// </summary>
        Vector3[] GetMeshWorldVertices(GameObject go)
        {
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var localVerts = mf.sharedMesh.vertices;
                var world = new Vector3[localVerts.Length];
                for (int i = 0; i < localVerts.Length; i++)
                    world[i] = mf.transform.TransformPoint(localVerts[i]);
                return world;
            }

            // Fallback: 1x1 metre quad centred on the object
            float h = 0.5f;
            return new[]
            {
                go.transform.position + go.transform.TransformDirection(new Vector3(-h, 0,  h)),
                go.transform.position + go.transform.TransformDirection(new Vector3( h, 0,  h)),
                go.transform.position + go.transform.TransformDirection(new Vector3( h, 0, -h)),
                go.transform.position + go.transform.TransformDirection(new Vector3(-h, 0, -h)),
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
