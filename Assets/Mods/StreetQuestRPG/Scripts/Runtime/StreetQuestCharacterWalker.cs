using System;
using System.Linq;
using BigAmbitions.SaveSystem.Legacy;
using UnityEngine;
using UnityEngine.AI;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestCharacterWalker : MonoBehaviour
    {
        private const float NavMeshProbeRadius = 6f;
        private const float ArrivalDistance = 0.35f;
        private const float RotationLerpSpeed = 8f;
        private const int MinutesPerDay = 24 * 60;

        private string _characterId;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Vector3[] _walkAwayWaypoints = Array.Empty<Vector3>();
        private float _walkAwaySpeed;
        private bool _hideAfterWalkAway;
        private Vector3[] _walkInWaypoints = Array.Empty<Vector3>();
        private int _walkInArrivalHour;
        private int _walkInArrivalMinute;
        private float _walkInStartMinuteOfDay;
        private float _walkInDurationMinutes;
        private bool _configured;
        private bool _walkingAway;
        private bool _walkAwayCompleted;
        private bool _walkingIn;
        private bool _walkInStartedThisCycle;
        private bool _presentationHidden;
        private bool _loggedAnimatorParameters;
        private bool _externalVisibility;
        private NavMeshAgent _agent;
        private Animator[] _animators = Array.Empty<Animator>();
        private float _lastObservedMinuteOfDay = -1f;
        private float _walkInElapsedMinutes;
        private float _walkInSpeed;
        private float _walkInTotalDistance;
        private int _walkAwayRouteIndex;
        private Vector3[] _walkAwayRoutePoints = Array.Empty<Vector3>();
        private Vector3[] _walkInRoutePoints = Array.Empty<Vector3>();

        public void Configure(
            string characterId,
            Vector3 spawnPosition,
            Quaternion spawnRotation,
            Vector3[] walkAwayWaypoints,
            float walkAwaySpeed,
            bool hideAfterWalkAway,
            Vector3[] walkInWaypoints,
            float walkInSpeed,
            int walkInArrivalHour,
            int walkInArrivalMinute)
        {
            _characterId = characterId ?? string.Empty;
            _spawnPosition = spawnPosition;
            _spawnRotation = spawnRotation;
            _walkAwayWaypoints = walkAwayWaypoints ?? Array.Empty<Vector3>();
            _walkAwaySpeed = walkAwaySpeed > 0.01f ? walkAwaySpeed : 1.4f;
            _hideAfterWalkAway = hideAfterWalkAway;
            _walkInWaypoints = walkInWaypoints ?? Array.Empty<Vector3>();
            _walkInSpeed = walkInSpeed > 0.01f ? walkInSpeed : 6f;
            _walkInArrivalHour = Mathf.Clamp(walkInArrivalHour, 0, 23);
            _walkInArrivalMinute = Mathf.Clamp(walkInArrivalMinute, 0, 59);
            _animators = GetComponentsInChildren<Animator>(true) ?? Array.Empty<Animator>();
            BuildWalkInRoute();
            _configured = true;

            EnsureAgent();
            StreetQuestShared.LogDebug(
                $"WalkCycleConfigured character={_characterId} spawn={FormatVector3(_spawnPosition)} " +
                $"walkAwayWaypoints={_walkAwayWaypoints.Length} walkAwaySpeed={_walkAwaySpeed:F2} hideAfterWalkAway={_hideAfterWalkAway} walkInWaypoints={_walkInWaypoints.Length} " +
                $"walkAwayRoutePoints={_walkAwayRoutePoints.Length} walkInDistance={_walkInTotalDistance:F2} walkInSpeed={_walkInSpeed:F2} " +
                $"walkInDurationMinutes={_walkInDurationMinutes:F2} " +
                $"walkInStartMinuteOfDay={_walkInStartMinuteOfDay} walkInArrival={_walkInArrivalHour:D2}:{_walkInArrivalMinute:D2}");
        }

        public void OnVisibilityChanged(bool visible)
        {
            if (!_configured)
                return;

            _externalVisibility = visible;
            StreetQuestShared.LogDebug(
                $"WalkCycleVisibility character={_characterId} visible={visible} walkingAway={_walkingAway} walkAwayCompleted={_walkAwayCompleted} walkingIn={_walkingIn} walkInStarted={_walkInStartedThisCycle}");

            if (visible)
            {
                if (!_walkAwayCompleted && !_walkingAway && !_walkingIn)
                    BeginWalkAway();

                return;
            }

            if (_walkingIn)
            {
                HideCharacterPresentation();
                return;
            }

            if (_walkAwayCompleted)
            {
                if (_walkInStartedThisCycle)
                    ResetWalker();

                HideCharacterPresentation();
                return;
            }

            ResetWalker();
            HideCharacterPresentation();
        }

        private void Update()
        {
            TickWalkAway();
            TickWalkIn();
        }

        private void TickWalkAway()
        {
            if (!_walkingAway || _agent == null || !_agent.enabled)
                return;

            var velocity = _agent.velocity;
            var isMoving = velocity.sqrMagnitude > 0.01f;
            UpdateAnimatorState(isMoving, velocity.magnitude);
            UpdateFacing(velocity);

            if (_agent.pathPending)
                return;

            if (_agent.remainingDistance > ArrivalDistance)
                return;

            if (_agent.hasPath && _agent.velocity.sqrMagnitude > 0.01f)
                return;

            if (_walkAwayRouteIndex + 1 < _walkAwayRoutePoints.Length)
            {
                _walkAwayRouteIndex++;
                var nextPoint = _walkAwayRoutePoints[_walkAwayRouteIndex];
                if (_agent.SetDestination(nextPoint))
                {
                    StreetQuestShared.LogDebug(
                        $"WalkAwayAdvance character={_characterId} routeIndex={_walkAwayRouteIndex} target={FormatVector3(nextPoint)}");
                    return;
                }
            }

            _walkingAway = false;
            _walkAwayCompleted = true;
            _agent.ResetPath();
            UpdateAnimatorState(false, 0f);
            StreetQuestShared.LogDebug($"WalkAwayArrived character={_characterId} final={FormatVector3(transform.position)}");

            if (_hideAfterWalkAway)
                HideCharacterPresentation();
        }

        private void TickWalkIn()
        {
            if (!_configured || _walkInRoutePoints.Length < 2)
                return;

            if (!TryGetCurrentMinuteOfDay(out var currentMinuteOfDay))
                return;

            if (_lastObservedMinuteOfDay < 0f)
                _lastObservedMinuteOfDay = currentMinuteOfDay;

            if (_walkAwayCompleted && !_walkInStartedThisCycle && ShouldStartWalkIn(currentMinuteOfDay))
                BeginWalkIn(currentMinuteOfDay);

            if (!_walkingIn)
            {
                _lastObservedMinuteOfDay = currentMinuteOfDay;
                return;
            }

            var deltaMinutes = GetMinuteDelta(_lastObservedMinuteOfDay, currentMinuteOfDay);
            _lastObservedMinuteOfDay = currentMinuteOfDay;
            if (deltaMinutes <= 0f)
                return;

            _walkInElapsedMinutes += deltaMinutes;
            var travelledDistance = Mathf.Min(_walkInElapsedMinutes * _walkInSpeed, _walkInTotalDistance);
            var sampledPosition = SampleWalkInPosition(travelledDistance);
            var lookAheadDistance = Mathf.Min(_walkInTotalDistance, travelledDistance + 0.15f);
            var lookAheadPosition = SampleWalkInPosition(lookAheadDistance);
            var travelVector = lookAheadPosition - sampledPosition;
            transform.position = sampledPosition;
            if (travelVector.sqrMagnitude > 0.0001f)
                UpdateFacing(travelVector);

            UpdateAnimatorState(travelledDistance < _walkInTotalDistance, _walkInSpeed);

            if (travelledDistance + 0.001f < _walkInTotalDistance)
                return;

            _walkingIn = false;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            UpdateAnimatorState(false, 0f);
            StreetQuestShared.LogDebug($"WalkInArrived character={_characterId} minuteOfDay={currentMinuteOfDay} final={FormatVector3(transform.position)}");

            if (_externalVisibility)
                HideCharacterPresentation();
            else
            {
                ResetWalker();
                HideCharacterPresentation();
            }
        }

        private void BeginWalkAway()
        {
            EnsureAgent();
            if (_agent == null)
            {
                StreetQuestShared.LogDebug($"WalkAwayBegin failed character={_characterId} reason=agent_missing");
                return;
            }

            if (_walkAwayCompleted)
                return;

            if (_walkAwayRoutePoints.Length < 2)
            {
                StreetQuestShared.LogDebug($"WalkAwayBegin failed character={_characterId} reason=route_too_short");
                return;
            }

            if (!TrySampleNavMeshPosition(transform.position, out var startPosition))
            {
                StreetQuestShared.LogDebug(
                    $"WalkAwayBegin failed character={_characterId} reason=start_not_on_navmesh position={FormatVector3(transform.position)}");
                return;
            }

            ShowCharacterPresentation();
            transform.position = startPosition;
            _agent.enabled = true;
            _agent.Warp(startPosition);
            _agent.speed = _walkAwaySpeed;
            _agent.isStopped = false;
            _walkAwayRouteIndex = 1;
            var firstTarget = _walkAwayRoutePoints[_walkAwayRouteIndex];
            _walkingAway = _agent.SetDestination(firstTarget);

            LogAnimatorParametersOnce();
            UpdateAnimatorState(_walkingAway, _walkingAway ? _agent.speed : 0f);
            StreetQuestShared.LogDebug(
                $"WalkAwayBegin character={_characterId} start={FormatVector3(startPosition)} target={FormatVector3(firstTarget)} " +
                $"speed={_agent.speed:F2} baseOffset={_agent.baseOffset:F2} setDestination={_walkingAway} routePoints={_walkAwayRoutePoints.Length}");
        }

        private void ResetWalker()
        {
            EnsureAgent();
            if (_agent != null && _agent.enabled)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }

            _walkingAway = false;
            _walkAwayCompleted = false;
            _walkingIn = false;
            _walkInStartedThisCycle = false;
            _walkInElapsedMinutes = 0f;
            _walkAwayRouteIndex = 0;
            _lastObservedMinuteOfDay = -1f;
            _presentationHidden = true;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);

            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
                _agent.Warp(_spawnPosition);

            UpdateAnimatorState(false, 0f);
            StreetQuestShared.LogDebug($"WalkAwayReset character={_characterId} spawn={FormatVector3(_spawnPosition)}");
        }

        private void HideCharacterPresentation()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                    renderer.enabled = false;
            }

            SetColliderState(false);
            SetAnimatorState(false);

            _presentationHidden = true;
        }

        private void ShowCharacterPresentation()
        {
            if (!_presentationHidden)
                return;

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                    renderer.enabled = true;
            }

            SetAnimatorState(true);

            _presentationHidden = false;
        }

        private void EnsureAgent()
        {
            if (_agent == null)
                _agent = GetComponent<NavMeshAgent>();

            if (_agent == null)
                _agent = gameObject.AddComponent<NavMeshAgent>();

            _agent.speed = _walkAwaySpeed > 0.01f ? _walkAwaySpeed : 1.4f;
            _agent.angularSpeed = 240f;
            _agent.acceleration = 8f;
            _agent.stoppingDistance = 0.1f;
            _agent.radius = 0.2f;
            _agent.height = 1.8f;
            _agent.baseOffset = 0f;
            _agent.autoBraking = true;
            _agent.updateRotation = true;
            _agent.updateUpAxis = true;
        }

        private void UpdateAnimatorState(bool walking, float velocityMagnitude)
        {
            foreach (var animator in _animators)
            {
                if (animator == null)
                    continue;

                TrySetAnimatorFloat(animator, velocityMagnitude);
                TrySetAnimatorBool(animator, walking);
            }
        }

        private void SetAnimatorState(bool enabled)
        {
            foreach (var animator in _animators)
            {
                if (animator != null)
                    animator.enabled = enabled;
            }
        }

        private void SetColliderState(bool enabled)
        {
            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                    collider.enabled = enabled;
            }
        }

        private void UpdateFacing(Vector3 velocity)
        {
            velocity.y = 0f;
            if (velocity.sqrMagnitude < 0.0001f)
                return;

            var targetRotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * RotationLerpSpeed);
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
            foreach (var parameterName in new[] { "Speed", "speed", "MoveSpeed", "Movement", "Velocity", "WalkSpeed", "AnimationSpeed", "Forward" })
            {
                if (!HasParameter(animator, parameterName, AnimatorControllerParameterType.Float))
                    continue;

                float value;
                if (string.Equals(parameterName, "Forward", StringComparison.Ordinal))
                    value = velocity > 0.01f ? 0.45f : 0f;
                else if (string.Equals(parameterName, "AnimationSpeed", StringComparison.Ordinal))
                    value = velocity > 0.01f ? 0.65f : 0f;
                else
                    value = velocity;

                animator.SetFloat(parameterName, value);
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

            if (HasParameter(animator, "Running", AnimatorControllerParameterType.Bool))
                animator.SetBool("Running", false);
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

        private void BuildWalkInRoute()
        {
            _walkAwayRoutePoints = BuildWalkAwayRoutePoints();
            _walkInRoutePoints = BuildWalkInRoutePoints();
            _walkInTotalDistance = 0f;
            for (var index = 1; index < _walkInRoutePoints.Length; index++)
                _walkInTotalDistance += Vector3.Distance(_walkInRoutePoints[index - 1], _walkInRoutePoints[index]);

            _walkInDurationMinutes = _walkInSpeed > 0.01f
                ? _walkInTotalDistance / _walkInSpeed
                : 0f;
            var arrivalMinuteOfDay = (_walkInArrivalHour * 60f) + _walkInArrivalMinute;
            _walkInStartMinuteOfDay = arrivalMinuteOfDay - _walkInDurationMinutes;
            while (_walkInStartMinuteOfDay < 0)
                _walkInStartMinuteOfDay += MinutesPerDay;
        }

        private void BeginWalkIn(float currentMinuteOfDay)
        {
            _walkInStartedThisCycle = true;
            _walkingIn = true;
            _walkInElapsedMinutes = 0f;
            if (_agent != null && _agent.enabled)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }

            transform.position = _walkInRoutePoints[0];
            ShowCharacterPresentation();
            LogAnimatorParametersOnce();
            UpdateAnimatorState(true, _walkInSpeed);
            StreetQuestShared.LogDebug(
                $"WalkInBegin character={_characterId} minuteOfDay={currentMinuteOfDay} start={FormatVector3(_walkInRoutePoints[0])} " +
                $"arrival={_walkInArrivalHour:D2}:{_walkInArrivalMinute:D2} durationMinutes={_walkInDurationMinutes:F2} routePoints={_walkInRoutePoints.Length}");
        }

        private Vector3 SampleWalkInPosition(float travelledDistance)
        {
            if (_walkInRoutePoints.Length == 0)
                return transform.position;

            if (travelledDistance <= 0f)
                return _walkInRoutePoints[0];

            var remaining = travelledDistance;
            for (var index = 1; index < _walkInRoutePoints.Length; index++)
            {
                var start = _walkInRoutePoints[index - 1];
                var end = _walkInRoutePoints[index];
                var segmentLength = Vector3.Distance(start, end);
                if (segmentLength <= 0.001f)
                    continue;

                if (remaining <= segmentLength)
                {
                    var t = remaining / segmentLength;
                    return Vector3.Lerp(start, end, t);
                }

                remaining -= segmentLength;
            }

            return _walkInRoutePoints[_walkInRoutePoints.Length - 1];
        }

        private bool ShouldStartWalkIn(float currentMinuteOfDay)
        {
            if (_walkInRoutePoints.Length < 2)
                return false;

            var arrivalMinuteOfDay = (_walkInArrivalHour * 60f) + _walkInArrivalMinute;
            if (_walkInStartMinuteOfDay <= arrivalMinuteOfDay)
                return currentMinuteOfDay >= _walkInStartMinuteOfDay && currentMinuteOfDay < arrivalMinuteOfDay;

            return currentMinuteOfDay >= _walkInStartMinuteOfDay || currentMinuteOfDay < arrivalMinuteOfDay;
        }

        private static bool TryGetCurrentMinuteOfDay(out float minuteOfDay)
        {
            minuteOfDay = 0f;
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return false;

            minuteOfDay = (saveGame.Hour * 60f) + Mathf.Clamp(saveGame.Minute, 0f, 59.999f);
            return true;
        }

        private static float GetMinuteDelta(float previousMinuteOfDay, float currentMinuteOfDay)
        {
            if (previousMinuteOfDay < 0f)
                return 0f;

            var delta = currentMinuteOfDay - previousMinuteOfDay;
            if (delta < 0f)
                delta += MinutesPerDay;

            return delta;
        }

        private Vector3[] BuildWalkAwayRoutePoints()
        {
            var explicitWaypoints = _walkAwayWaypoints.Where(value => value != default).ToArray();
            if (explicitWaypoints.Length > 1)
            {
                var points = new Vector3[explicitWaypoints.Length + 1];
                points[0] = _spawnPosition;
                Array.Copy(explicitWaypoints, 0, points, 1, explicitWaypoints.Length);
                return points;
            }

            if (explicitWaypoints.Length == 1)
            {
                var inferredPoints = BuildNavMeshRoutePoints(_spawnPosition, explicitWaypoints[0]);
                if (inferredPoints.Length >= 2)
                    return inferredPoints;

                return new[] { _spawnPosition, explicitWaypoints[0] };
            }

            return Array.Empty<Vector3>();
        }

        private Vector3[] BuildWalkInRoutePoints()
        {
            var explicitWaypoints = _walkInWaypoints.Where(value => value != default).ToArray();
            if (explicitWaypoints.Length > 1)
            {
                var points = new Vector3[explicitWaypoints.Length + 1];
                Array.Copy(explicitWaypoints, 0, points, 0, explicitWaypoints.Length);
                points[points.Length - 1] = _spawnPosition;
                return points;
            }

            if (explicitWaypoints.Length == 1)
            {
                var inferredPoints = BuildNavMeshRoutePoints(explicitWaypoints[0], _spawnPosition);
                if (inferredPoints.Length >= 2)
                    return inferredPoints;

                return new[] { explicitWaypoints[0], _spawnPosition };
            }

            return Array.Empty<Vector3>();
        }

        private Vector3[] BuildNavMeshRoutePoints(Vector3 start, Vector3 end)
        {
            if (!TrySampleNavMeshPosition(start, out var startPosition) ||
                !TrySampleNavMeshPosition(end, out var endPosition))
            {
                return Array.Empty<Vector3>();
            }

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startPosition, endPosition, NavMesh.AllAreas, path) ||
                path.corners == null ||
                path.corners.Length < 2)
            {
                return Array.Empty<Vector3>();
            }

            return path.corners;
        }
    }
}
