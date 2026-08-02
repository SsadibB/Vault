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

        [Header("Animation Settings")]
        [SerializeField] private string animatorBoolName = "IsWalking";

        private NavMeshAgent navAgent;
        private Animator animator;
        private Vector3 startPosition;
        private float waitTimer;
        private bool isWaiting;

        private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
        private int animParamHash;

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

            // Check velocity to update IsWalking animation bool
            bool isMoving = navAgent.velocity.sqrMagnitude > 0.05f && !navAgent.isStopped;

            if (animator != null)
            {
                animator.SetBool(animParamHash, isMoving);
            }

            // Handle destination arrival & waiting logic
            if (isWaiting)
            {
                waitTimer -= Time.deltaTime;
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
