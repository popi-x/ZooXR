using UnityEngine;

namespace ZooBuilder
{
    /// <summary>
    /// Fence segment that can be scaled only along its length (local X axis).
    /// Height and width remain constant as required by the assignment.
    /// </summary>
    [RequireComponent(typeof(ZooObject))]
    public class FenceSegment : MonoBehaviour
    {
        [Header("Length Constraints")]
        [SerializeField] float m_MinLength = 0.3f;
        [SerializeField] float m_MaxLength = 5.0f;

        [Tooltip("Initial length (local X scale = 1 corresponds to this many meters).")]
        [SerializeField] float m_BaseLength = 1.0f;

        // Current length in meters
        float m_CurrentLength;

        void Awake()
        {
            m_CurrentLength = m_BaseLength * transform.localScale.x;
        }

        /// <summary>
        /// Sets the fence length in meters, clamped to [MinLength, MaxLength].
        /// Only scales the X axis; Y and Z remain unchanged.
        /// </summary>
        public void SetLength(float meters)
        {
            meters = Mathf.Clamp(meters, m_MinLength, m_MaxLength);
            m_CurrentLength = meters;

            Vector3 s = transform.localScale;
            s.x = meters / m_BaseLength;
            transform.localScale = s;
        }

        /// <summary>
        /// Adjusts the length by a delta (positive = longer, negative = shorter).
        /// </summary>
        public void AdjustLength(float delta)
        {
            SetLength(m_CurrentLength + delta);
        }

        public float CurrentLength => m_CurrentLength;
        public float MinLength => m_MinLength;
        public float MaxLength => m_MaxLength;
    }
}
