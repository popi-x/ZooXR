using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

namespace ZooBuilder
{
    /// <summary>
    /// Handles transforming the entire zoo (all enclosures, objects, and paths as a unit):
    ///   - Translate parallel to the real-world horizontal plane
    ///   - Rotate about the vertical (Y) axis
    ///   - Scale isotropically (floors remain on the plane)
    ///
    /// These transformations affect only the AR preview and do NOT modify
    /// the saved layout data used for the VR export.
    ///
    /// Attach to the same GameObject as ZooManager, or reference ZooManager.zooRoot.
    /// </summary>
    public class ZooTransformer : MonoBehaviour
    {
        [Header("Scale Constraints")]
        [SerializeField] float m_MinScale = 0.1f;
        [SerializeField] float m_MaxScale = 5.0f;

        [Header("Touch Sensitivity")]
        [SerializeField] float m_TranslateSensitivity = 0.01f;
        [SerializeField] float m_RotateSensitivity = 0.3f;
        [SerializeField] float m_ScaleSensitivity = 0.005f;

        Transform m_ZooRoot;
        float m_CurrentScale = 1.0f;

        // For pinch-to-zoom and twist-to-rotate gesture tracking
        float m_PrevPinchDist = -1f;
        float m_PrevTwistAngle = -1f;
        Vector2 m_PrevDragPos = Vector2.zero;
        bool m_IsDragging = false;

        // AR plane Y level for clamping translation
        float m_PlaneY = 0f;
        bool m_PlaneYSet = false;

        // ── Setup ────────────────────────────────────────────────────────────

        void Start()
        {
            if (ZooManager.Instance != null)
                m_ZooRoot = ZooManager.Instance.zooRoot;
        }

        /// <summary>
        /// Sets the reference Y level (AR plane height) so that the zoo stays on the plane.
        /// Call once when the AR plane is first detected.
        /// </summary>
        public void SetPlaneY(float y)
        {
            m_PlaneY = y;
            m_PlaneYSet = true;
        }

        // ── Direct transform API (called by UI gestures or buttons) ──────────

        /// <summary>
        /// Moves the zoo parallel to the horizontal plane by the given world-space delta.
        /// The Y component is ignored (translation stays in-plane).
        /// </summary>
        public void Translate(Vector3 delta)
        {
            if (m_ZooRoot == null) return;
            Vector3 move = new Vector3(delta.x, 0f, delta.z);
            m_ZooRoot.position += move;
        }

        /// <summary>
        /// Rotates the zoo about the vertical (Y) axis by the given degrees.
        /// </summary>
        public void RotateY(float degrees)
        {
            if (m_ZooRoot == null) return;
            m_ZooRoot.Rotate(Vector3.up, degrees, Space.World);
        }

        /// <summary>
        /// Scales the zoo isotropically. The zoo root scale changes uniformly,
        /// but the position of each enclosure floor is adjusted so that floors
        /// remain at the AR plane Y level.
        /// </summary>
        public void Scale(float factor)
        {
            if (m_ZooRoot == null) return;

            float newScale = Mathf.Clamp(m_CurrentScale * factor, m_MinScale, m_MaxScale);
            float actualFactor = newScale / m_CurrentScale;
            m_CurrentScale = newScale;

            // Scale around the zoo root's position
            m_ZooRoot.localScale *= actualFactor;

            // Re-clamp enclosure floor Y positions to the AR plane
            if (m_PlaneYSet && ZooManager.Instance != null)
            {
                foreach (var enc in ZooManager.Instance.Enclosures)
                {
                    Vector3 p = enc.transform.position;
                    p.y = m_PlaneY;
                    enc.transform.position = p;
                }
            }
        }

        // ── Two-finger gesture handling ──────────────────────────────────────

        /// <summary>
        /// Call every frame with the current touch positions.
        /// Handles:
        ///   - One finger drag  → translate
        ///   - Two finger pinch → scale
        ///   - Two finger twist → rotate
        /// </summary>
        public void HandleTouches(Touch[] touches)
        {
            if (touches.Length == 1)
            {
                HandleSingleFingerDrag(touches[0]);
                m_PrevPinchDist = -1f;
                m_PrevTwistAngle = -1f;
            }
            else if (touches.Length == 2)
            {
                m_IsDragging = false;
                m_PrevDragPos = Vector2.zero;
                HandleTwoFingerGesture(touches[0], touches[1]);
            }
            else
            {
                ResetGestureState();
            }
        }

        void HandleSingleFingerDrag(Touch t)
        {
            if (t.phase == TouchPhase.Began)
            {
                m_PrevDragPos = t.position;
                m_IsDragging = true;
            }
            else if (t.phase == TouchPhase.Moved && m_IsDragging)
            {
                Vector2 delta = t.position - m_PrevDragPos;
                m_PrevDragPos = t.position;
                Translate(new Vector3(delta.x * m_TranslateSensitivity, 0f,
                                      delta.y * m_TranslateSensitivity));
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                m_IsDragging = false;
            }
        }

        void HandleTwoFingerGesture(Touch t0, Touch t1)
        {
            Vector2 pos0 = t0.position;
            Vector2 pos1 = t1.position;

            float pinchDist = Vector2.Distance(pos0, pos1);
            float twistAngle = Mathf.Atan2(pos1.y - pos0.y, pos1.x - pos0.x) * Mathf.Rad2Deg;

            if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
            {
                m_PrevPinchDist = pinchDist;
                m_PrevTwistAngle = twistAngle;
                return;
            }

            if (m_PrevPinchDist > 0f)
            {
                float pinchDelta = pinchDist - m_PrevPinchDist;
                if (Mathf.Abs(pinchDelta) > 1f)
                    Scale(1f + pinchDelta * m_ScaleSensitivity);
            }

            if (m_PrevTwistAngle >= 0f)
            {
                float twistDelta = Mathf.DeltaAngle(m_PrevTwistAngle, twistAngle);
                if (Mathf.Abs(twistDelta) > 0.5f)
                    RotateY(-twistDelta * m_RotateSensitivity);
            }

            m_PrevPinchDist = pinchDist;
            m_PrevTwistAngle = twistAngle;
        }

        void ResetGestureState()
        {
            m_PrevPinchDist = -1f;
            m_PrevTwistAngle = -1f;
            m_IsDragging = false;
        }

        void Update()
        {
            // Handle touch gestures automatically each frame
            if (Input.touchCount > 0)
                HandleTouches(Input.touches);
        }

        public float CurrentScale => m_CurrentScale;
    }
}
