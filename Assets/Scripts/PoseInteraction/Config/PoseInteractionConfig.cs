using UnityEngine;

namespace PoseInteraction.Config
{
    /// <summary>
    /// Configuration settings for pose-based ball interaction
    /// Create via: Assets > Create > Pose Interaction > Config
    /// </summary>
    [CreateAssetMenu(fileName = "PoseInteractionConfig", menuName = "Pose Interaction/Config", order = 1)]
    public class PoseInteractionConfig : ScriptableObject
    {
        [Header("Tracking Settings")]
        [Tooltip("Enable right hand tracking")]
        public bool trackRightHand = true;

        [Tooltip("Enable left hand tracking")]
        public bool trackLeftHand = false;

        [Tooltip("Mirror the camera feed (useful for selfie mode)")]
        public bool mirrorMode = true;

        [Header("Coordinate Mapping")]
        [Tooltip("Base distance from camera in meters")]
        [Range(0.5f, 20f)]
        public float baseDepthOffset = 2f;

        [Tooltip("Scale factor for Z depth perception")]
        [Range(0f, 20f)]
        public float depthScale = 5f;

        [Tooltip("Minimum landmark confidence (checks BOTH presence and visibility, accepts if EITHER is above threshold)")]
        [Range(0f, 1f)]
        public float minLandmarkVisibility = 0.3f;

        [Header("Smoothing")]
        [Tooltip("Enable position smoothing to reduce jitter")]
        public bool useSmoothing = true;

        [Tooltip("Smoothing factor (lower = smoother, higher = more responsive)")]
        [Range(0.01f, 1f)]
        public float smoothingFactor = 0.3f;

        [Tooltip("Number of velocity samples to average")]
        [Range(2, 10)]
        public int velocitySampleSize = 5;

        [Header("Collision Detection")]
        [Tooltip("Radius of virtual hand collider")]
        [Range(0.01f, 0.5f)]
        public float handColliderRadius = 0.08f;

        [Tooltip("Minimum hand speed to trigger collision (m/s)")]
        [Range(0f, 5f)]
        public float minHitVelocity = 0.5f;

        [Tooltip("Maximum distance to detect ball collision")]
        [Range(0.1f, 1f)]
        public float collisionDetectionRange = 0.3f;

        [Header("Physics Force")]
        [Tooltip("Force multiplier when hitting the ball")]
        [Range(1f, 100f)]
        public float forceMultiplier = 20f;

        [Tooltip("Maximum force that can be applied")]
        [Range(10f, 500f)]
        public float maxForce = 150f;

        [Tooltip("Apply force at contact point (true) or center (false)")]
        public bool applyForceAtPoint = true;

        [Tooltip("Add upward lift to hits")]
        [Range(0f, 2f)]
        public float upwardLiftFactor = 0.2f;

        [Header("Cooldown")]
        [Tooltip("Time between hits to prevent spam (seconds)")]
        [Range(0f, 1f)]
        public float hitCooldown = 0.1f;

        [Header("Debug")]
        [Tooltip("Show debug visualization in Scene view")]
        public bool showDebugGizmos = true;

        [Tooltip("Show debug information in Console")]
        public bool verboseLogging = false;

        /// <summary>
        /// Validate and clamp values
        /// </summary>
        private void OnValidate()
        {
            baseDepthOffset = Mathf.Max(0.5f, baseDepthOffset);
            depthScale = Mathf.Max(1f, depthScale);
            minLandmarkVisibility = Mathf.Clamp01(minLandmarkVisibility);
            smoothingFactor = Mathf.Clamp(smoothingFactor, 0.01f, 1f);
            velocitySampleSize = Mathf.Max(2, velocitySampleSize);
            handColliderRadius = Mathf.Max(0.01f, handColliderRadius);
            forceMultiplier = Mathf.Max(1f, forceMultiplier);
            maxForce = Mathf.Max(10f, maxForce);
        }
    }
}
