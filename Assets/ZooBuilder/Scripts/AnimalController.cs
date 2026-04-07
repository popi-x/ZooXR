using UnityEngine;

namespace ZooBuilder
{
    /// <summary>
    /// Controls animal behavior based on which enclosure it is placed in.
    ///
    ///   Enclosure 1 (Static)  – Animal stands still.
    ///   Enclosure 2 (Roaming) – Animal wanders randomly within the enclosure bounds.
    ///   Enclosure 3 (Hungry)  – Animal moves toward the nearest boundary / aggressively.
    /// </summary>
    [RequireComponent(typeof(ZooObject))]
    public class AnimalController : MonoBehaviour
    {
        [SerializeField] AnimalBehaviorType m_BehaviorType = AnimalBehaviorType.Static;

        [Header("Roaming Settings")]
        [SerializeField] float m_RoamSpeed = 0.3f;
        [SerializeField] float m_RoamRadius = 1.5f;
        [SerializeField] float m_WaypointReachedThreshold = 0.1f;
        [SerializeField] float m_WaypointIdleTime = 2.0f;

        [Header("Hungry Settings")]
        [SerializeField] float m_HungrySpeed = 0.5f;
        [SerializeField] float m_HungryPaceRadius = 0.8f;

        ZooObject m_ZooObject;
        EnclosureFloor m_Enclosure;
        Vector3 m_Origin;           // Spawn position – used as roam center
        Vector3 m_CurrentWaypoint;
        float m_IdleTimer = 0f;
        bool m_Idling = false;
        Animator m_Animator;

        void Awake()
        {
            m_ZooObject = GetComponent<ZooObject>();
            m_Animator = GetComponentInChildren<Animator>();
        }

        void Start()
        {
            m_Enclosure = m_ZooObject.ParentEnclosure;
            m_Origin = transform.position;
            m_CurrentWaypoint = m_Origin;
        }

        /// <summary>
        /// Called after placement to set the behavior type from the enclosure type.
        /// </summary>
        public void SetBehaviorFromEnclosure(EnclosureType enclosureType)
        {
            switch (enclosureType)
            {
                case EnclosureType.Enclosure1: m_BehaviorType = AnimalBehaviorType.Static; break;
                case EnclosureType.Enclosure2: m_BehaviorType = AnimalBehaviorType.Roaming; break;
                case EnclosureType.Enclosure3: m_BehaviorType = AnimalBehaviorType.Hungry; break;
            }
        }

        void Update()
        {
            switch (m_BehaviorType)
            {
                case AnimalBehaviorType.Static:
                    // Do nothing – stand still
                    break;

                case AnimalBehaviorType.Roaming:
                    UpdateRoaming();
                    break;

                case AnimalBehaviorType.Hungry:
                    UpdateHungry();
                    break;
            }
        }

        // ── Roaming behavior ─────────────────────────────────────────────────

        void UpdateRoaming()
        {
            if (m_Idling)
            {
                m_IdleTimer -= Time.deltaTime;
                if (m_IdleTimer <= 0f)
                {
                    m_Idling = false;
                    m_CurrentWaypoint = PickRandomWaypoint();
                }
                return;
            }

            MoveToward(m_CurrentWaypoint, m_RoamSpeed);

            float dist = Vector3.Distance(
                new Vector3(transform.position.x, 0, transform.position.z),
                new Vector3(m_CurrentWaypoint.x, 0, m_CurrentWaypoint.z));

            if (dist < m_WaypointReachedThreshold)
            {
                m_Idling = true;
                m_IdleTimer = Random.Range(m_WaypointIdleTime * 0.5f, m_WaypointIdleTime * 1.5f);
            }
        }

        Vector3 PickRandomWaypoint()
        {
            // Try random positions within roam radius; validate they're inside the enclosure
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * m_RoamRadius;
                Vector3 candidate = m_Origin + new Vector3(offset.x, 0f, offset.y);
                if (m_Enclosure == null || m_Enclosure.ContainsPoint(candidate))
                    return candidate;
            }
            return m_Origin;
        }

        // ── Hungry behavior ──────────────────────────────────────────────────

        void UpdateHungry()
        {
            // Hungry animal paces back and forth aggressively
            if (m_Idling)
            {
                m_IdleTimer -= Time.deltaTime;
                if (m_IdleTimer <= 0f)
                {
                    m_Idling = false;
                    // Pick opposite side of current waypoint
                    Vector3 dir = (m_CurrentWaypoint - m_Origin).normalized;
                    m_CurrentWaypoint = m_Origin - dir * m_HungryPaceRadius;
                    if (m_Enclosure != null && !m_Enclosure.ContainsPoint(m_CurrentWaypoint))
                        m_CurrentWaypoint = m_Origin;
                }
                return;
            }

            MoveToward(m_CurrentWaypoint, m_HungrySpeed);

            float dist = Vector3.Distance(
                new Vector3(transform.position.x, 0, transform.position.z),
                new Vector3(m_CurrentWaypoint.x, 0, m_CurrentWaypoint.z));

            if (dist < m_WaypointReachedThreshold)
            {
                m_Idling = true;
                m_IdleTimer = Random.Range(0.3f, 0.8f);
                if (m_CurrentWaypoint == m_Origin)
                    m_CurrentWaypoint = m_Origin + transform.forward * m_HungryPaceRadius;
            }
        }

        // ── Shared movement ──────────────────────────────────────────────────

        void MoveToward(Vector3 target, float speed)
        {
            Vector3 pos = transform.position;
            Vector3 dir = new Vector3(target.x - pos.x, 0f, target.z - pos.z);
            if (dir.sqrMagnitude < 0.001f) return;

            dir.Normalize();
            transform.position += dir * speed * Time.deltaTime;

            // Face movement direction
            Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5f);

            if (m_Animator != null)
                m_Animator.SetBool("IsWalking", true);
        }

        public AnimalBehaviorType BehaviorType => m_BehaviorType;
    }
}
