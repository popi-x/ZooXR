using System.Collections.Generic;
using UnityEngine;

namespace ZooBuilder
{
    /// <summary>
    /// Handles the placement of zoo objects (fences, gates, bins, animals) on enclosure floors
    /// via screen-space raycasting.
    ///
    /// Objects can only be placed:
    ///   - On an enclosure floor
    ///   - Without overlapping any existing object
    ///
    /// All placements go through the UndoRedoManager so they can be undone.
    /// </summary>
    public class ZooObjectPlacer : MonoBehaviour
    {
        [Header("Object Prefab Library")]
        [Tooltip("All placeable prefabs. Each entry maps a string ID to a prefab.")]
        [SerializeField] List<PrefabEntry> m_PrefabLibrary = new List<PrefabEntry>();

        [Header("Prefab IDs (configure in Inspector)")]
        [SerializeField] string m_FencePrefabId   = "fence";
        [SerializeField] string m_GatePrefabId    = "gate";
        [SerializeField] string m_BinPrefabId     = "bin";
        [SerializeField] string m_AnimalEnc1Id    = "animal_enc1";
        [SerializeField] string m_AnimalEnc2Id    = "animal_enc2";
        [SerializeField] string m_AnimalEnc3Id    = "animal_enc3";

        [Header("Raycast")]
        [SerializeField] Camera m_ARCamera;
        [SerializeField] LayerMask m_EnclosureLayerMask;

        [Header("Ghost Preview")]
        [Tooltip("A ghost/preview object shown before the user confirms placement.")]
        [SerializeField] GameObject m_GhostObject;

        // Which enclosure type should receive animal placements (set by ZooUIManager)
        EnclosureType m_TargetAnimalEnclosureType = EnclosureType.Enclosure1;

        Dictionary<string, GameObject> m_PrefabDict;

        // ── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            if (m_ARCamera == null) m_ARCamera = Camera.main;
            BuildPrefabDict();
        }

        void BuildPrefabDict()
        {
            m_PrefabDict = new Dictionary<string, GameObject>();
            foreach (var entry in m_PrefabLibrary)
            {
                if (!string.IsNullOrEmpty(entry.id) && entry.prefab != null)
                    m_PrefabDict[entry.id] = entry.prefab;
            }
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void SetTargetEnclosureType(EnclosureType t) => m_TargetAnimalEnclosureType = t;

        /// <summary>
        /// Attempts to place the currently selected object at the given screen position.
        /// Uses EnclosureFloor mesh collider raycasting.
        /// Returns true on success.
        /// </summary>
        public bool TryPlaceObject(Vector2 screenPos)
        {
            if (ZooManager.Instance == null) return false;

            PlacementMode mode = ZooManager.Instance.CurrentPlacementMode;
            if (mode == PlacementMode.None || mode == PlacementMode.EnclosureFloor
                || mode == PlacementMode.Path) return false;

            // Raycast against enclosure floor colliders
            Ray ray = m_ARCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, m_EnclosureLayerMask))
            {
                Debug.Log("[ZooObjectPlacer] No enclosure floor hit.");
                return false;
            }

            EnclosureFloor floor = hit.collider.GetComponentInParent<EnclosureFloor>();
            if (floor == null)
            {
                Debug.Log("[ZooObjectPlacer] Hit collider has no EnclosureFloor parent.");
                return false;
            }

            // Determine prefab ID and object type for this placement mode
            (string prefabId, ZooObjectType objType) = GetPrefabForMode(mode, floor.EnclosureType);
            if (string.IsNullOrEmpty(prefabId))
            {
                Debug.LogWarning("[ZooObjectPlacer] No prefab configured for this placement mode.");
                return false;
            }

            if (!m_PrefabDict.TryGetValue(prefabId, out GameObject prefab))
            {
                Debug.LogWarning($"[ZooObjectPlacer] Prefab '{prefabId}' not found in library.");
                return false;
            }

