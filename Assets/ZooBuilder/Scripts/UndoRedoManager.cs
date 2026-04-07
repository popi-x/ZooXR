using System.Collections.Generic;
using UnityEngine;

namespace ZooBuilder
{
    // ── Command interface ────────────────────────────────────────────────────

    public interface IZooCommand
    {
        void Execute();
        void Undo();
    }

    // ── Concrete commands ────────────────────────────────────────────────────

    /// <summary>Place a zoo object (Execute = place, Undo = destroy).</summary>
    public class PlaceObjectCommand : IZooCommand
    {
        readonly GameObject m_Prefab;
        readonly Vector3 m_Position;
        readonly Quaternion m_Rotation;
        readonly ZooObjectType m_Type;
        readonly string m_PrefabId;
        readonly EnclosureFloor m_Floor;
        readonly Transform m_Parent;

        GameObject m_Spawned;

        public PlaceObjectCommand(GameObject prefab, string prefabId, Vector3 pos, Quaternion rot,
                                   ZooObjectType type, EnclosureFloor floor, Transform parent)
        {
            m_Prefab = prefab;
            m_PrefabId = prefabId;
            m_Position = pos;
            m_Rotation = rot;
            m_Type = type;
            m_Floor = floor;
            m_Parent = parent;
        }

        public void Execute()
        {
            m_Spawned = Object.Instantiate(m_Prefab, m_Position, m_Rotation, m_Parent);
            var zooObj = m_Spawned.GetComponent<ZooObject>()
                      ?? m_Spawned.AddComponent<ZooObject>();
            zooObj.Initialize(m_Type, m_PrefabId, m_Floor);

            // Set animal behavior if applicable
            var anim = m_Spawned.GetComponent<AnimalController>();
            if (anim != null && m_Floor != null)
                anim.SetBehaviorFromEnclosure(m_Floor.EnclosureType);
        }

        public void Undo()
        {
            if (m_Spawned != null)
                Object.Destroy(m_Spawned);
        }

        public GameObject SpawnedObject => m_Spawned;
    }

    /// <summary>Delete a zoo object (Execute = destroy, Undo = re-place).</summary>
    public class DeleteObjectCommand : IZooCommand
    {
        readonly GameObject m_Object;
        readonly Vector3 m_Position;
        readonly Quaternion m_Rotation;
        readonly Transform m_Parent;
        readonly ZooObjectType m_Type;
        readonly string m_PrefabId;
        readonly EnclosureFloor m_Floor;
        readonly System.Func<string, GameObject> m_PrefabLookup;

        GameObject m_Restored;

        public DeleteObjectCommand(GameObject obj, System.Func<string, GameObject> prefabLookup)
        {
            m_Object = obj;
            m_Position = obj.transform.position;
            m_Rotation = obj.transform.rotation;
            m_Parent = obj.transform.parent;

            var zooObj = obj.GetComponent<ZooObject>();
            m_Type = zooObj?.ObjectType ?? ZooObjectType.Fence;
            m_PrefabId = zooObj?.PrefabId ?? "";
            m_Floor = zooObj?.ParentEnclosure;
            m_PrefabLookup = prefabLookup;
        }

        public void Execute()
        {
            Object.Destroy(m_Object);
        }

        public void Undo()
        {
            if (m_PrefabLookup == null || string.IsNullOrEmpty(m_PrefabId)) return;
            var prefab = m_PrefabLookup(m_PrefabId);
            if (prefab == null) return;

            m_Restored = Object.Instantiate(prefab, m_Position, m_Rotation, m_Parent);
            var zooObj = m_Restored.GetComponent<ZooObject>()
                      ?? m_Restored.AddComponent<ZooObject>();
            zooObj.Initialize(m_Type, m_PrefabId, m_Floor);
        }
    }

    /// <summary>Translate a zoo object.</summary>
    public class TranslateObjectCommand : IZooCommand
    {
        readonly Transform m_Transform;
        readonly Vector3 m_OldPosition;
        readonly Vector3 m_NewPosition;

        public TranslateObjectCommand(Transform t, Vector3 oldPos, Vector3 newPos)
        {
            m_Transform = t;
            m_OldPosition = oldPos;
            m_NewPosition = newPos;
        }

        public void Execute() { if (m_Transform != null) m_Transform.position = m_NewPosition; }
        public void Undo()    { if (m_Transform != null) m_Transform.position = m_OldPosition; }
    }

    /// <summary>Rotate a zoo object about Y.</summary>
    public class RotateObjectCommand : IZooCommand
    {
        readonly Transform m_Transform;
        readonly Quaternion m_OldRotation;
        readonly Quaternion m_NewRotation;

        public RotateObjectCommand(Transform t, Quaternion oldRot, Quaternion newRot)
        {
            m_Transform = t;
            m_OldRotation = oldRot;
            m_NewRotation = newRot;
        }

        public void Execute() { if (m_Transform != null) m_Transform.rotation = m_NewRotation; }
        public void Undo()    { if (m_Transform != null) m_Transform.rotation = m_OldRotation; }
    }

    // ── Manager ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Manages undo/redo for individual zoo object operations.
    /// Supports at least 10 levels of undo/redo.
    ///
    /// NOTE: Undo/redo is NOT tracked for:
    ///   - Creation/deletion of enclosure floors or paths
    ///   - Translation, rotation, resizing of floors
    ///   - Translation, rotation, scaling of the entire zoo
    /// </summary>
    public class UndoRedoManager : MonoBehaviour
    {
        public static UndoRedoManager Instance { get; private set; }

        [SerializeField] int m_MaxHistory = 10;

        readonly Stack<IZooCommand> m_UndoStack = new Stack<IZooCommand>();
        readonly Stack<IZooCommand> m_RedoStack = new Stack<IZooCommand>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>
        /// Executes a command and adds it to the undo history.
        /// Clears the redo stack.
        /// </summary>
        public void Execute(IZooCommand command)
        {
            command.Execute();
            m_UndoStack.Push(command);
            m_RedoStack.Clear();

            // Trim history if over limit
            if (m_UndoStack.Count > m_MaxHistory)
            {
                var temp = new Stack<IZooCommand>();
                int count = 0;
                foreach (var cmd in m_UndoStack)
                {
                    if (count < m_MaxHistory) temp.Push(cmd);
                    count++;
                }
                m_UndoStack.Clear();
                foreach (var cmd in temp) m_UndoStack.Push(cmd);
            }
        }

        public bool CanUndo => m_UndoStack.Count > 0;
        public bool CanRedo => m_RedoStack.Count > 0;

        public void Undo()
        {
            if (!CanUndo) return;
            var cmd = m_UndoStack.Pop();
            cmd.Undo();
            m_RedoStack.Push(cmd);
        }

        public void Redo()
        {
            if (!CanRedo) return;
            var cmd = m_RedoStack.Pop();
            cmd.Execute();
            m_UndoStack.Push(cmd);
        }

        public void Clear()
        {
            m_UndoStack.Clear();
            m_RedoStack.Clear();
        }
    }
}
