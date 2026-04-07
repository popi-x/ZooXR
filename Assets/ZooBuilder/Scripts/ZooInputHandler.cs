using UnityEngine;
using UnityEngine.EventSystems;

namespace ZooBuilder
{
    /// <summary>
    /// Routes touch input to the correct subsystem based on PlacementMode.
    ///
    /// When an enclosure is selected (PlacementMode.None + enclosure selected):
    ///   - 1-finger drag  → move the enclosure
    ///   - 2-finger pinch → scale the enclosure
    ///   - 2-finger twist → rotate the enclosure
    ///
    /// Otherwise 2-finger gestures go to ZooTransformer (whole zoo).
    /// </summary>
    public class ZooInputHandler : MonoBehaviour
    {
        [SerializeField] EnclosurePlacer m_EnclosurePlacer;
        [SerializeField] ZooObjectPlacer m_ObjectPlacer;
        [SerializeField] PathCreator m_PathCreator;
        [SerializeField] ZooUIManager m_UIManager;
        [SerializeField] ZooTransformer m_ZooTransformer;
        [SerializeField] Camera m_ARCamera;

        [SerializeField] float m_DragThreshold = 10f;
        [SerializeField] float m_EnclosureMoveSensitivity = 0.003f;
        [SerializeField] float m_EnclosureRotateSensitivity = 0.3f;
        [SerializeField] float m_EnclosureScaleSensitivity = 0.003f;

        Vector2 m_TouchStartPos;
        bool m_IsDragging = false;

        // Two-finger state
        float m_PrevPinchDist;
        float m_PrevTwistAngle;
        bool m_TwoFingerActive = false;

        void Update()
        {
            int touchCount = Input.touchCount;
            if (touchCount == 0) { m_TwoFingerActive = false; return; }

            // ── Two-finger gestures ───────────────────────────────────────────
            if (touchCount >= 2)
            {
                m_IsDragging = false;
                HandleTwoFingers();
                return;
            }

            m_TwoFingerActive = false;

            // ── Single-finger ─────────────────────────────────────────────────
            var touch = Input.GetTouch(0);

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                return;

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    m_TouchStartPos = touch.position;
                    m_IsDragging = false;
                    break;

                case TouchPhase.Moved:
                    if (Vector2.Distance(touch.position, m_TouchStartPos) > m_DragThreshold)
                    {
                        m_IsDragging = true;
                        OnSingleDrag(touch.position, touch.deltaPosition);
                    }
                    break;

                case TouchPhase.Ended:
                    if (!m_IsDragging)
                        OnTap(touch.position);
                    m_IsDragging = false;
                    break;
            }
        }

        // ── Two-finger handling ───────────────────────────────────────────────

        void HandleTwoFingers()
        {
            var t0 = Input.GetTouch(0);
            var t1 = Input.GetTouch(1);

            float pinchDist  = Vector2.Distance(t0.position, t1.position);
            Vector2 midpoint = (t0.position + t1.position) * 0.5f;
            float angle      = Mathf.Atan2(t1.position.y - t0.position.y,
                                           t1.position.x - t0.position.x) * Mathf.Rad2Deg;

            if (!m_TwoFingerActive)
            {
                m_PrevPinchDist  = pinchDist;
                m_PrevTwistAngle = angle;
                m_TwoFingerActive = true;
                return;
            }

            float pinchDelta = pinchDist - m_PrevPinchDist;
            float twistDelta = Mathf.DeltaAngle(m_PrevTwistAngle, angle);

            m_PrevPinchDist  = pinchDist;
            m_PrevTwistAngle = angle;

            bool enclosureSelected = m_UIManager != null && m_UIManager.HasSelectedEnclosure;

            if (enclosureSelected)
            {
                // Apply gestures to selected enclosure
                if (Mathf.Abs(pinchDelta) > 0.5f)
                {
                    float factor = 1f + pinchDelta * m_EnclosureScaleSensitivity;
                    m_UIManager.GestureScaleEnclosure(factor);
                }
                if (Mathf.Abs(twistDelta) > 0.2f)
                {
                    m_UIManager.GestureRotateEnclosure(-twistDelta * m_EnclosureRotateSensitivity * 10f);
                }
            }
            else
            {
                // Apply gestures to whole zoo
                m_ZooTransformer?.HandleTwoFingerGesture(pinchDelta, twistDelta, midpoint);
            }
        }

        // ── Single-finger drag ────────────────────────────────────────────────

        void OnSingleDrag(Vector2 screenPos, Vector2 screenDelta)
        {
            // Path drawing
            if (ZooManager.Instance?.CurrentPhase == BuildPhase.CreatingPath)
            {
                if (m_PathCreator != null && m_PathCreator.IsDrawing)
                    m_PathCreator.AddPathPoint(screenPos);
                return;
            }

            // Move selected enclosure
            bool enclosureSelected = m_UIManager != null && m_UIManager.HasSelectedEnclosure;
            if (enclosureSelected && ZooManager.Instance?.CurrentPlacementMode == PlacementMode.None)
            {
                // Convert screen delta to world-space XZ movement
                Vector3 worldDelta = Vector3.zero;
                if (m_ARCamera != null)
                {
                    Vector3 right   = m_ARCamera.transform.right;
                    Vector3 forward = m_ARCamera.transform.forward;
                    right.y = 0f; right.Normalize();
                    forward.y = 0f; forward.Normalize();
                    worldDelta = (right * screenDelta.x + forward * screenDelta.y)
                                 * m_EnclosureMoveSensitivity;
                }
                m_UIManager.GestureMoveEnclosure(worldDelta);
            }
        }

        // ── Tap ───────────────────────────────────────────────────────────────

        void OnTap(Vector2 screenPos)
        {
            if (ZooManager.Instance == null) return;

            switch (ZooManager.Instance.CurrentPlacementMode)
            {
                case PlacementMode.EnclosureFloor:
                    m_EnclosurePlacer?.TryPlace(screenPos);
                    break;

                case PlacementMode.Fence:
                case PlacementMode.Gate:
                case PlacementMode.Bin:
                case PlacementMode.Animal:
                    m_ObjectPlacer?.TryPlaceObject(screenPos);
                    break;

                case PlacementMode.Path:
                    m_PathCreator?.PlacePathSegment(screenPos);
                    break;

                case PlacementMode.None:
                    TrySelect(screenPos);
                    break;
            }
        }

        // ── Selection ─────────────────────────────────────────────────────────

        void TrySelect(Vector2 screenPos)
        {
            if (Camera.main == null) return;

            Ray ray = Camera.main.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                // Tapped empty space — deselect
                m_UIManager?.DeselectEnclosure();
                return;
            }

            var zooObj = hit.collider.GetComponentInParent<ZooObject>();
            if (zooObj != null)
            {
                m_UIManager?.SelectObject(zooObj);
                return;
            }

            var floor = hit.collider.GetComponentInParent<EnclosureFloor>();
            if (floor != null)
                m_UIManager?.SelectEnclosure(floor);
            else
                m_UIManager?.DeselectEnclosure();
        }
    }
}
