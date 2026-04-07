using UnityEngine;

namespace ZooBuilder
{
    /// <summary>
    /// Base component for all placeable zoo objects: fences, gates, bins, and animals.
    /// Tracks which enclosure floor the object belongs to and its type.
    /// </summary>
    public class ZooObject : MonoBehaviour
    {
        [SerializeField] ZooObjectType m_ObjectType;
        [SerializeField] string m_PrefabId; // Identifier for save/load

        public ZooObjectType ObjectType => m_ObjectType;
        public string PrefabId
        {
            get => m_PrefabId;
            set => m_PrefabId = value;
        }

        public EnclosureFloor ParentEnclosure { get; private set; }

        // Saved transform state (in ZooRoot local space, 1 unit = 1 meter)
        public Vector3 SavedPosition => transform.localPosition;
        public Quaternion SavedRotation => transform.localRotation;

        // ── Initialization ───────────────────────────────────────────────────

        public void Initialize(ZooObjectType type, string prefabId, EnclosureFloor enclosure)
        {
            m_ObjectType = type;
            m_PrefabId = prefabId;
            ParentEnclosure = enclosure;
            enclosure?.AddObject(this);
        }

        // ── Placement validation ─────────────────────────────────────────────

        /// <summary>
        /// Returns true if this object overlaps another ZooObject using a simple bounds check.
        /// </summary>
        public bool OverlapsAny()
        {
            if (ParentEnclosure == null) return false;

            var myBounds = GetBounds();
            foreach (var other in ParentEnclosure.PlacedObjects)
            {
                if (other == this) continue;
                var otherBounds = other.GetBounds();
                if (myBounds.Intersects(otherBounds))
                    return true;
            }
            return false;
        }

        Bounds GetBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(transform.position, Vector3.one * 0.1f);

            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }

        // ── Manipulation ──────────────────────────────────────────────────────

        /// <summary>
        /// Moves the object by delta, staying on the enclosure floor (Y is fixed).
        /// </summary>
        public void Translate(Vector3 worldDelta)
        {
            Vector3 newPos = transform.position + new Vector3(worldDelta.x, 0f, worldDelta.z);
            // Clamp to enclosure floor
            if (ParentEnclosure != null && !ParentEnclosure.ContainsPoint(newPos))
                return;
            transform.position = newPos;
        }

        /// <summary>
        /// Rotates the object about the vertical (Y) axis.
        /// </summary>
        public void RotateY(float degrees)
        {
            transform.Rotate(Vector3.up, degrees, Space.World);
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        void OnDestroy()
        {
            ParentEnclosure?.RemoveObject(this);
        }
    }
}
