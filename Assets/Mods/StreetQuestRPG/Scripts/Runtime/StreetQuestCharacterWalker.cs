using System;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestCharacterWalker : MonoBehaviour
    {
        private const float NavMeshProbeRadius = 6f;
        private const float ArrivalDistance = 0.35f;

        private string _characterId;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Vector3 _targetPosition;
        private float _walkSpeed;
        private bool _despawnAfterArrival;
        private bool _configured;
        private bool _walking;
        private bool _completed;
        private bool _loggedAnimatorParameters;
        private NavMeshAgent _agent;
        private Animator[] _animators = Array.Empty<Animator>();

        public void Configure(
            string characterId,
            Vector3 spawnPosition,
            Quaternion spawnRotation,
            Vector3 targetPosition,
            float walkSpeed,
            bool despawnAfterArrival)
        {
            _characterId = characterId ?? string.Empty;
            _spawnPosition = spawnPosition;
            _spawnRotation = spawnRotation;
            _targetPosition = targetPosition;
            _walkSpeed = walkSpeed > 0.01f ? walkSpeed : 1.4f;
            _despawnAfterArrival = despawnAfterArrival;
            _animators = GetComponentsInChildren<Animator>(true) ?? Array.Empty<Animator>();
            _configured = true;

            EnsureAgent();
            StreetQuestShared.LogDebug(
                $"WalkAwayConfigured character={_characterId} spawn={FormatVector3(_spawnPosition)} target={FormatVector3(_targetPosition)} " +
                $"speed={_walkSpeed:F2} despawnAfterArrival={_despawnAfterArrival}");
        }

        public void OnVisibilityChanged(bool visible)
        {
            if (!_configured)
                return;

            if (visible)
                BeginWalk();
            else
                ResetWalker();
        }

        private void Update()
        {
            if (!_walking || _agent == null || !_agent.enabled)
                return;

            UpdateAnimatorState(true);

            if (_agent.pathPending)
                return;

            if (_agent.remainingDistance > ArrivalDistance)
                return;

            if (_agent.hasPath && _agent.velocity.sqrMagnitude > 0.01f)
                return;

            _walking = false;
            _completed = true;
            _agent.ResetPath();
            UpdateAnimatorState(false);
            StreetQuestShared.LogDebug($"WalkAwayArrived character={_characterId} final={FormatVector3(transform.position)}");

            if (_despawnAfterArrival)
                HideCharacterPresentation();
        }

        private void BeginWalk()
        {
            EnsureAgent();
            if (_agent == null)
            {
                StreetQuestShared.LogDebug($"WalkAwayBegin failed character={_characterId} reason=agent_missing");
                return;
            }

            if (_completed)
                return;

            if (!TrySampleNavMeshPosition(transform.position, out var startPosition))
            {
                StreetQuestShared.LogDebug(
                    $"WalkAwayBegin failed character={_characterId} reason=start_not_on_navmesh position={FormatVector3(transform.position)}");
                return;
            }

            if (!TrySampleNavMeshPosition(_targetPosition, out var targetPosition))
            {
                StreetQuestShared.LogDebug(
                    $"WalkAwayBegin failed character={_characterId} reason=target_not_on_navmesh target={FormatVector3(_targetPosition)}");
                return;
            }

            transform.position = startPosition;
            _agent.enabled = true;
            _agent.Warp(startPosition);
            _agent.speed = _walkSpeed;
            _agent.isStopped = false;
            _walking = _agent.SetDestination(targetPosition);

            LogAnimatorParametersOnce();
            UpdateAnimatorState(_walking);
            StreetQuestShared.LogDebug(
                $"WalkAwayBegin character={_characterId} start={FormatVector3(startPosition)} target={FormatVector3(targetPosition)} " +
                $"speed={_agent.speed:F2} setDestination={_walking}");
        }

        private void ResetWalker()
        {
            EnsureAgent();
            if (_agent != null && _agent.enabled)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }

            _walking = false;
            _completed = false;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);

            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
                _agent.Warp(_spawnPosition);

            UpdateAnimatorState(false);
            StreetQuestShared.LogDebug($"WalkAwayReset character={_characterId} spawn={FormatVector3(_spawnPosition)}");
        }

        private void HideCharacterPresentation()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                    renderer.enabled = false;
            }

            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                    collider.enabled = false;
            }
        }

        private void EnsureAgent()
        {
            if (_agent == null)
                _agent = GetComponent<NavMeshAgent>();

            if (_agent == null)
                _agent = gameObject.AddComponent<NavMeshAgent>();

            _agent.speed = _walkSpeed > 0.01f ? _walkSpeed : 1.4f;
            _agent.angularSpeed = 240f;
            _agent.acceleration = 8f;
            _agent.stoppingDistance = 0.1f;
            _agent.radius = 0.2f;
            _agent.height = 1.8f;
            _agent.autoBraking = true;
            _agent.updateRotation = true;
            _agent.updateUpAxis = true;
        }

        private void UpdateAnimatorState(bool walking)
        {
            var velocity = _agent != null ? _agent.velocity.magnitude : 0f;
            foreach (var animator in _animators)
            {
                if (animator == null)
                    continue;

                TrySetAnimatorFloat(animator, velocity);
                TrySetAnimatorBool(animator, walking);
            }
        }

        private void LogAnimatorParametersOnce()
        {
            if (_loggedAnimatorParameters)
                return;

            _loggedAnimatorParameters = true;
            foreach (var animator in _animators)
            {
                if (animator == null)
                    continue;

                var parameterSummary = string.Join(
                    ",",
                    animator.parameters.Select(value => $"{value.name}:{value.type}"));
                StreetQuestShared.LogDebug(
                    $"WalkAwayAnimatorParams character={_characterId} animator={animator.name} params=[{parameterSummary}]");
            }
        }

        private static void TrySetAnimatorFloat(Animator animator, float velocity)
        {
            foreach (var parameterName in new[] { "Speed", "speed", "MoveSpeed", "Movement", "Velocity", "WalkSpeed" })
            {
                if (!HasParameter(animator, parameterName, AnimatorControllerParameterType.Float))
                    continue;

                animator.SetFloat(parameterName, velocity);
            }
        }

        private static void TrySetAnimatorBool(Animator animator, bool walking)
        {
            foreach (var parameterName in new[] { "Walking", "IsWalking", "Walk", "Moving", "IsMoving" })
            {
                if (!HasParameter(animator, parameterName, AnimatorControllerParameterType.Bool))
                    continue;

                animator.SetBool(parameterName, walking);
            }
        }

        private static bool HasParameter(Animator animator, string parameterName, AnimatorControllerParameterType type)
        {
            return animator != null &&
                   animator.parameters.Any(value =>
                       value != null &&
                       value.type == type &&
                       string.Equals(value.name, parameterName, StringComparison.Ordinal));
        }

        private static bool TrySampleNavMeshPosition(Vector3 requestedPosition, out Vector3 sampledPosition)
        {
            if (NavMesh.SamplePosition(requestedPosition, out var hit, NavMeshProbeRadius, NavMesh.AllAreas))
            {
                sampledPosition = hit.position;
                return true;
            }

            sampledPosition = requestedPosition;
            return false;
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
        }
    }
}
