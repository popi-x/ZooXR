using UnityEngine;
using UnityEngine.EventSystems;

namespace ZooBuilder
{
    /// <summary>
    /// Routes touch/tap input to the correct subsystem based on the current PlacementMode.
    ///
    ///   - EnclosureFloor mode → EnclosurePlacer.TryAddCorner
    ///   - Fence/Gate/Bin/Animal mode → ZooObjectPlacer.TryPlaceObject
    ///   - Path mode (drag) → PathCreator.AddPathPoint
    ///
    /// This component should be on the same GameObject as ZooManager.
    /// </summary>
    public class ZooInputHandler : MonoBehaviour
    {
        [SerializeField] EnclosurePlacer m_EnclosurePlacer;
        [SerializeField] ZooObjectPlacer m_ObjectPlacer;
        [SerializeField] PathCreator m_PathCreator;
        [SerializeField] ZooUIManager m_UIManager;

        // Drag detection threshold in pixels
        [SerializeField] float m_DragThreshold = 10f;

        Vector2 m_TouchStartPos;
        bool m_IsDragging = false;

        void Update()
        {
            if (Input.touchCount == 0) return;

            var touch = Input.GetTouch(0);

            // Skip if the user is touching a UI element
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
                        OnDrag(touch.position);
                    }
                    break;

                case TouchPhase.Ended:
                    if (!m_IsDragging)
                        OnTap(touch.position);
                    m_IsDragging = false;
                    break;
            }
        }

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
                    // Path is drawn by dragging, but tap can place discrete segments
                    m_PathCreator?.PlacePathSegment(screenPos);
                    break;

                case PlacementMode.None:
                    // Try to select an object or enclosure that was tapped
                    TrySelect(screenPos);
                    break;
            }
        }

        void OnDrag(Vector2 screenPos)
        {
            if (ZooManager.Instance?.CurrentPhase == BuildPhase.CreatingPath)
            {
                if (m_PathCreator != null && m_PathCreator.IsDrawing)
                    m_PathCreator.AddPathPoint(screenPos);
            }
        }

        void TrySelect(Vector2 screenPos)
        {
            if (Camera.main == null) return;

            Ray ray = Camera.main.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;

            // Check for zoo object
            var zooObj = hit.collider.GetComponentInParent<ZooObject>();
            if (zooObj != null)
            {
                m_UIManager?.SelectObject(zooObj);
                return;
            }

            // Check for enclosure floor
            var floor = hit.collider.GetComponentInParent<EnclosureFloor>();
            if (floor != null)
            {
                m_UIManager?.SelectEnclosure(floor);
            }
        }
    }
}
