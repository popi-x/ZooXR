using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ZooBuilder
{
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

        Vector2 m_TouchStartPos;
        bool m_IsDragging;

        float m_PrevPinchDist;
        float m_PrevTwistAngle;
        bool m_TwoFingerActive;
        bool m_TwoFingerOnEnclosure;

        void OnEnable()  { EnhancedTouchSupport.Enable(); }
        void OnDisable() { EnhancedTouchSupport.Disable(); }

        void Update()
        {
            var touches = Touch.activeTouches;
            if (touches.Count == 0) { m_TwoFingerActive = false; return; }

            if (touches.Count >= 2)
            {
                m_IsDragging = false;
                HandleTwoFingers(touches[0], touches[1]);
                return;
            }

            m_TwoFingerActive = false;

            var touch = touches[0];

            // Skip UI touches – use RaycastAll so it works with both old and new Input System
            if (IsOverUI(touch.screenPosition))
                return;

            if (touch.phase == TouchPhase.Began)
            {
                m_TouchStartPos = touch.screenPosition;
                m_IsDragging = false;
            }
            else if (touch.phase == TouchPhase.Moved)
            {
                if (!m_IsDragging &&
                    Vector2.Distance(touch.screenPosition, m_TouchStartPos) > m_DragThreshold)
                    m_IsDragging = true;

                if (m_IsDragging)
                    OnSingleDrag(touch.screenPosition, touch.delta);
            }
            else if (touch.phase == TouchPhase.Ended)
            {
                if (!m_IsDragging) OnTap(touch.screenPosition);
                m_IsDragging = false;
            }
        }

        // ── Two-finger ────────────────────────────────────────────────────────

        void HandleTwoFingers(Touch t0, Touch t1)
        {
            float pinchDist = Vector2.Distance(t0.screenPosition, t1.screenPosition);
            float angle     = Mathf.Atan2(
                t1.screenPosition.y - t0.screenPosition.y,
                t1.screenPosition.x - t0.screenPosition.x) * Mathf.Rad2Deg;

            if (!m_TwoFingerActive)
            {
                m_TwoFingerOnEnclosure = HitsEnclosure(t0.screenPosition) ||
                                         HitsEnclosure(t1.screenPosition);
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
                Vector2 mid = (t0.screenPosition + t1.screenPosition) * 0.5f;
                m_ZooTransformer?.HandleTwoFingerGesture(pinchDelta, twistDelta, mid);
            }
        }

        // Works with both StandaloneInputModule and InputSystemUIInputModule
        static readonly List<RaycastResult> s_UIRaycastResults = new List<RaycastResult>();
        bool IsOverUI(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;
            var pe = new PointerEventData(EventSystem.current) { position = screenPos };
            s_UIRaycastResults.Clear();
            EventSystem.current.RaycastAll(pe, s_UIRaycastResults);
            return s_UIRaycastResults.Count > 0;
        }

        bool HitsEnclosure(Vector2 screenPos)
        {
            if (Camera.main == null) return false;
            Ray ray = Camera.main.ScreenPointToRay(screenPos);
            return Physics.Raycast(ray, out RaycastHit hit, 100f) &&
                   hit.collider.GetComponentInParent<EnclosureFloor>() != null;
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

            if (m_UIManager != null && m_UIManager.HasSelectedEnclosure &&
                ZooManager.Instance?.CurrentPlacementMode == PlacementMode.None &&
                m_ARCamera != null)
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
                    if (floor != null) m_UIManager?.SelectEnclosure(floor);
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
