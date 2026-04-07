using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ZooBuilder
{
    /// <summary>
    /// Master UI controller for the Zoo Builder AR app.
    ///
    /// Manages:
    ///   - Phase-appropriate button panels (detecting plane, placing enclosures,
    ///     drawing path, placing objects)
    ///   - 3D object-selection menu (fence, gate, bin, animal per enclosure type)
    ///   - Status/requirements display
    ///   - Undo/Redo, Save, Check Requirements buttons
    ///   - Enclosure editing controls (translate, rotate, scale) while no path active
    /// </summary>
    public class ZooUIManager : MonoBehaviour
    {
        // ── Inspector references ─────────────────────────────────────────────

        [Header("Phase Panels")]
        [SerializeField] GameObject m_PanelDetecting;
        [SerializeField] GameObject m_PanelPlacingEnclosures;
        [SerializeField] GameObject m_PanelPathCreation;
        [SerializeField] GameObject m_PanelPlacingObjects;
        [SerializeField] GameObject m_PanelComplete;

        [Header("Enclosure Placement")]
        [SerializeField] Button m_BtnAddCorner;
        [SerializeField] Button m_BtnRemoveCorner;
        [SerializeField] Button m_BtnConfirmEnclosure;
        [SerializeField] Button m_BtnCancelEnclosure;
        [SerializeField] TextMeshProUGUI m_LblEnclosureStatus;
        [SerializeField] Button m_BtnStartPathCreation;

        [Header("Path Creation")]
        [SerializeField] Button m_BtnBeginDraw;
        [SerializeField] Button m_BtnStopDraw;
        [SerializeField] Button m_BtnFinalizePath;
        [SerializeField] TextMeshProUGUI m_LblPathStatus;

        [Header("Object Placement Menu")]
        [SerializeField] GameObject m_ObjectMenu;
        [SerializeField] Button m_BtnOpenObjectMenu;
        [SerializeField] Button m_BtnCloseObjectMenu;

        // Buttons to select which object to place
        [SerializeField] Button m_BtnSelectFence;
        [SerializeField] Button m_BtnSelectGate;
        [SerializeField] Button m_BtnSelectBin;
        [SerializeField] Button m_BtnSelectAnimalEnc1;
        [SerializeField] Button m_BtnSelectAnimalEnc2;
        [SerializeField] Button m_BtnSelectAnimalEnc3;

        [Header("Object Editing")]
        [SerializeField] Button m_BtnDeleteObject;
        [SerializeField] Button m_BtnUndo;
        [SerializeField] Button m_BtnRedo;

        [Header("Zoo Transform Buttons")]
        [SerializeField] Button m_BtnTranslateMode;
        [SerializeField] Button m_BtnRotateMode;
        [SerializeField] Button m_BtnScaleMode;

        [Header("Enclosure Edit Buttons (pre-path)")]
        [SerializeField] GameObject m_PanelEnclosureEdit;
        [SerializeField] Button m_BtnDeleteEnclosure;
        [SerializeField] Button m_BtnMoveEnclosureLeft;
        [SerializeField] Button m_BtnMoveEnclosureRight;
        [SerializeField] Button m_BtnMoveEnclosureFwd;
        [SerializeField] Button m_BtnMoveEnclosureBack;
        [SerializeField] Button m_BtnRotateEnclosureCW;
        [SerializeField] Button m_BtnRotateEnclosureCCW;
        [SerializeField] Button m_BtnScaleEnclosureUp;
        [SerializeField] Button m_BtnScaleEnclosureDown;

        [Header("Requirements & Save")]
        [SerializeField] Button m_BtnCheckRequirements;
        [SerializeField] Button m_BtnSave;
        [SerializeField] TextMeshProUGUI m_LblRequirements;
        [SerializeField] GameObject m_PanelRequirements;

        [Header("Misc")]
        [SerializeField] TextMeshProUGUI m_LblPhase;
        [SerializeField] TextMeshProUGUI m_LblCornerCount;

        [Header("System References")]
        [SerializeField] EnclosurePlacer m_EnclosurePlacer;
        [SerializeField] PathCreator m_PathCreator;
        [SerializeField] ZooObjectPlacer m_ObjectPlacer;
        [SerializeField] ZooSaveManager m_SaveManager;
        [SerializeField] ZooRequirementsChecker m_RequirementsChecker;
        [SerializeField] ZooTransformer m_ZooTransformer;

        [Header("Enclosure Edit Settings")]
        [SerializeField] float m_EnclosureMoveStep = 0.1f;
        [SerializeField] float m_EnclosureRotateStep = 15f;
        [SerializeField] float m_EnclosureScaleStep = 1.1f;

        EnclosureFloor m_SelectedEnclosure = null;
        ZooObject m_SelectedObject = null;

        // ── Lifecycle ────────────────────────────────────────────────────────

        void OnEnable()
        {
            if (ZooManager.Instance != null)
            {
                ZooManager.Instance.OnPhaseChanged += HandlePhaseChanged;
                ZooManager.Instance.OnEnclosureListChanged += RefreshEnclosureUI;
            }

            WireButtons();
        }

        void OnDisable()
        {
            if (ZooManager.Instance != null)
            {
                ZooManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
                ZooManager.Instance.OnEnclosureListChanged -= RefreshEnclosureUI;
            }
        }

        void Start()
        {
            ShowOnlyPanel(m_PanelDetecting);
            HideObjectMenu();
            if (m_PanelRequirements != null) m_PanelRequirements.SetActive(false);
            if (m_PanelEnclosureEdit != null) m_PanelEnclosureEdit.SetActive(false);
            RefreshUndoRedoButtons();
        }

        // ── Button wiring ────────────────────────────────────────────────────

        void WireButtons()
        {
            // Enclosure placement — taps are handled by ZooInputHandler (TryPlace)
            AddClick(m_BtnCancelEnclosure, () => m_EnclosurePlacer?.CancelPlacement());
            AddClick(m_BtnStartPathCreation,  OnStartPathCreation);

            // Path creation
            AddClick(m_BtnBeginDraw,   () => m_PathCreator?.BeginPath());
            AddClick(m_BtnStopDraw,    () => m_PathCreator?.StopDrawing());
            AddClick(m_BtnFinalizePath, OnFinalizePath);

            // Object menu
            AddClick(m_BtnOpenObjectMenu,  ShowObjectMenu);
            AddClick(m_BtnCloseObjectMenu, HideObjectMenu);

            // Object selection
            AddClick(m_BtnSelectFence,       () => SetPlacement(PlacementMode.Fence));
            AddClick(m_BtnSelectGate,        () => SetPlacement(PlacementMode.Gate));
            AddClick(m_BtnSelectBin,         () => SetPlacement(PlacementMode.Bin));
            AddClick(m_BtnSelectAnimalEnc1,  () => SetAnimalPlacement(EnclosureType.Enclosure1));
            AddClick(m_BtnSelectAnimalEnc2,  () => SetAnimalPlacement(EnclosureType.Enclosure2));
            AddClick(m_BtnSelectAnimalEnc3,  () => SetAnimalPlacement(EnclosureType.Enclosure3));

            // Object editing
            AddClick(m_BtnDeleteObject, OnDeleteSelectedObject);
            AddClick(m_BtnUndo,         () => { UndoRedoManager.Instance?.Undo(); RefreshUndoRedoButtons(); });
            AddClick(m_BtnRedo,         () => { UndoRedoManager.Instance?.Redo(); RefreshUndoRedoButtons(); });

            // Enclosure editing
            AddClick(m_BtnDeleteEnclosure,    OnDeleteSelectedEnclosure);
            AddClick(m_BtnMoveEnclosureLeft,  () => MoveEnclosure(Vector3.left));
            AddClick(m_BtnMoveEnclosureRight, () => MoveEnclosure(Vector3.right));
            AddClick(m_BtnMoveEnclosureFwd,   () => MoveEnclosure(Vector3.forward));
            AddClick(m_BtnMoveEnclosureBack,  () => MoveEnclosure(Vector3.back));
            AddClick(m_BtnRotateEnclosureCW,  () => RotateEnclosure(-m_EnclosureRotateStep));
            AddClick(m_BtnRotateEnclosureCCW, () => RotateEnclosure(m_EnclosureRotateStep));
            AddClick(m_BtnScaleEnclosureUp,   () => ScaleEnclosure(m_EnclosureScaleStep));
            AddClick(m_BtnScaleEnclosureDown, () => ScaleEnclosure(1f / m_EnclosureScaleStep));

            // Requirements & Save
            AddClick(m_BtnCheckRequirements, ShowRequirements);
            AddClick(m_BtnSave,              OnSave);
        }

        static void AddClick(Button btn, System.Action action)
        {
            if (btn != null) btn.onClick.AddListener(() => action());
        }

        // ── Phase handling ───────────────────────────────────────────────────

        void HandlePhaseChanged(BuildPhase phase)
        {
            switch (phase)
            {
                case BuildPhase.DetectingPlane:
                    ShowOnlyPanel(m_PanelDetecting);
                    break;
                case BuildPhase.PlacingEnclosures:
                    ShowOnlyPanel(m_PanelPlacingEnclosures);
                    m_EnclosurePlacer?.BeginPlacement();
                    break;
                case BuildPhase.CreatingPath:
                    ShowOnlyPanel(m_PanelPathCreation);
                    if (m_PanelEnclosureEdit != null) m_PanelEnclosureEdit.SetActive(false);
                    break;
                case BuildPhase.PlacingObjects:
                    ShowOnlyPanel(m_PanelPlacingObjects);
                    break;
                case BuildPhase.Complete:
                    ShowOnlyPanel(m_PanelComplete);
                    break;
            }

            if (m_LblPhase != null)
                m_LblPhase.text = phase.ToString().Replace("_", " ");
        }

        // ── Enclosure actions ─────────────────────────────────────────────────

        // Enclosure placement now happens via tap (ZooInputHandler → EnclosurePlacer.TryPlace).
        // ZooManager fires OnEnclosureListChanged which calls RefreshEnclosureUI automatically.

        void RefreshEnclosureUI()
        {
            int count = ZooManager.Instance?.Enclosures.Count ?? 0;
            if (m_LblEnclosureStatus != null)
                m_LblEnclosureStatus.text = $"Enclosures: {count}/3";

            if (m_BtnStartPathCreation != null)
                m_BtnStartPathCreation.interactable = count >= 3;

            if (m_LblCornerCount != null && m_EnclosurePlacer != null)
                m_LblCornerCount.text = $"Corners: {m_EnclosurePlacer.CornerCount}";
        }

        void OnStartPathCreation()
        {
            if (ZooManager.Instance?.TryStartPathCreation() == true)
            {
                if (m_PanelEnclosureEdit != null) m_PanelEnclosureEdit.SetActive(false);
            }
        }

        // ── Path actions ──────────────────────────────────────────────────────

        void OnFinalizePath()
        {
            if (m_PathCreator == null) return;
            bool ok = m_PathCreator.TryFinalizePath();
            if (!ok && m_LblPathStatus != null)
                m_LblPathStatus.text = "Path must connect all 3 enclosures!";
        }

        // ── Object placement ──────────────────────────────────────────────────

        void SetPlacement(PlacementMode mode)
        {
            ZooManager.Instance?.SetPlacementMode(mode);
            HideObjectMenu();
        }

        void SetAnimalPlacement(EnclosureType forEnclosure)
        {
            if (m_ObjectPlacer != null)
                m_ObjectPlacer.SetTargetEnclosureType(forEnclosure);
            ZooManager.Instance?.SetPlacementMode(PlacementMode.Animal);
            HideObjectMenu();
        }

        void OnDeleteSelectedObject()
        {
            if (m_SelectedObject == null) return;
            var cmd = new DeleteObjectCommand(m_SelectedObject.gameObject,
                      id => m_ObjectPlacer?.GetPrefabById(id));
            UndoRedoManager.Instance?.Execute(cmd);
            m_SelectedObject = null;
            RefreshUndoRedoButtons();
            if (m_BtnDeleteObject != null) m_BtnDeleteObject.interactable = false;
        }

        // ── Enclosure editing (pre-path) ──────────────────────────────────────

        void OnDeleteSelectedEnclosure()
        {
            if (m_SelectedEnclosure == null) return;
            if (!m_SelectedEnclosure.CanBeDeleted())
            {
                ShowStatusMessage("Cannot delete: enclosure has objects or path is started.");
                return;
            }
            ZooManager.Instance.UnregisterEnclosure(m_SelectedEnclosure);
            Destroy(m_SelectedEnclosure.gameObject);
            m_SelectedEnclosure = null;
            if (m_PanelEnclosureEdit != null) m_PanelEnclosureEdit.SetActive(false);
        }

        void MoveEnclosure(Vector3 dir)
        {
            if (m_SelectedEnclosure == null || !m_SelectedEnclosure.CanBeModified()) return;
            m_SelectedEnclosure.MoveFloor(dir * m_EnclosureMoveStep);
        }

        void RotateEnclosure(float deg)
        {
            if (m_SelectedEnclosure == null || !m_SelectedEnclosure.CanBeModified()) return;
            m_SelectedEnclosure.RotateFloor(deg);
        }

        void ScaleEnclosure(float factor)
        {
            if (m_SelectedEnclosure == null || !m_SelectedEnclosure.CanBeModified()) return;
            // Validate no overlap after scaling
            var verts = m_SelectedEnclosure.WorldVertices;
            if (verts != null && ZooManager.Instance.PolygonOverlapsAnyEnclosure(verts, m_SelectedEnclosure))
            {
                ShowStatusMessage("Cannot scale: would overlap another enclosure.");
                return;
            }
            m_SelectedEnclosure.ScaleFloor(factor);
        }

        // ── Object menu ───────────────────────────────────────────────────────

        void ShowObjectMenu()
        {
            if (m_ObjectMenu != null) m_ObjectMenu.SetActive(true);
        }

        void HideObjectMenu()
        {
            if (m_ObjectMenu != null) m_ObjectMenu.SetActive(false);
        }

        // ── Requirements ──────────────────────────────────────────────────────

        void ShowRequirements()
        {
            if (m_RequirementsChecker == null) return;
            var result = m_RequirementsChecker.CheckAll();

            if (m_LblRequirements != null)
            {
                if (result.AllPassed)
                {
                    m_LblRequirements.text = "<color=green>All requirements met!</color>";
                }
                else
                {
                    string failures = string.Join("\n• ", result.Failures);
                    m_LblRequirements.text = $"<color=red>Not met:</color>\n• {failures}";
                }
            }

            if (m_PanelRequirements != null)
                m_PanelRequirements.SetActive(true);
        }

        // ── Save ──────────────────────────────────────────────────────────────

        void OnSave()
        {
            // Prevent saving if requirements are not met and no existing layout to update
            if (!ZooRequirementsChecker.Instance.AllRequirementsMet())
            {
                ShowStatusMessage("Cannot save: not all zoo requirements are met.\nCheck requirements for details.");
                ShowRequirements();
                return;
            }

            bool ok = m_SaveManager?.SaveLayout() ?? false;
            if (ok)
                ShowStatusMessage($"Zoo saved successfully!");
        }

        // ── Undo/Redo refresh ─────────────────────────────────────────────────

        void RefreshUndoRedoButtons()
        {
            if (m_BtnUndo != null && UndoRedoManager.Instance != null)
                m_BtnUndo.interactable = UndoRedoManager.Instance.CanUndo;
            if (m_BtnRedo != null && UndoRedoManager.Instance != null)
                m_BtnRedo.interactable = UndoRedoManager.Instance.CanRedo;
        }

        // ── Selection ─────────────────────────────────────────────────────────

        public void SelectObject(ZooObject obj)
        {
            m_SelectedObject = obj;
            if (m_BtnDeleteObject != null)
                m_BtnDeleteObject.interactable = obj != null;
        }

        public void SelectEnclosure(EnclosureFloor floor)
        {
            m_SelectedEnclosure = floor;
            bool canEdit = floor != null && floor.CanBeModified();
            if (m_PanelEnclosureEdit != null)
                m_PanelEnclosureEdit.SetActive(canEdit);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        void ShowOnlyPanel(GameObject panel)
        {
            if (m_PanelDetecting != null)       m_PanelDetecting.SetActive(false);
            if (m_PanelPlacingEnclosures != null) m_PanelPlacingEnclosures.SetActive(false);
            if (m_PanelPathCreation != null)    m_PanelPathCreation.SetActive(false);
            if (m_PanelPlacingObjects != null)  m_PanelPlacingObjects.SetActive(false);
            if (m_PanelComplete != null)        m_PanelComplete.SetActive(false);

            if (panel != null) panel.SetActive(true);
        }

        void ShowStatusMessage(string msg)
        {
            // Show in requirements label or a temporary status panel
            if (m_LblRequirements != null)
            {
                m_LblRequirements.text = msg;
                if (m_PanelRequirements != null) m_PanelRequirements.SetActive(true);
            }
            else
            {
                Debug.Log($"[ZooUIManager] Status: {msg}");
            }
        }

        void Update()
        {
            // Refresh corner count label in real time
            if (m_LblCornerCount != null && m_EnclosurePlacer != null)
                m_LblCornerCount.text = $"Corners: {m_EnclosurePlacer.CornerCount} (min 3)";

            RefreshUndoRedoButtons();
        }
    }
}
