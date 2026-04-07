using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ZooBuilder
{
    /// <summary>
    /// Allows the builder to draw a freehand path on the AR plane using a LineRenderer.
    /// The path must connect all three enclosures before it can be finalized.
    ///
    /// Usage:
    ///   1. Call BeginPath() to start drawing.
    ///   2. Call AddPathPoint(screenPos) repeatedly as the user drags a finger.
    ///   3. Call FinalizePath() once all enclosures are connected.
    ///   4. The resulting path is stored as a series of world-space points for
    ///      serialization/export to the VR app.
    ///
    /// The path data is later re-constructed in VR using Unity Splines.
    /// </summary>
    public class PathCreator : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] ARRaycastManager m_RaycastManager;

        [Header("Visual")]
        [SerializeField] LineRenderer m_PathLine;
        [SerializeField] float m_PathYOffset = 0.005f;  // Slightly above plane
        [SerializeField] float m_MinPointSpacing = 0.05f; // Meters between points

        [Header("Path Segments (alternative)")]
        [Tooltip("If set, discrete path segment prefabs are used instead of a free-draw line.")]
        [SerializeField] GameObject m_PathSegmentPrefab;

        // All path points (world space)
        readonly List<Vector3> m_PathPoints = new List<Vector3>();
        readonly List<GameObject> m_SpawnedSegments = new List<GameObject>();

        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        bool m_IsDrawing = false;
        bool m_PathFinalized = false;

        // ── Path events ──────────────────────────────────────────────────────

        public System.Action OnPathConnectedAllEnclosures;
        public System.Action OnPathFinalized;

        // ── Drawing ──────────────────────────────────────────────────────────

        public void BeginPath()
        {
            if (m_PathFinalized) return;
            m_PathPoints.Clear();
            if (m_PathLine != null) m_PathLine.positionCount = 0;
            foreach (var s in m_SpawnedSegments) if (s != null) Destroy(s);
            m_SpawnedSegments.Clear();
            m_IsDrawing = true;
        }

        public void StopDrawing()
        {
            m_IsDrawing = false;
        }

        /// <summary>
        /// Called while the user drags to draw the path. Pass screen-space finger position.
        /// Returns true if a point was added.
        /// </summary>
        public bool AddPathPoint(Vector2 screenPos)
        {
            if (!m_IsDrawing || m_PathFinalized) return false;

            if (!m_RaycastManager.Raycast(screenPos, s_Hits, TrackableType.PlaneWithinPolygon))
                return false;

            var hit = s_Hits[0];
            var plane = hit.trackable as ARPlane;
            if (plane != null &&
                plane.alignment != PlaneAlignment.HorizontalUp &&
                plane.alignment != PlaneAlignment.HorizontalDown)
                return false;

            Vector3 worldPoint = hit.pose.position + Vector3.up * m_PathYOffset;

            // Enforce minimum spacing between points
            if (m_PathPoints.Count > 0)
            {
                float dist = Vector3.Distance(m_PathPoints[m_PathPoints.Count - 1], worldPoint);
                if (dist < m_MinPointSpacing) return false;
            }

            m_PathPoints.Add(worldPoint);
            RefreshLine();

            if (AllEnclosuresConnected())
                OnPathConnectedAllEnclosures?.Invoke();

            return true;
        }

        /// <summary>
        /// Adds a discrete path segment prefab at the specified screen position.
        /// Used when m_PathSegmentPrefab is set.
        /// </summary>
        public bool PlacePathSegment(Vector2 screenPos)
        {
            if (m_PathSegmentPrefab == null || m_PathFinalized) return false;

            if (!m_RaycastManager.Raycast(screenPos, s_Hits, TrackableType.PlaneWithinPolygon))
                return false;

            var hit = s_Hits[0];
            Vector3 worldPoint = hit.pose.position;

            var segment = Instantiate(m_PathSegmentPrefab, worldPoint, hit.pose.rotation);
            if (ZooManager.Instance?.zooRoot != null)
                segment.transform.SetParent(ZooManager.Instance.zooRoot, true);

            m_SpawnedSegments.Add(segment);
            m_PathPoints.Add(worldPoint);
            RefreshLine();

            if (AllEnclosuresConnected())
                OnPathConnectedAllEnclosures?.Invoke();

            return true;
        }

        // ── Validation ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the path passes near all three enclosures.
        /// "Near" is defined as any path point within ConnectionRadius of an enclosure centroid
        /// or the path line crossing the enclosure boundary.
        /// </summary>
        [SerializeField] float m_ConnectionRadius = 0.5f;

        public bool AllEnclosuresConnected()
        {
            if (ZooManager.Instance == null) return false;
            var enclosures = ZooManager.Instance.Enclosures;
            if (enclosures.Count < 3) return false;

            bool[] connected = new bool[enclosures.Count];
            foreach (var pt in m_PathPoints)
            {
                for (int i = 0; i < enclosures.Count; i++)
                {
                    if (!connected[i])
                    {
                        var bounds = enclosures[i].GetWorldBounds();
                        // Expand bounds by connection radius
                        bounds.Expand(m_ConnectionRadius * 2f);
                        if (bounds.Contains(new Vector3(pt.x, bounds.center.y, pt.z)))
                            connected[i] = true;
                    }
                }
            }

            foreach (bool c in connected)
                if (!c) return false;

            return true;
        }

        // ── Finalize ─────────────────────────────────────────────────────────

        /// <summary>
        /// Finalizes the path. Requires all enclosures to be connected.
        /// Returns false if validation fails.
        /// </summary>
        public bool TryFinalizePath()
        {
            if (!AllEnclosuresConnected())
            {
                Debug.LogWarning("[PathCreator] Path does not connect all enclosures.");
                return false;
            }

            m_PathFinalized = true;
            m_IsDrawing = false;

            // Parent the line renderer to the zoo root
            if (ZooManager.Instance?.zooRoot != null && m_PathLine != null)
                m_PathLine.transform.SetParent(ZooManager.Instance.zooRoot, true);

            ZooManager.Instance?.FinishPathCreation();
            OnPathFinalized?.Invoke();
            return true;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        void RefreshLine()
        {
            if (m_PathLine == null) return;
            m_PathLine.positionCount = m_PathPoints.Count;
            for (int i = 0; i < m_PathPoints.Count; i++)
                m_PathLine.SetPosition(i, m_PathPoints[i]);
        }

        // ── Public data ──────────────────────────────────────────────────────

        public List<Vector3> PathPoints => m_PathPoints;
        public bool IsDrawing => m_IsDrawing;
        public bool PathFinalized => m_PathFinalized;
    }
}
