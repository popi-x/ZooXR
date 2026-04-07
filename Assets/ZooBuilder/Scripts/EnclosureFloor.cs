using System.Collections.Generic;
using UnityEngine;

namespace ZooBuilder
{
    /// <summary>
    /// Represents a single enclosure floor in the zoo.
    /// Stores the polygon shape, tracks placed objects, and enforces modification rules.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class EnclosureFloor : MonoBehaviour
    {
        [SerializeField] EnclosureType m_EnclosureType = EnclosureType.None;

        public EnclosureType EnclosureType
        {
            get => m_EnclosureType;
            set => m_EnclosureType = value;
        }

        // World-space polygon vertices (XZ plane, Y matches floor height)
        public Vector3[] WorldVertices { get; private set; }

        // Local-space polygon vertices (relative to this transform)
        Vector2[] m_LocalVerts2D;

        // All zoo objects placed on this floor
        readonly List<ZooObject> m_PlacedObjects = new List<ZooObject>();
        public IReadOnlyList<ZooObject> PlacedObjects => m_PlacedObjects;

        // Label shown in AR (optional TextMesh)
        [SerializeField] TextMesh m_Label;

        // ── Initialization ──────────────────────────────────────────────────

        /// <summary>
        /// Build the floor mesh from world-space polygon points (on the AR plane).
        /// </summary>
        public void Initialize(Vector3[] worldPoints, EnclosureType type)
        {
            m_EnclosureType = type;
            WorldVertices = worldPoints;

            // Convert to local space for 2D containment tests
            m_LocalVerts2D = new Vector2[worldPoints.Length];
            for (int i = 0; i < worldPoints.Length; i++)
            {
                var local = transform.InverseTransformPoint(worldPoints[i]);
                m_LocalVerts2D[i] = new Vector2(local.x, local.z);
            }

            BuildMesh(worldPoints);

            if (m_Label != null)
                m_Label.text = $"Enclosure {(int)type}";
        }

        void BuildMesh(Vector3[] worldPoints)
        {
            int n = worldPoints.Length;
            Vector3[] localVerts = new Vector3[n];
            for (int i = 0; i < n; i++)
                localVerts[i] = transform.InverseTransformPoint(worldPoints[i]);

            // Triangulate polygon (ear clipping for simple convex/concave)
            int[] tris = TriangulatePolygon(localVerts);

            var mesh = new Mesh();
            mesh.vertices = localVerts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;
            var col = GetComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.convex = false;
        }

        // Simple fan triangulation (works for convex polygons; for concave use ear-clipping)
        int[] TriangulatePolygon(Vector3[] verts)
        {
            int n = verts.Length;
            if (n < 3) return new int[0];

            // Fan triangulation from vertex 0 (works for convex polygons)
            // For a proper solution, ear-clipping is used below
            return EarClip(verts);
        }

        int[] EarClip(Vector3[] verts)
        {
            int n = verts.Length;
            var indices = new List<int>();
            for (int i = 0; i < n; i++) indices.Add(i);

            var tris = new List<int>();

            int attempts = 0;
            int i0 = 0;
            while (indices.Count > 3 && attempts < indices.Count * indices.Count)
            {
                int count = indices.Count;
                int prev = indices[(i0 - 1 + count) % count];
                int curr = indices[i0];
                int next = indices[(i0 + 1) % count];

                if (IsEar(verts, prev, curr, next, indices))
                {
                    tris.Add(prev);
                    tris.Add(curr);
                    tris.Add(next);
                    indices.RemoveAt(i0);
                    if (i0 >= indices.Count) i0 = 0;
                    attempts = 0;
                }
                else
                {
                    i0 = (i0 + 1) % indices.Count;
                    attempts++;
                }
            }

            if (indices.Count == 3)
            {
                tris.Add(indices[0]);
                tris.Add(indices[1]);
                tris.Add(indices[2]);
            }

            return tris.ToArray();
        }

        bool IsEar(Vector3[] verts, int prev, int curr, int next, List<int> remaining)
        {
            Vector2 a = new Vector2(verts[prev].x, verts[prev].z);
            Vector2 b = new Vector2(verts[curr].x, verts[curr].z);
            Vector2 c = new Vector2(verts[next].x, verts[next].z);

            // Must be counter-clockwise (left turn)
            if (Cross2D(a, b, c) <= 0) return false;

            // No other vertex inside triangle
            foreach (int idx in remaining)
            {
                if (idx == prev || idx == curr || idx == next) continue;
                Vector2 p = new Vector2(verts[idx].x, verts[idx].z);
                if (PointInTriangle(p, a, b, c)) return false;
            }
            return true;
        }

        float Cross2D(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross2D(p, a, b);
            float d2 = Cross2D(p, b, c);
            float d3 = Cross2D(p, c, a);
            bool has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(has_neg && has_pos);
        }

        // ── Containment / overlap ────────────────────────────────────────────

        /// <summary>
        /// Returns true if the given world-space point (XZ) is inside this enclosure.
        /// </summary>
        public bool ContainsPoint(Vector3 worldPos)
        {
            if (m_LocalVerts2D == null) return false;
            var local = transform.InverseTransformPoint(worldPos);
            return PointInPolygon(new Vector2(local.x, local.z), m_LocalVerts2D);
        }

        /// <summary>
        /// Returns true if ANY of the given world-space polygon vertices fall inside this enclosure,
        /// or any of this enclosure's vertices fall inside the given polygon.
        /// (Simple conservative overlap check.)
        /// </summary>
        public bool OverlapsPolygon(Vector3[] otherWorldPoints)
        {
            if (WorldVertices == null) return false;

            // Check if any of the other polygon's points are inside this enclosure
            foreach (var pt in otherWorldPoints)
            {
                if (ContainsPoint(pt)) return true;
            }

            // Check if any of this enclosure's points are inside the other polygon
            var other2D = WorldPointsTo2D(otherWorldPoints);
            foreach (var v in WorldVertices)
            {
                var local = new Vector2(v.x, v.z);
                if (PointInPolygon(local, other2D)) return true;
            }

            return false;
        }

        Vector2[] WorldPointsTo2D(Vector3[] pts)
        {
            var arr = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                arr[i] = new Vector2(pts[i].x, pts[i].z);
            return arr;
        }

        // Ray casting point-in-polygon test
        bool PointInPolygon(Vector2 point, Vector2[] polygon)
        {
            int n = polygon.Length;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                    (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                    (polygon[j].y - polygon[i].y) + polygon[i].x))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        // ── Object tracking ──────────────────────────────────────────────────

        public void AddObject(ZooObject obj)
        {
            if (!m_PlacedObjects.Contains(obj))
                m_PlacedObjects.Add(obj);
        }

        public void RemoveObject(ZooObject obj)
        {
            m_PlacedObjects.Remove(obj);
        }

        public bool HasObjects() => m_PlacedObjects.Count > 0;

        // ── Modification rules ───────────────────────────────────────────────

        /// <summary>
        /// Enclosure can be moved/resized only when it has no objects and path creation has not started.
        /// </summary>
        public bool CanBeModified()
        {
            if (ZooManager.Instance == null) return true;
            return !HasObjects() && !ZooManager.Instance.PathCreationStarted;
        }

        /// <summary>
        /// Enclosure can be deleted only when it has no objects and path creation has not started.
        /// </summary>
        public bool CanBeDeleted()
        {
            return CanBeModified();
        }

        // ── Bounds helper ────────────────────────────────────────────────────

        public Bounds GetWorldBounds()
        {
            if (WorldVertices == null || WorldVertices.Length == 0)
                return new Bounds(transform.position, Vector3.zero);

            var b = new Bounds(WorldVertices[0], Vector3.zero);
            foreach (var v in WorldVertices)
                b.Encapsulate(v);
            return b;
        }

        // ── Translate / Rotate helpers (for ZooTransformer and direct editing) ──

        public void MoveFloor(Vector3 delta)
        {
            if (!CanBeModified()) return;
            transform.position += delta;
            // Update world vertices
            for (int i = 0; i < WorldVertices.Length; i++)
                WorldVertices[i] += delta;
        }

        public void RotateFloor(float degrees)
        {
            if (!CanBeModified()) return;
            transform.RotateAround(transform.position, Vector3.up, degrees);
            // Update world vertices
            Quaternion rot = Quaternion.AngleAxis(degrees, Vector3.up);
            Vector3 center = GetWorldBounds().center;
            for (int i = 0; i < WorldVertices.Length; i++)
                WorldVertices[i] = rot * (WorldVertices[i] - center) + center;
            // Refresh 2D verts
            RefreshLocalVerts();
        }

        public void ScaleFloor(float scaleFactor)
        {
            if (!CanBeModified()) return;
            Vector3 center = GetWorldBounds().center;
            for (int i = 0; i < WorldVertices.Length; i++)
                WorldVertices[i] = center + (WorldVertices[i] - center) * scaleFactor;
            transform.localScale *= scaleFactor;
            RefreshLocalVerts();
            BuildMesh(WorldVertices);
        }

        void RefreshLocalVerts()
        {
            if (WorldVertices == null) return;
            m_LocalVerts2D = new Vector2[WorldVertices.Length];
            for (int i = 0; i < WorldVertices.Length; i++)
            {
                var local = transform.InverseTransformPoint(WorldVertices[i]);
                m_LocalVerts2D[i] = new Vector2(local.x, local.z);
            }
        }

        void OnDestroy()
        {
            ZooManager.Instance?.UnregisterEnclosure(this);
        }
    }
}
