using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ZooBuilder
{
    /// <summary>
    /// Handles the placement of enclosure floors on a detected AR horizontal plane.
    ///
    /// Workflow:
    ///   1. User taps to add corner points of the enclosure polygon.
    ///   2. After placing at least 3 points the "Confirm" button becomes available.
    ///   3. On confirm, the polygon is validated (no overlap, minimum size) and
    ///      an EnclosureFloor is instantiated and registered with ZooManager.
    ///
    /// To make an enclosure non-rectangular, the user can add 5+ corner points.
    /// </summary>
    public class EnclosurePlacer : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] ARRaycastManager m_RaycastManager;
        [SerializeField] Camera m_ARCamera;

        [Header("Prefabs")]
        [Tooltip("Prefab for a placed enclosure floor (needs EnclosureFloor component, MeshFilter, MeshRenderer, MeshCollider).")]
        [SerializeField] GameObject m_EnclosureFloorPrefab;

        [Tooltip("Small sphere shown at each corner tap point.")]
        [SerializeField] GameObject m_CornerMarkerPrefab;

        [Tooltip("Preview line showing the polygon being drawn.")]
        [SerializeField] LineRenderer m_PreviewLine;

        [Header("Validation")]
        [SerializeField] float m_MinPolygonArea = 0.5f;  // square meters

        // Corner points gathered so far (world space, on AR plane)
        readonly List<Vector3> m_CornerPoints = new List<Vector3>();
        readonly List<GameObject> m_CornerMarkers = new List<GameObject>();

        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        bool m_IsActive = false;
        int m_MinCorners = 3;

        // ── Activation ───────────────────────────────────────────────────────

        public void BeginPlacement()
        {
            if (ZooManager.Instance == null) return;
            if (ZooManager.Instance.GetNextEnclosureType() == EnclosureType.None)
            {
                Debug.LogWarning("[EnclosurePlacer] All 3 enclosure types are already placed.");
                return;
            }
            ClearCorners();
            m_IsActive = true;
            if (m_PreviewLine != null) m_PreviewLine.positionCount = 0;
        }

        public void CancelPlacement()
        {
            ClearCorners();
            m_IsActive = false;
            if (m_PreviewLine != null) m_PreviewLine.positionCount = 0;
        }

        // ── Per-frame ────────────────────────────────────────────────────────

        void Update()
        {
            if (!m_IsActive) return;
            UpdatePreviewLine();
        }

        // ── Tap handling (called by ZooUIManager or input handler) ───────────

        /// <summary>
        /// Called when the user taps the screen to add a corner point.
        /// Returns true if a point was successfully added.
        /// </summary>
        public bool TryAddCorner(Vector2 screenPos)
        {
            if (!m_IsActive) return false;

            if (m_RaycastManager.Raycast(screenPos, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                var hit = s_Hits[0];
                // Only accept horizontal planes
                var plane = hit.trackable as ARPlane;
                if (plane != null && plane.alignment != PlaneAlignment.HorizontalUp &&
                    plane.alignment != PlaneAlignment.HorizontalDown)
                {
                    Debug.Log("[EnclosurePlacer] Ignoring non-horizontal plane tap.");
                    return false;
                }

                Vector3 worldPoint = hit.pose.position;
                m_CornerPoints.Add(worldPoint);
                SpawnCornerMarker(worldPoint);
                UpdatePreviewLine();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the last added corner point (undo-style while drawing).
        /// </summary>
        public void RemoveLastCorner()
        {
            if (m_CornerPoints.Count == 0) return;
            m_CornerPoints.RemoveAt(m_CornerPoints.Count - 1);

            var last = m_CornerMarkers[m_CornerMarkers.Count - 1];
            m_CornerMarkers.RemoveAt(m_CornerMarkers.Count - 1);
            Destroy(last);

            UpdatePreviewLine();
        }

        /// <summary>
        /// Returns true if we have enough corners to confirm placement.
        /// </summary>
        public bool CanConfirm() => m_CornerPoints.Count >= m_MinCorners && m_IsActive;

        /// <summary>
        /// Confirms the enclosure polygon and creates the floor.
        /// Returns the created EnclosureFloor, or null on failure.
        /// </summary>
        public EnclosureFloor ConfirmPlacement()
        {
            if (!CanConfirm()) return null;

            var points = m_CornerPoints.ToArray();

            // Validate polygon area
            float area = PolygonArea(points);
            if (area < m_MinPolygonArea)
            {
                Debug.LogWarning($"[EnclosurePlacer] Enclosure too small (area={area:F2}m²). Minimum is {m_MinPolygonArea}m².");
                return null;
            }

            // Check overlap with existing enclosures
            if (ZooManager.Instance.PolygonOverlapsAnyEnclosure(points))
            {
                Debug.LogWarning("[EnclosurePlacer] Enclosure overlaps an existing enclosure.");
                return null;
            }

            // Determine which enclosure type to assign
            EnclosureType type = ZooManager.Instance.GetNextEnclosureType();
            if (type == EnclosureType.None)
            {
                Debug.LogWarning("[EnclosurePlacer] All enclosure types are already placed.");
                return null;
            }

            // Spawn the floor
            Vector3 center = PolygonCentroid(points);
            var floorGO = Instantiate(m_EnclosureFloorPrefab, center, Quaternion.identity);
            floorGO.name = $"Enclosure_{(int)type}";

            // Parent to zoo root so zoo transformation works
            if (ZooManager.Instance.zooRoot != null)
                floorGO.transform.SetParent(ZooManager.Instance.zooRoot, true);

            var floor = floorGO.GetComponent<EnclosureFloor>();
            if (floor == null) floor = floorGO.AddComponent<EnclosureFloor>();
            floor.Initialize(points, type);

            ZooManager.Instance.RegisterEnclosure(floor);

            // Clean up corner markers
            CancelPlacement();

            return floor;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        void SpawnCornerMarker(Vector3 pos)
        {
            if (m_CornerMarkerPrefab == null) return;
            var marker = Instantiate(m_CornerMarkerPrefab, pos + Vector3.up * 0.02f, Quaternion.identity);
            m_CornerMarkers.Add(marker);
        }

        void UpdatePreviewLine()
        {
            if (m_PreviewLine == null) return;
            int n = m_CornerPoints.Count;
            if (n < 2)
            {
                m_PreviewLine.positionCount = 0;
                return;
            }

            // Close the polygon preview
            m_PreviewLine.positionCount = n + 1;
            for (int i = 0; i < n; i++)
                m_PreviewLine.SetPosition(i, m_CornerPoints[i] + Vector3.up * 0.02f);
            m_PreviewLine.SetPosition(n, m_CornerPoints[0] + Vector3.up * 0.02f);
        }

        void ClearCorners()
        {
            m_CornerPoints.Clear();
            foreach (var m in m_CornerMarkers)
                if (m != null) Destroy(m);
            m_CornerMarkers.Clear();
        }

        // Shoelace formula for polygon area
        float PolygonArea(Vector3[] pts)
        {
            float area = 0f;
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += pts[i].x * pts[j].z;
                area -= pts[j].x * pts[i].z;
            }
            return Mathf.Abs(area) * 0.5f;
        }

        Vector3 PolygonCentroid(Vector3[] pts)
        {
            Vector3 c = Vector3.zero;
            foreach (var p in pts) c += p;
            return c / pts.Length;
        }

        // ── Public state ────────────────────────────────────────────────────

        public bool IsActive => m_IsActive;
        public int CornerCount => m_CornerPoints.Count;
    }
}
