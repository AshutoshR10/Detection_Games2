using UnityEngine;
using PoseInteraction.Config;
using PoseInteraction.Core;
using PoseInteraction.Utilities;

namespace PoseInteraction.Physics
{
    /// <summary>
    /// Virtual collider that follows hand position and detects physics interactions
    /// Does NOT use Unity's physics system directly - uses distance-based detection
    /// </summary>
    public class VirtualHandCollider : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private PoseInteractionConfig config;

        [Header("Hand Selection")]
        [SerializeField] private HandType handType = HandType.Right;

        [Header("Visual Feedback")]
        [SerializeField] private GameObject visualIndicator;
        [SerializeField] private Material normalMaterial;
        [SerializeField] private Material activeMaterial;

        public enum HandType { Right, Left }

        // State
        public Vector3 CurrentPosition { get; private set; }
        public Vector3 CurrentVelocity { get; private set; }
        public float CurrentSpeed { get; private set; }
        public bool IsTracked { get; private set; }

        // Events
        public event System.Action<Rigidbody, Vector3, Vector3> OnCollisionDetected;

        // Internal
        private HandPoseTracker _poseTracker;
        private Renderer _visualRenderer;
        private float _lastHitTime;

        private void Awake()
        {
            Debug.unityLogger.logEnabled = false;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            // Config may be null on Awake (set later via SetConfig), that's okay
            // Just initialize visual components

            // Setup visual indicator
            if (visualIndicator == null)
            {
                CreateDefaultVisualIndicator();
            }
            else
            {
                _visualRenderer = visualIndicator.GetComponent<Renderer>();
            }
        }

        private void CreateDefaultVisualIndicator()
        {
            visualIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visualIndicator.transform.SetParent(transform);
            visualIndicator.transform.localPosition = Vector3.zero;

            // Remove collider - this is just visual
            Destroy(visualIndicator.GetComponent<Collider>());

            _visualRenderer = visualIndicator.GetComponent<Renderer>();

            // Create default materials if not assigned
            // Use Unlit/Color for better mobile compatibility
            if (normalMaterial == null)
            {
                normalMaterial = new Material(Shader.Find("Unlit/Color"));
                normalMaterial.color = new Color(1f, 0.75f, 0.8f, 1f); // Pink, fully opaque
            }

            if (activeMaterial == null)
            {
                activeMaterial = new Material(Shader.Find("Unlit/Color"));
                activeMaterial.color = new Color(1f, 0f, 1f, 1f); // Bright magenta when active
            }

            _visualRenderer.material = normalMaterial;

            Debug.Log($"[VirtualHandCollider] Created visual indicator for {handType} hand with Unlit shader");
        }

        public void Initialize(HandPoseTracker poseTracker)
        {
            _poseTracker = poseTracker;
        }

        /// <summary>
        /// Set configuration and hand type (call after creation)
        /// </summary>
        public void SetConfig(PoseInteractionConfig newConfig, HandType newHandType)
        {
            config = newConfig;
            handType = newHandType;
        }

        private void Update()
        {
            if (_poseTracker == null || config == null) return;

            UpdateHandState();
            UpdateVisualIndicator();
        }

        private void UpdateHandState()
        {
            // Get hand data based on type
            HandVelocityTracker velocityTracker;

            if (handType == HandType.Right)
            {
                IsTracked = _poseTracker.IsRightHandTracked;
                CurrentPosition = _poseTracker.RightHandPosition;
                velocityTracker = _poseTracker.RightHandVelocity;
            }
            else
            {
                IsTracked = _poseTracker.IsLeftHandTracked;
                CurrentPosition = _poseTracker.LeftHandPosition;
                velocityTracker = _poseTracker.LeftHandVelocity;
            }

            if (IsTracked && velocityTracker != null)
            {
                CurrentVelocity = velocityTracker.CurrentVelocity;
                CurrentSpeed = velocityTracker.Speed;

                // Update visual position
                transform.position = CurrentPosition;
            }
        }

