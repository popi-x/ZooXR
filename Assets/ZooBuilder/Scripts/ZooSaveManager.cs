using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ZooBuilder
{
    // ── Serializable data structures ─────────────────────────────────────────

    [Serializable]
    public class ObjectData
    {
        public string prefabId;
        public string objectType;   // Fence, Gate, Bin, Animal
        public float posX, posY, posZ;
        public float rotX, rotY, rotZ, rotW;
        public float scaleX, scaleY, scaleZ;
        public string animalBehavior; // Static, Roaming, Hungry (for animals only)
    }

    [Serializable]
    public class EnclosureData
    {
        public int enclosureIndex;  // 1, 2, or 3
        public string enclosureType;
        public float[] verticesX;
        public float[] verticesY;
        public float[] verticesZ;
        public List<ObjectData> objects = new List<ObjectData>();
    }

    [Serializable]
    public class PathData
    {
        public float[] pointsX;
        public float[] pointsY;
        public float[] pointsZ;
    }

    [Serializable]
    public class ZooLayoutData
    {
        public string version = "1.0";
        public string savedAt;
        public float unitsPerMeter = 1.0f; // 1 Unity unit = 1 meter
        public List<EnclosureData> enclosures = new List<EnclosureData>();
        public List<PathData> paths = new List<PathData>();
    }

    // ── Manager ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes the zoo layout to a human-readable JSON file and saves it to
    /// Application.persistentDataPath. 1 Unity unit = 1 meter.
    ///
    /// The layout file is designed to be imported by the VR Zoo Explorer app.
    /// </summary>
    public class ZooSaveManager : MonoBehaviour
    {
        public static ZooSaveManager Instance { get; private set; }

        [SerializeField] string m_FileName = "zoo_layout.json";

        [Header("Path Creator (optional)")]
        [SerializeField] PathCreator m_PathCreator;

        public string SaveFilePath => Path.Combine(Application.persistentDataPath, m_FileName);

        public event Action<string> OnSaveCompleted;   // passes file path
        public event Action<string> OnSaveFailed;      // passes error message

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ── Save ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Collects all zoo data and writes it to the layout JSON file.
        /// </summary>
        public bool SaveLayout()
        {
            if (ZooManager.Instance == null)
            {
                OnSaveFailed?.Invoke("ZooManager not found.");
                return false;
            }

            var layout = new ZooLayoutData();
            layout.savedAt = DateTime.UtcNow.ToString("o");

            // Serialize enclosures and their objects
            foreach (var enc in ZooManager.Instance.Enclosures)
            {
                var encData = SerializeEnclosure(enc);
                layout.enclosures.Add(encData);
            }

            // Serialize path
            if (m_PathCreator != null && m_PathCreator.PathPoints.Count > 0)
            {
                var pathData = SerializePath(m_PathCreator.PathPoints);
                layout.paths.Add(pathData);
            }

            // Write JSON
            try
            {
                string json = JsonUtility.ToJson(layout, prettyPrint: true);
                File.WriteAllText(SaveFilePath, json);
                Debug.Log($"[ZooSaveManager] Zoo saved to: {SaveFilePath}");
                OnSaveCompleted?.Invoke(SaveFilePath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ZooSaveManager] Save failed: {ex.Message}");
                OnSaveFailed?.Invoke(ex.Message);
                return false;
            }
        }

        // ── Serialization helpers ─────────────────────────────────────────────

        EnclosureData SerializeEnclosure(EnclosureFloor enc)
        {
            var data = new EnclosureData();
            data.enclosureIndex = (int)enc.EnclosureType;
            data.enclosureType = enc.EnclosureType.ToString();

            // Store world-space vertices (these are the actual meter-scale positions)
            // We store them relative to the ZooRoot so that the VR app can reconstruct them
            var verts = enc.WorldVertices ?? Array.Empty<Vector3>();
            data.verticesX = new float[verts.Length];
            data.verticesY = new float[verts.Length];
            data.verticesZ = new float[verts.Length];

            Transform zooRoot = ZooManager.Instance?.zooRoot;
            for (int i = 0; i < verts.Length; i++)
            {
                // Convert to ZooRoot-local space (the "canonical" meter-scale coordinates)
                Vector3 local = zooRoot != null
                    ? zooRoot.InverseTransformPoint(verts[i])
                    : verts[i];

                data.verticesX[i] = local.x;
                data.verticesY[i] = local.y;
                data.verticesZ[i] = local.z;
            }

            // Serialize objects on this enclosure
            foreach (var obj in enc.PlacedObjects)
            {
                data.objects.Add(SerializeObject(obj, zooRoot));
            }

            return data;
        }

        ObjectData SerializeObject(ZooObject obj, Transform zooRoot)
        {
            var data = new ObjectData();
            data.prefabId = obj.PrefabId;
            data.objectType = obj.ObjectType.ToString();

            // Store position/rotation in ZooRoot-local space (meter scale)
            Vector3 localPos = zooRoot != null
                ? zooRoot.InverseTransformPoint(obj.transform.position)
                : obj.transform.position;
            Quaternion localRot = zooRoot != null
                ? Quaternion.Inverse(zooRoot.rotation) * obj.transform.rotation
                : obj.transform.rotation;
            Vector3 localScale = obj.transform.lossyScale;  // world scale

            data.posX = localPos.x;
            data.posY = localPos.y;
            data.posZ = localPos.z;

            data.rotX = localRot.x;
            data.rotY = localRot.y;
            data.rotZ = localRot.z;
            data.rotW = localRot.w;

            data.scaleX = localScale.x;
            data.scaleY = localScale.y;
            data.scaleZ = localScale.z;

            // Animal behavior type
            var animal = obj.GetComponent<AnimalController>();
            if (animal != null)
                data.animalBehavior = animal.BehaviorType.ToString();

            return data;
        }

        PathData SerializePath(List<Vector3> points)
        {
            var data = new PathData();
            data.pointsX = new float[points.Count];
            data.pointsY = new float[points.Count];
            data.pointsZ = new float[points.Count];

            Transform zooRoot = ZooManager.Instance?.zooRoot;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 local = zooRoot != null
                    ? zooRoot.InverseTransformPoint(points[i])
                    : points[i];
                data.pointsX[i] = local.x;
                data.pointsY[i] = local.y;
                data.pointsZ[i] = local.z;
            }
            return data;
        }

        // ── Load (optional - check if existing layout file can be loaded) ─────

        /// <summary>
        /// Returns true if a saved layout file exists.
        /// </summary>
        public bool HasSavedLayout() => File.Exists(SaveFilePath);

        /// <summary>
        /// Loads and returns the layout data (for display/debugging).
        /// Does NOT reconstruct the scene.
        /// </summary>
        public ZooLayoutData LoadLayout()
        {
            if (!HasSavedLayout())
            {
                Debug.LogWarning("[ZooSaveManager] No saved layout found.");
                return null;
            }
            try
            {
                string json = File.ReadAllText(SaveFilePath);
                return JsonUtility.FromJson<ZooLayoutData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ZooSaveManager] Load failed: {ex.Message}");
                return null;
            }
        }
    }
}
