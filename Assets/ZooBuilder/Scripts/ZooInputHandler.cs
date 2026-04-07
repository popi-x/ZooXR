using UnityEngine;
using UnityEngine.EventSystems;

namespace ZooBuilder
{
    /// <summary>
    /// Routes all touch input:
    ///
    /// Tap:
    ///   - EnclosureFloor mode → place enclosure, then auto-select it
    ///   - Fence/Gate/Bin/Animal → place object
    ///   - Path → place path segment
    ///   - None → select/deselect enclosure or object
    ///
    /// Single-finger drag on selected enclosure → move enclosure
    ///
    /// Two-finger gesture:
    ///   - Started on enclosure → scale/rotate that enclosure
    ///   - Started on empty space → ZooTransformer (whole zoo)
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
        [SerializeField] float m_EnclosureRotateSensitivity = 3f;
        [SerializeField] float m_EnclosureScaleSensitivity = 0.003f;

        // Single-finger state
        Vector2 m_TouchStartPos;
        bool m_IsDragging;

        // Two-finger state
        float m_PrevPinchDist;
        float m_PrevTwistAngle;
        bool m_TwoFingerActive;
        bool m_TwoFingerOnEnclosure; // whether gesture started on enclosure

        void Update()
        {
            int count = Input.touchCount;
            if (count == 0) { m_TwoFingerActive = false; return; }

            if (count >= 2)
            {
                m_IsDragging = false;
                HandleTwoFingers();
                return;
            }

            m_TwoFingerActive = false;

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
                    if (!m_IsDragging && Vector2.Distance(touch.position, m_TouchStartPos) > m_DragThreshold)
                        m_IsDragging = true;
                    if (m_IsDragging)
                        OnSingleDrag(touch.position, touch.deltaPosition);
                    break;

                case TouchPhase.Ended:
                    if (!m_IsDragging) OnTap(touch.position);
                    m_IsDragging = false;
                    break;
            }
        }

        // ── Two-finger ────────────────────────────────────────────────────────

        void HandleTwoFingers()
        {
            var t0 = Input.GetTouch(0);
            var t1 = Input.GetTouch(1);

            float pinchDist = Vector2.Distance(t0.position, t1.position);
            float angle     = Mathf.Atan2(t1.position.y - t0.position.y,
                                          t1.position.x - t0.position.x) * Mathf.Rad2Deg;

            if (!m_TwoFingerActive)
            {
                // Decide target on first frame: check if either finger is over an enclosure
                m_TwoFingerOnEnclosure = HitsEnclosure(t0.position) || HitsEnclosure(t1.position);
                m_PrevPinchDist  = pinchDist;
                m_PrevTwistAngle = angle;
                m_TwoFingerActive = true;
                return;
            }

            float pinchDelta = pinchDist - m_PrevPinchDist;
            float twistDelta = Mathf.DeltaAngle(m_PrevTwistAngle, angle);
            m_PrevPinchDist  = pinchDist;
            m_PrevTwistAngle = angle;

            if (m_TwoFingerOnEnclosure && m_UIManager != null && m_UIManager.HasSelectedEnclosure)
            {
                if (Mathf.Abs(pinchDelta) > 0.5f)
                    m_UIManager.GestureScaleEnclosure(1f + pinchDelta * m_EnclosureScaleSensitivity);
                if (Mathf.Abs(twistDelta) > 0.2f)
                    m_UIManager.GestureRotateEnclosure(-twistDelta * m_EnclosureRotateSensitivity);
            }
            else
            {
                Vector2 mid = (t0.position + t1.position) * 0.5f;
                m_ZooTransformer?.HandleTwoFingerGesture(pinchDelta, twistDelta, mid);
            }
        }

        bool HitsEnclosure(Vector2 screenPos)
        {
            if (Camera.main == null) return false;
            Ray ray = Camera.main.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return false;
            return hit.collider.GetComponentInParent<EnclosureFloor>() != null;
        }

        // ── Single-finger drag ────────────────────────────────────────────────

        void OnSingleDrag(Vector2 screenPos, Vector2 screenDelta)
        {
            if (ZooManager.Instance?.CurrentPhase == BuildPhase.CreatingPath)
            {
                if (m_PathCreator != null && m_PathCreator.IsDrawing)
                    m_PathCreator.AddPathPoint(screenPos);
                return;
            }

            // Move selected enclosure
            if (m_UIManager != null && m_UIManager.HasSelectedEnclosure
                && ZooManager.Instance?.CurrentPlacementMode == PlacementMode.None
                && m_ARCamera != null)
            {
                Vector3 right   = m_ARCamera.transform.right;   right.y = 0f;   right.Normalize();
                Vector3 forward = m_ARCamera.transform.forward; forward.y = 0f; forward.Normalize();
                Vector3 delta   = (right * screenDelta.x + forward * screenDelta.y)
                                  * m_EnclosureMoveSensitivity;
                m_UIManager.GestureMoveEnclosure(delta);
            }
        }

        // ── Tap ───────────────────────────────────────────────────────────────

        void OnTap(Vector2 screenPos)
        {
            if (ZooManager.Instance == null) return;
            Debug.Log($"[ZooInputHandler] Tap at {screenPos}, mode={ZooManager.Instance.CurrentPlacementMode}");

            switch (ZooManager.Instance.CurrentPlacementMode)
            {
                case PlacementMode.EnclosureFloor:
                    var floor = m_EnclosurePlacer?.TryPlace(screenPos);
                    if (floor != null)
                        m_UIManager?.SelectEnclosure(floor); // auto-select for gesture editing
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

        void TrySelect(Vector2 screenPos)
        {
            if (Camera.main == null) return;
            Ray ray = Camera.main.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                m_UIManager?.DeselectEnclosure();
                return;
            }

            var zooObj = hit.collider.GetComponentInParent<ZooObject>();
            if (zooObj != null) { m_UIManager?.SelectObject(zooObj); return; }

            var enclosure = hit.collider.GetComponentInParent<EnclosureFloor>();
            if (enclosure != null) m_UIManager?.SelectEnclosure(enclosure);
            else m_UIManager?.DeselectEnclosure();
        }
    }
}