            // Face the camera (yaw only)
            Vector3 faceDir = m_ARCamera.transform.position - hit.point;
            faceDir.y = 0f;
            Quaternion rotation = faceDir.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(faceDir.normalized)
                : Quaternion.identity;

            // Build and execute a PlaceObjectCommand
            var cmd = new PlaceObjectCommand(
                prefab, prefabId, hit.point, rotation,
                objType, floor,
                ZooManager.Instance.zooRoot);

            cmd.Execute();

            // Validate no overlap after placing
            var placed = cmd.SpawnedObject;
            if (placed != null)
            {
                var zooObj = placed.GetComponent<ZooObject>();
                if (zooObj != null && zooObj.OverlapsAny())
                {
                    Debug.LogWarning("[ZooObjectPlacer] Object overlaps existing object. Cancelling.");
                    Object.Destroy(placed);
                    return false;
                }
            }

            // Register the command in undo history (already executed above, push manually)
            UndoRedoManager.Instance?.Execute(new AlreadyExecutedCommand(cmd));

            return true;
        }

        (string prefabId, ZooObjectType type) GetPrefabForMode(PlacementMode mode, EnclosureType encType)
        {
            switch (mode)
            {
                case PlacementMode.Fence:  return (m_FencePrefabId,  ZooObjectType.Fence);
                case PlacementMode.Gate:   return (m_GatePrefabId,   ZooObjectType.Gate);
                case PlacementMode.Bin:    return (m_BinPrefabId,    ZooObjectType.Bin);
                case PlacementMode.Animal:
                    switch (m_TargetAnimalEnclosureType)
                    {
                        case EnclosureType.Enclosure1: return (m_AnimalEnc1Id, ZooObjectType.Animal);
                        case EnclosureType.Enclosure2: return (m_AnimalEnc2Id, ZooObjectType.Animal);
                        case EnclosureType.Enclosure3: return (m_AnimalEnc3Id, ZooObjectType.Animal);
                    }
                    break;
            }
            return (null, ZooObjectType.Fence);
        }

        // ── Ghost preview ────────────────────────────────────────────────────

        void Update()
        {
            UpdateGhostPreview();
        }

        void UpdateGhostPreview()
        {
            if (m_GhostObject == null) return;

            PlacementMode mode = ZooManager.Instance?.CurrentPlacementMode ?? PlacementMode.None;
            if (mode == PlacementMode.None || mode == PlacementMode.EnclosureFloor || mode == PlacementMode.Path)
            {
                m_GhostObject.SetActive(false);
                return;
            }

            Ray ray = m_ARCamera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, m_EnclosureLayerMask))
            {
                m_GhostObject.SetActive(true);
                m_GhostObject.transform.position = hit.point;
                Vector3 dir = m_ARCamera.transform.position - hit.point;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    m_GhostObject.transform.rotation = Quaternion.LookRotation(dir.normalized);
            }
            else
            {
                m_GhostObject.SetActive(false);
            }
        }

        // ── Prefab lookup (used by DeleteObjectCommand for undo) ─────────────

        public GameObject GetPrefabById(string id)
        {
            if (m_PrefabDict == null) return null;
            m_PrefabDict.TryGetValue(id, out GameObject prefab);
            return prefab;
        }
    }

    // ── Helper: wrap an already-executed command for the undo stack ───────────

    /// <summary>
    /// Wraps a command that has already been executed so it can be pushed
    /// to UndoRedoManager without being executed a second time.
    /// </summary>
    public class AlreadyExecutedCommand : IZooCommand
    {
        readonly IZooCommand m_Inner;
        public AlreadyExecutedCommand(IZooCommand inner) => m_Inner = inner;
        public void Execute() { /* already done */ }
        public void Undo() => m_Inner.Undo();
    }

    // ── Serializable prefab entry ─────────────────────────────────────────────

    [System.Serializable]
    public class PrefabEntry
    {
        public string id;
        public GameObject prefab;
    }
}
