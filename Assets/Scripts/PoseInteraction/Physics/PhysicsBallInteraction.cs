using UnityEngine;
using PoseInteraction.Config;

namespace PoseInteraction.Physics
{
    /// <summary>
    /// Handles physics-based ball interaction when hit by virtual hand
    /// Applies realistic forces and manages ball behavior
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsBallInteraction : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private PoseInteractionConfig config;

        [Header("Ball Settings")]
        [SerializeField] private float ballMass = 0.5f;
        [SerializeField] private float drag = 0.5f;
        [SerializeField] private float angularDrag = 0.05f;

        [Header("Hit Effect")]
        [SerializeField] private ParticleSystem hitEffect;
        [SerializeField] private AudioClip hitSound;
        [SerializeField] private AudioSource audioSource;

        // Components
        private Rigidbody _rigidbody;

        // State
        private int _hitCount;
        private float _lastHitTime;
        private Vector3 _lastHitVelocity;

        // Events
        public event System.Action<Vector3, float> OnBallHit;

        private void Awake()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            // Get or add rigidbody
            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null)
            {
                _rigidbody = gameObject.AddComponent<Rigidbody>();
            }

            // Configure rigidbody
            _rigidbody.mass = ballMass;
            _rigidbody.linearDamping = drag;
            _rigidbody.angularDamping = angularDrag;
            _rigidbody.useGravity = true;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // Setup audio
            if (audioSource == null && hitSound != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f; // 3D sound
            }

            if (config == null)
            {
                Debug.LogError("[PhysicsBallInteraction] Config is missing!");
            }
        }

        /// <summary>
        /// Apply hit force from virtual hand collision
        /// </summary>
        public void ApplyHit(Vector3 contactPoint, Vector3 handVelocity)
        {
            if (config == null || _rigidbody == null) return;

            // Calculate force based on hand velocity
            float speed = handVelocity.magnitude;
            Vector3 direction = handVelocity.normalized;

            // Add upward lift for more natural arc
            direction += Vector3.up * config.upwardLiftFactor;
            direction.Normalize();

            // Calculate force magnitude
            float forceMagnitude = Mathf.Min(speed * config.forceMultiplier, config.maxForce);

            // Apply force
            if (config.applyForceAtPoint)
            {
                _rigidbody.AddForceAtPosition(direction * forceMagnitude, contactPoint, ForceMode.Impulse);
            }
            else
            {
                _rigidbody.AddForce(direction * forceMagnitude, ForceMode.Impulse);
            }

            // Add some spin for realism
            Vector3 torque = Vector3.Cross(direction, Vector3.up) * forceMagnitude * 0.1f;
            _rigidbody.AddTorque(torque, ForceMode.Impulse);

            // Update state
            _lastHitTime = Time.time;
            _lastHitVelocity = handVelocity;
            _hitCount++;

            // Trigger effects
            PlayHitEffects(contactPoint, forceMagnitude);

            // Invoke event
            OnBallHit?.Invoke(direction, forceMagnitude);

            Debug.Log($"[Ball] Applied Force: {forceMagnitude:F1}N, Hand Speed: {speed:F2} m/s, Hit Count: {_hitCount}");
        }

        private void PlayHitEffects(Vector3 position, float force)
        {
            // Play particle effect
            if (hitEffect != null)
            {
                if (hitEffect.isPlaying)
                {
                    hitEffect.Stop();
                }
                hitEffect.transform.position = position;
                hitEffect.Play();
            }

            // Play sound
            if (audioSource != null && hitSound != null)
            {
                // Vary pitch based on force
                float pitch = Mathf.Lerp(0.8f, 1.2f, force / (config != null ? config.maxForce : 100f));
                audioSource.pitch = pitch;
                audioSource.PlayOneShot(hitSound);
            }
        }

        /// <summary>
        /// Get current ball velocity
        /// </summary>
        public Vector3 GetVelocity()
        {
            return _rigidbody != null ? _rigidbody.linearVelocity : Vector3.zero;
        }

        /// <summary>
        /// Get ball speed
        /// </summary>
        public float GetSpeed()
        {
            return _rigidbody != null ? _rigidbody.linearVelocity.magnitude : 0f;
        }

        /// <summary>
        /// Reset ball to position and stop movement
        /// </summary>
        public void ResetBall(Vector3 position)
        {
            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                transform.position = position;
            }

            _hitCount = 0;
            _lastHitTime = 0f;
        }

        /// <summary>
        /// Check if ball was recently hit
        /// </summary>
        public bool WasRecentlyHit(float timeWindow = 0.5f)
        {
            return Time.time - _lastHitTime < timeWindow;
        }

        /// <summary>
        /// Get statistics
        /// </summary>
        public int GetHitCount() => _hitCount;
        public float GetLastHitTime() => _lastHitTime;
        public Vector3 GetLastHitVelocity() => _lastHitVelocity;

        private void OnDrawGizmos()
        {
            if (config == null || !config.showDebugGizmos) return;

            if (!Application.isPlaying) return;

            // Draw velocity vector
            if (_rigidbody != null && _rigidbody.linearVelocity.magnitude > 0.1f)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawRay(transform.position, _rigidbody.linearVelocity * 0.5f);
            }

            // Draw last hit direction
            if (WasRecentlyHit(1f))
            {
                Gizmos.color = Color.red;
                Gizmos.DrawRay(transform.position, _lastHitVelocity.normalized * 0.3f);
            }
        }
    }
}
