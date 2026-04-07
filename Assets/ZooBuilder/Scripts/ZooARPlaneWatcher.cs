using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ZooBuilder
{
    /// <summary>
    /// Watches for the first horizontal AR plane to be detected and transitions
    /// ZooManager from DetectingPlane → PlacingEnclosures.
    ///
    /// Also feeds the detected plane Y level to ZooTransformer so scaling keeps
    /// enclosure floors in the real-world plane.
    ///
    /// Attach to the same GameObject as ARPlaneManager (or any active GameObject).
    /// </summary>
    public class ZooARPlaneWatcher : MonoBehaviour
    {
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] ZooTransformer m_ZooTransformer;

        bool m_FirstPlaneDetected = false;

        void OnEnable()
        {
            if (m_PlaneManager != null)
                m_PlaneManager.trackablesChanged.AddListener(OnPlaneChanged);
        }

        void OnDisable()
        {
            if (m_PlaneManager != null)
                m_PlaneManager.trackablesChanged.RemoveListener(OnPlaneChanged);
        }

        void OnPlaneChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            if (m_FirstPlaneDetected) return;

            foreach (var plane in args.added)
            {
                // Only care about horizontal planes
                if (plane.alignment == PlaneAlignment.HorizontalUp ||
                    plane.alignment == PlaneAlignment.HorizontalDown)
                {
                    m_FirstPlaneDetected = true;

                    float planeY = plane.transform.position.y;
                    m_ZooTransformer?.SetPlaneY(planeY);

                    ZooManager.Instance?.OnPlaneDetected();

                    Debug.Log($"[ZooARPlaneWatcher] First horizontal plane detected at Y={planeY:F3}.");
                    break;
                }
            }
        }
    }
}