        private void UpdateVisualIndicator()
        {
            if (visualIndicator == null)
            {
                Debug.LogWarning($"[{handType} Hand] Visual indicator is NULL!");
                return;
            }

            // Show/hide based on tracking
            visualIndicator.SetActive(IsTracked);

            // Log every 120 frames for debugging
            if (Time.frameCount % 120 == 0)
            {
                Debug.Log($"[{handType} Hand Indicator] Active: {visualIndicator.activeSelf} | Tracked: {IsTracked} | Pos: {CurrentPosition} | Renderer: {(_visualRenderer != null ? "OK" : "NULL")}");
            }

            if (IsTracked)
            {
                // Scale visual based on config - make it BIGGER for visibility
                float radius = config != null ? config.handColliderRadius : 0.2f;
                visualIndicator.transform.localScale = Vector3.one * radius * 4f; // 2x bigger for visibility

                // Change material based on speed
                if (_visualRenderer != null && normalMaterial != null && activeMaterial != null)
                {
                    bool isActive = config != null && CurrentSpeed > config.minHitVelocity;
                    _visualRenderer.material = isActive ? activeMaterial : normalMaterial;
                }
            }
        }

        /// <summary>
        /// Check for collision with a rigidbody using distance-based detection
        /// </summary>
        public bool CheckCollisionWith(Rigidbody target)
        {
            if (!IsTracked || config == null || target == null)
            {
                return false;
            }

            // Check cooldown
            if (Time.time - _lastHitTime < config.hitCooldown)
            {
                return false;
            }

            // Calculate distance to target
            float distance = Vector3.Distance(CurrentPosition, target.position);

            // Check if within detection range
            if (distance > config.collisionDetectionRange)
            {
                // Debug: Show when close but not close enough (every 60 frames)
                if (distance < config.collisionDetectionRange * 2f && Time.frameCount % 60 == 0)
                {
                    Debug.LogWarning($"[Collision] TOO FAR - Distance: {distance:F3}m > Range: {config.collisionDetectionRange}m | Hand Z: {CurrentPosition.z:F2} | Ball Z: {target.position.z:F2}");
                }
                return false;
            }

            // Check if hand is moving fast enough
            if (CurrentSpeed < config.minHitVelocity)
            {
                // Debug: Hand close but too slow
                Debug.LogWarning($"[Collision] TOO SLOW - Speed: {CurrentSpeed:F2} m/s < Required: {config.minHitVelocity} m/s | Distance: {distance:F3}m");
                return false;
            }

            // Check if moving toward the target
            Vector3 directionToTarget = (target.position - CurrentPosition).normalized;
            float velocityAlignment = Vector3.Dot(CurrentVelocity.normalized, directionToTarget);

            if (velocityAlignment < 0.3f) // Moving toward target
            {
                // Debug: Hand close and fast but wrong direction
                Debug.LogWarning($"[Collision] WRONG DIRECTION - Alignment: {velocityAlignment:F2} < 0.3 | Speed: {CurrentSpeed:F2} m/s | Distance: {distance:F3}m");
                return false;
            }

            // Collision detected!
            Vector3 contactPoint = CurrentPosition + directionToTarget * config.handColliderRadius;
            OnCollisionDetected?.Invoke(target, contactPoint, CurrentVelocity);

            _lastHitTime = Time.time;

            // Log successful hit
            Debug.Log($"<color=green>[✓ HIT SUCCESS!] Speed: {CurrentSpeed:F2} m/s | Distance: {distance:F3}m | Direction: {velocityAlignment:F2}</color>");

            return true;
        }

        /// <summary>
        /// Manual collision check using sphere overlap
        /// </summary>
        public Collider[] DetectNearbyColliders()
        {
            if (!IsTracked || config == null) return new Collider[0];

            return UnityEngine.Physics.OverlapSphere(
                CurrentPosition,
                config.handColliderRadius,
                LayerMask.GetMask("Default") // Adjust layer mask as needed
            );
        }

        private void OnDrawGizmos()
        {
            if (config == null || !config.showDebugGizmos) return;

            if (!Application.isPlaying || !IsTracked) return;

            // Draw collision detection range
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(CurrentPosition, config.collisionDetectionRange);

            // Draw hand collider
            Gizmos.color = CurrentSpeed > config.minHitVelocity ? Color.red : Color.green;
            Gizmos.DrawWireSphere(CurrentPosition, config.handColliderRadius);

            // Draw velocity
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(CurrentPosition, CurrentVelocity * 0.1f);
        }
    }
}
