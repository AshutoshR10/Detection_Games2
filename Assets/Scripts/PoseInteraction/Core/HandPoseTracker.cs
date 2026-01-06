using UnityEngine;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using PoseInteraction.Config;
using PoseInteraction.Utilities;

namespace PoseInteraction.Core
{
    /// <summary>
    /// Tracks hand positions from MediaPipe Pose landmarks (using INDEX finger for better accuracy)
    /// Provides real-time world position and velocity data
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class HandPoseTracker : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private PoseInteractionConfig config;

        [Header("References")]
        [SerializeField] private Camera trackingCamera;

        // MediaPipe Pose Landmark Indices
        // Using INDEX finger (19/20) instead of WRIST (15/16) for more accurate hand center tracking
        private const int RIGHT_INDEX = 20;
        private const int LEFT_INDEX = 19;
        private const int RIGHT_WRIST = 16;
        private const int LEFT_WRIST = 15;
        private const int RIGHT_ELBOW = 14;
        private const int LEFT_ELBOW = 13;
        private const int RIGHT_SHOULDER = 12;
        private const int LEFT_SHOULDER = 11;

        // In mirror mode, we MUST swap landmark indices because:
        // - Physical RIGHT hand → MediaPipe RIGHT_INDEX → ToWorldPositionMirrored flips to LEFT side
        // - So we read LEFT_INDEX to get data that will flip to RIGHT side
        // This ensures indicators match physical hands!
        private int GetRightHandIndex() => config != null && config.mirrorMode ? LEFT_INDEX : RIGHT_INDEX;
        private int GetLeftHandIndex() => config != null && config.mirrorMode ? RIGHT_INDEX : LEFT_INDEX;
        private int GetRightElbowIndex() => config != null && config.mirrorMode ? LEFT_ELBOW : RIGHT_ELBOW;
        private int GetLeftElbowIndex() => config != null && config.mirrorMode ? RIGHT_ELBOW : LEFT_ELBOW;
        private int GetRightShoulderIndex() => config != null && config.mirrorMode ? LEFT_SHOULDER : RIGHT_SHOULDER;
        private int GetLeftShoulderIndex() => config != null && config.mirrorMode ? RIGHT_SHOULDER : LEFT_SHOULDER;

        // Public accessors for hand data
        public Vector3 RightHandPosition { get; private set; }
        public Vector3 LeftHandPosition { get; private set; }
        public Vector3 RightElbowPosition { get; private set; }
        public Vector3 LeftElbowPosition { get; private set; }
        public Vector3 RightShoulderPosition { get; private set; }
        public Vector3 LeftShoulderPosition { get; private set; }

        // Tracking status
        public bool IsRightHandTracked { get; private set; }
        public bool IsLeftHandTracked { get; private set; }

        // Velocity trackers
        private HandVelocityTracker _rightHandVelocity;
        private HandVelocityTracker _leftHandVelocity;

        public HandVelocityTracker RightHandVelocity => _rightHandVelocity;
        public HandVelocityTracker LeftHandVelocity => _leftHandVelocity;

        // Smoothed positions
        private Vector3 _smoothedRightHand;
        private Vector3 _smoothedLeftHand;

        // Image dimensions (from MediaPipe)
        private int _imageWidth;
        private int _imageHeight;

        // Current landmarks (for external access)
        private System.Collections.Generic.List<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> _currentLandmarks;
        public System.Collections.Generic.IReadOnlyList<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> CurrentLandmarks => _currentLandmarks;

        // Debug
        private bool _firstFrame = true;

        private void Awake()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            // Get or assign camera
            if (trackingCamera == null)
            {
                trackingCamera = GetComponent<Camera>();
                if (trackingCamera == null)
                {
                    trackingCamera = Camera.main;
                }
            }

            // Validate config
            if (config == null)
            {
                Debug.LogError("[HandPoseTracker] Config is missing! Please assign a PoseInteractionConfig.");
            }

            // Initialize velocity trackers
            int sampleSize = config != null ? config.velocitySampleSize : 5;
            _rightHandVelocity = new HandVelocityTracker(sampleSize);
            _leftHandVelocity = new HandVelocityTracker(sampleSize);

