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

        // ── Called by ZooInputHandler ────────────────────────────────────────

        /// <summary>
        /// Apply two-finger pinch/twist to the whole zoo.
        /// Called by ZooInputHandler when no enclosure is selected.
        /// </summary>
        public void HandleTwoFingerGesture(float pinchDelta, float twistDelta, Vector2 midpoint)
        {
            if (Mathf.Abs(pinchDelta) > 0.5f)
                Scale(1f + pinchDelta * m_ScaleSensitivity);

            if (Mathf.Abs(twistDelta) > 0.2f)
                RotateY(-twistDelta * m_RotateSensitivity);
        }

        public float CurrentScale => m_CurrentScale;
    }
}
