using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Vault.AI
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class VillagerAI : MonoBehaviour
    {
        [Header("Wander Settings")]
        [SerializeField] private float wanderRadius = 20f;
        [SerializeField] private float wanderWaitMin = 1.5f;
        [SerializeField] private float wanderWaitMax = 4.0f;
        [SerializeField] private float stoppingDistance = 0.5f;

        [Header("Optimization Settings")]
        [Tooltip("Interval in seconds between AI logic & navigation checks. (0.1 = 10 updates/sec instead of every frame)")]
        [SerializeField] private float aiTickInterval = 0.1f;

        [Header("Animation Settings")]
        [SerializeField] private string animatorBoolName = "IsWalking";

        private NavMeshAgent navAgent;
        private Animator animator;
        private Vector3 startPosition;
        private float waitTimer;
        private bool isWaiting;
        private float nextTickTime;

        private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
        private int animParamHash;
        private bool currentIsMoving;

        private void Awake()
        {
            navAgent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();

            if (!string.IsNullOrEmpty(animatorBoolName))
            {
                animParamHash = Animator.StringToHash(animatorBoolName);
            }
            else
            {
                animParamHash = IsWalkingHash;
            }
        }

        private void Start()
        {
            startPosition = transform.position;

            if (navAgent != null)
            {
                navAgent.stoppingDistance = stoppingDistance;
                PickNewDestination();
            }
        }

        private void Update()
        {
            if (navAgent == null) return;

            // Throttle checks to run on interval (e.g. 10 times/sec) rather than every frame
            if (Time.time < nextTickTime) return;
            nextTickTime = Time.time + aiTickInterval;

            // 1. Movement state caching: Only call SetBool when state changes to avoid GC/native overhead
            bool isMoving = !isWaiting && navAgent.velocity.sqrMagnitude > 0.05f && !navAgent.isStopped;
            if (isMoving != currentIsMoving)
            {
                currentIsMoving = isMoving;
                if (animator != null)
                {
                    animator.SetBool(animParamHash, currentIsMoving);
                }
            }

            // 2. Handle destination arrival & waiting logic
            if (isWaiting)
            {
                waitTimer -= aiTickInterval;
                if (waitTimer <= 0f)
                {
                    isWaiting = false;
                    PickNewDestination();
                }
            }
            else if (!navAgent.pathPending && navAgent.remainingDistance <= navAgent.stoppingDistance)
            {
                isWaiting = true;
                waitTimer = Random.Range(wanderWaitMin, wanderWaitMax);
            }
        }

        public void PickNewDestination()
        {
            if (navAgent == null) return;

            Vector3 randomDirection = Random.insideUnitSphere * wanderRadius + transform.position;
            if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                navAgent.SetDestination(hit.position);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, wanderRadius);
        }
    }
}