            // Initialize smoothed positions
            _smoothedRightHand = Vector3.zero;
            _smoothedLeftHand = Vector3.zero;
        }

        /// <summary>
        /// Main update method - call this with MediaPipe results
        /// </summary>
        public void UpdatePoseLandmarks(PoseLandmarkerResult result, int imageWidth, int imageHeight)
        {
            if (config == null) return;

            _imageWidth = imageWidth;
            _imageHeight = imageHeight;

            // Reset tracking flags
            IsRightHandTracked = false;
            IsLeftHandTracked = false;

            // Validate result
            if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
            {
                _currentLandmarks = null;
                return;
            }

            // Get first person's landmarks
            var normalizedLandmarks = result.poseLandmarks[0];

            // NormalizedLandmarks is a protobuf struct - check for public FIELDS (not properties)
            var type = normalizedLandmarks.GetType();
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            // Only log once, not every frame (removed excessive logging)

            System.Collections.IEnumerable landmarkCollection = null;

            // Look for a field that is IEnumerable
            foreach (var field in fields)
            {
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType) &&
                    field.FieldType != typeof(string))
                {
                    landmarkCollection = field.GetValue(normalizedLandmarks) as System.Collections.IEnumerable;
                    if (landmarkCollection != null)
                    {
                        break;
                    }
                }
            }

            if (landmarkCollection == null)
            {
                Debug.LogError("[HandPoseTracker] Could not find landmark collection in NormalizedLandmarks");
                Debug.LogError($"[HandPoseTracker] Checked {fields.Length} public fields");
                return;
            }

            // Convert to list
            var landmarkList = new System.Collections.Generic.List<Mediapipe.Tasks.Components.Containers.NormalizedLandmark>();
            foreach (var item in landmarkCollection)
            {
                if (item is Mediapipe.Tasks.Components.Containers.NormalizedLandmark landmark)
                {
                    landmarkList.Add(landmark);
                }
            }

            // Store current landmarks for external access (e.g., BodyPartVisualizer)
            _currentLandmarks = landmarkList;

            // Track right hand
            if (config.trackRightHand && landmarkList.Count > GetRightHandIndex())
            {
                UpdateRightHand(landmarkList);
            }

            // Track left hand
            if (config.trackLeftHand && landmarkList.Count > GetLeftHandIndex())
            {
                UpdateLeftHand(landmarkList);
            }

            // First frame setup done
            if (_firstFrame)
            {
                _firstFrame = false;
            }
        }

        private void UpdateRightHand(System.Collections.Generic.IList<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> landmarks)
        {
            var handLandmark = landmarks[GetRightHandIndex()];

            // Check confidence (accept if EITHER presence OR visibility is above threshold)
            if (!LandmarkToWorldConverter.IsLandmarkValid(handLandmark, config.minLandmarkVisibility))
            {
                IsRightHandTracked = false;
                return;
            }

            // Convert to world position
            Vector3 worldPos = config.mirrorMode
                ? LandmarkToWorldConverter.ToWorldPositionMirrored(handLandmark, trackingCamera, config.baseDepthOffset, config.depthScale)
                : LandmarkToWorldConverter.ToWorldPosition(handLandmark, trackingCamera, config.baseDepthOffset, config.depthScale);

            // Apply smoothing
            if (config.useSmoothing)
            {
                _smoothedRightHand = Vector3.Lerp(_smoothedRightHand, worldPos, config.smoothingFactor);
                worldPos = _smoothedRightHand;
            }

            RightHandPosition = worldPos;

            // Update velocity
            _rightHandVelocity.UpdatePosition(RightHandPosition, Time.time);

            // Track additional landmarks
            RightElbowPosition = config.mirrorMode
                ? LandmarkToWorldConverter.ToWorldPositionMirrored(landmarks[GetRightElbowIndex()], trackingCamera, config.baseDepthOffset, config.depthScale)
                : LandmarkToWorldConverter.ToWorldPosition(landmarks[GetRightElbowIndex()], trackingCamera, config.baseDepthOffset, config.depthScale);

            RightShoulderPosition = config.mirrorMode
                ? LandmarkToWorldConverter.ToWorldPositionMirrored(landmarks[GetRightShoulderIndex()], trackingCamera, config.baseDepthOffset, config.depthScale)
                : LandmarkToWorldConverter.ToWorldPosition(landmarks[GetRightShoulderIndex()], trackingCamera, config.baseDepthOffset, config.depthScale);

            IsRightHandTracked = true;
        }

        private void UpdateLeftHand(System.Collections.Generic.IList<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> landmarks)
        {
            var handLandmark = landmarks[GetLeftHandIndex()];

            // Check confidence (accept if EITHER presence OR visibility is above threshold)
            if (!LandmarkToWorldConverter.IsLandmarkValid(handLandmark, config.minLandmarkVisibility))
            {
                IsLeftHandTracked = false;
                return;
            }

            // Convert to world position
            Vector3 worldPos = config.mirrorMode
                ? LandmarkToWorldConverter.ToWorldPositionMirrored(handLandmark, trackingCamera, config.baseDepthOffset, config.depthScale)
                : LandmarkToWorldConverter.ToWorldPosition(handLandmark, trackingCamera, config.baseDepthOffset, config.depthScale);

            // Apply smoothing
            if (config.useSmoothing)
            {
                _smoothedLeftHand = Vector3.Lerp(_smoothedLeftHand, worldPos, config.smoothingFactor);
                worldPos = _smoothedLeftHand;
            }

            LeftHandPosition = worldPos;

            // Update velocity
            _leftHandVelocity.UpdatePosition(LeftHandPosition, Time.time);

            // Track additional landmarks
            LeftElbowPosition = config.mirrorMode
                ? LandmarkToWorldConverter.ToWorldPositionMirrored(landmarks[GetLeftElbowIndex()], trackingCamera, config.baseDepthOffset, config.depthScale)
                : LandmarkToWorldConverter.ToWorldPosition(landmarks[GetLeftElbowIndex()], trackingCamera, config.baseDepthOffset, config.depthScale);

            LeftShoulderPosition = config.mirrorMode
                ? LandmarkToWorldConverter.ToWorldPositionMirrored(landmarks[GetLeftShoulderIndex()], trackingCamera, config.baseDepthOffset, config.depthScale)
                : LandmarkToWorldConverter.ToWorldPosition(landmarks[GetLeftShoulderIndex()], trackingCamera, config.baseDepthOffset, config.depthScale);

            IsLeftHandTracked = true;
        }

        /// <summary>
        /// Get hand direction vector (elbow to wrist)
        /// </summary>
        public Vector3 GetRightHandDirection()
        {
            if (!IsRightHandTracked) return Vector3.zero;
            return (RightHandPosition - RightElbowPosition).normalized;
        }

        public Vector3 GetLeftHandDirection()
        {
            if (!IsLeftHandTracked) return Vector3.zero;
            return (LeftHandPosition - LeftElbowPosition).normalized;
        }

        /// <summary>
        /// Reset all tracking data
        /// </summary>
        public void ResetTracking()
        {
            _rightHandVelocity?.Reset();
            _leftHandVelocity?.Reset();
            _smoothedRightHand = Vector3.zero;
            _smoothedLeftHand = Vector3.zero;
            IsRightHandTracked = false;
            IsLeftHandTracked = false;
        }

        private void OnDrawGizmos()
        {
            if (config == null || !config.showDebugGizmos || !Application.isPlaying) return;

            // Draw right hand
            if (IsRightHandTracked)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(RightHandPosition, config.handColliderRadius);
                Gizmos.DrawLine(RightElbowPosition, RightHandPosition);
                Gizmos.DrawLine(RightShoulderPosition, RightElbowPosition);

                // Draw velocity vector
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(RightHandPosition, _rightHandVelocity.CurrentVelocity * 0.1f);
            }

            // Draw left hand
            if (IsLeftHandTracked)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawWireSphere(LeftHandPosition, config.handColliderRadius);
                Gizmos.DrawLine(LeftElbowPosition, LeftHandPosition);
                Gizmos.DrawLine(LeftShoulderPosition, LeftElbowPosition);

                // Draw velocity vector
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(LeftHandPosition, _leftHandVelocity.CurrentVelocity * 0.1f);
            }
        }
    }
}
