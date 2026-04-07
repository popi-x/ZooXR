using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZooBuilder
{
    /// <summary>
    /// Central singleton manager for the Zoo Builder AR app.
    /// Tracks global state, enclosure registration, and phase transitions.
    /// </summary>
    public class ZooManager : MonoBehaviour
    {
        public static ZooManager Instance { get; private set; }

        [Header("Zoo Root")]
        [Tooltip("Parent transform for all zoo objects. Moving this transform moves the whole zoo.")]
        [SerializeField] Transform m_ZooRoot;

        [Header("Minimum Enclosure Size")]
        [Tooltip("Minimum side length (meters) to discourage tiny enclosures.")]
        [SerializeField] float m_MinEnclosureSize = 1.0f;

        public Transform zooRoot => m_ZooRoot;
        public float minEnclosureSize => m_MinEnclosureSize;

        public BuildPhase CurrentPhase { get; private set; } = BuildPhase.DetectingPlane;
        public PlacementMode CurrentPlacementMode { get; private set; } = PlacementMode.None;
        public bool PathCreationStarted { get; private set; } = false;

        public List<EnclosureFloor> Enclosures { get; private set; } = new List<EnclosureFloor>();

        // Events
        public event Action<BuildPhase> OnPhaseChanged;
        public event Action<PlacementMode> OnPlacementModeChanged;
        public event Action OnPathCreationStarted;
        public event Action OnEnclosureListChanged;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (m_ZooRoot == null)
            {
                var root = new GameObject("ZooRoot");
                m_ZooRoot = root.transform;
            }
        }

        // ── Phase management ────────────────────────────────────────────────

        public void SetPhase(BuildPhase phase)
        {
            if (CurrentPhase == phase) return;
            CurrentPhase = phase;
            OnPhaseChanged?.Invoke(phase);
        }

        public void SetPlacementMode(PlacementMode mode)
        {
            if (CurrentPlacementMode == mode)
            {
                // Toggle off
                CurrentPlacementMode = PlacementMode.None;
            }
            else
            {
                CurrentPlacementMode = mode;
            }
            OnPlacementModeChanged?.Invoke(CurrentPlacementMode);
        }

        public void SetPlacementModeExplicit(PlacementMode mode)
        {
            CurrentPlacementMode = mode;
            OnPlacementModeChanged?.Invoke(CurrentPlacementMode);
        }

        public void OnPlaneDetected()
        {
            if (CurrentPhase == BuildPhase.DetectingPlane)
                SetPhase(BuildPhase.PlacingEnclosures);
        }

        public bool TryStartPathCreation()
        {
            if (Enclosures.Count < 3)
            {
                Debug.LogWarning("[ZooManager] Need 3 enclosures before starting path creation.");
                return false;
            }
            PathCreationStarted = true;
            SetPhase(BuildPhase.CreatingPath);
            OnPathCreationStarted?.Invoke();
            return true;
        }

        public void FinishPathCreation()
        {
            SetPhase(BuildPhase.PlacingObjects);
        }

        // ── Enclosure registration ──────────────────────────────────────────

        public void RegisterEnclosure(EnclosureFloor floor)
        {
            if (!Enclosures.Contains(floor))
            {
                Enclosures.Add(floor);
                OnEnclosureListChanged?.Invoke();
            }
        }

        public void UnregisterEnclosure(EnclosureFloor floor)
        {
            if (Enclosures.Remove(floor))
                OnEnclosureListChanged?.Invoke();
        }

        /// <summary>
        /// Returns the next available enclosure type (1, 2, or 3).
        /// Returns None if all three are taken.
        /// </summary>
        public EnclosureType GetNextEnclosureType()
        {
            bool has1 = false, has2 = false, has3 = false;
            foreach (var e in Enclosures)
            {
                if (e.EnclosureType == EnclosureType.Enclosure1) has1 = true;
                if (e.EnclosureType == EnclosureType.Enclosure2) has2 = true;
                if (e.EnclosureType == EnclosureType.Enclosure3) has3 = true;
            }
            if (!has1) return EnclosureType.Enclosure1;
            if (!has2) return EnclosureType.Enclosure2;
            if (!has3) return EnclosureType.Enclosure3;
            return EnclosureType.None;
        }

        // ── Overlap detection ───────────────────────────────────────────────

        /// <summary>
        /// Checks if the given world-space polygon points overlap any existing enclosure
        /// (other than 'exclude').
        /// </summary>
        public bool PolygonOverlapsAnyEnclosure(Vector3[] worldPoints, EnclosureFloor exclude = null)
        {
            foreach (var enc in Enclosures)
            {
                if (enc == exclude) continue;
                if (enc.OverlapsPolygon(worldPoints))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the enclosure floor that contains the given world position (XZ plane check).
        /// Returns null if none.
        /// </summary>
        public EnclosureFloor GetEnclosureAt(Vector3 worldPos)
        {
            foreach (var enc in Enclosures)
            {
                if (enc.ContainsPoint(worldPos))
                    return enc;
            }
            return null;
        }
    }
}
