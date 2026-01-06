using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;

namespace PoseInteraction.UI
{
    /// <summary>
    /// Visualizes real-time body part detection status from MediaPipe pose landmarks.
    /// Provides visual feedback (color-coded sprites) and text notifications for missing body parts.
    /// Supports mirror mode for front-facing camera scenarios.
    ///
    /// Architecture:
    /// - Monitors MediaPipe landmarks from PoseBallGameManager
    /// - Maps 33 pose landmarks to 6 simplified body parts
    /// - Updates UI sprites based on detection confidence
    /// - Generates user-friendly feedback messages
    /// </summary>
    public class BodyPartVisualizer : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Body Part Sprite References")]
        [Tooltip("UI Image component displaying the head sprite")]
        [SerializeField] private Image headImage;

        [Tooltip("UI Image component displaying the torso sprite")]
        [SerializeField] private Image torsoImage;

        [Tooltip("UI Image component displaying the left arm sprite")]
        [SerializeField] private Image leftArmImage;

        [Tooltip("UI Image component displaying the right arm sprite")]
        [SerializeField] private Image rightArmImage;

        [Tooltip("UI Image component displaying the left leg sprite")]
        [SerializeField] private Image leftLegImage;

        [Tooltip("UI Image component displaying the right leg sprite")]
        [SerializeField] private Image rightLegImage;

        [Header("Feedback Configuration")]
        [Tooltip("TextMeshProUGUI component for displaying missing body part messages")]
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Tooltip("Color applied to detected body parts")]
        [SerializeField] private Color detectedColor = new Color(0f, 1f, 0f, 1f); // Green

        [Tooltip("Color applied to non-detected body parts")]
        [SerializeField] private Color notDetectedColor = new Color(1f, 0f, 0f, 1f); // Red

        [Tooltip("Minimum visibility score (0-1) required to consider a landmark as detected")]
        [Range(0f, 1f)]
        [SerializeField] private float visibilityThreshold = 0.5f;

        [Header("Mirror Mode Settings")]
        [Tooltip("Enable for front-facing camera to swap left/right detection")]
        [SerializeField] private bool mirrorMode = true;

        [Header("Component References")]
        [Tooltip("Reference to the main pose game manager (auto-detected if not assigned)")]
        [SerializeField] private Core.PoseBallGameManager gameManager;

        #endregion

        #region Private Fields

        // Detection status cache for each body part
        private readonly Dictionary<BodyPart, bool> _bodyPartDetectionStatus = new Dictionary<BodyPart, bool>();

        // Validation flag
        private bool _isInitialized = false;

        #endregion

        #region Enums

        /// <summary>
        /// Simplified body part enumeration for visualization
        /// </summary>
        private enum BodyPart
        {
            Head,
            Torso,
            LeftArm,
            RightArm,
            LeftLeg,
            RightLeg
        }

        #endregion

        #region MediaPipe Landmark Constants

        // MediaPipe Pose Landmark indices (33 total landmarks)
        // Reference: https://developers.google.com/mediapipe/solutions/vision/pose_landmarker

        // Head landmarks
        private const int NOSE = 0;
        private const int LEFT_EYE_INNER = 1;
        private const int LEFT_EYE = 2;
        private const int LEFT_EYE_OUTER = 3;
        private const int RIGHT_EYE_INNER = 4;
        private const int RIGHT_EYE = 5;
        private const int RIGHT_EYE_OUTER = 6;
        private const int LEFT_EAR = 7;
        private const int RIGHT_EAR = 8;

        // Upper body landmarks
        private const int LEFT_SHOULDER = 11;
        private const int RIGHT_SHOULDER = 12;
        private const int LEFT_ELBOW = 13;
        private const int RIGHT_ELBOW = 14;
        private const int LEFT_WRIST = 15;
        private const int RIGHT_WRIST = 16;

        // Core landmarks
        private const int LEFT_HIP = 23;
        private const int RIGHT_HIP = 24;

        // Lower body landmarks
        private const int LEFT_KNEE = 25;
        private const int RIGHT_KNEE = 26;
        private const int LEFT_ANKLE = 27;
        private const int RIGHT_ANKLE = 28;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            InitializeComponent();
        }

        private void Update()
        {
            if (!_isInitialized)
                return;

            UpdateBodyPartDetection();
            UpdateVisualization();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the component and validates all required references
        /// </summary>
        private void InitializeComponent()
        {
            // Auto-find game manager if not assigned
            if (gameManager == null)
            {
                gameManager = FindObjectOfType<Core.PoseBallGameManager>();

                if (gameManager == null)
                {
                    Debug.LogError("[BodyPartVisualizer] PoseBallGameManager not found in scene. Component disabled.");
                    enabled = false;
                    return;
                }
            }

            // Validate UI references
            if (!ValidateReferences())
            {
                Debug.LogError("[BodyPartVisualizer] Missing required UI references. Please check Inspector assignments.");
                enabled = false;
                return;
            }

            // Initialize detection status dictionary
            foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart)))
            {
                _bodyPartDetectionStatus[part] = false;
            }

            _isInitialized = true;

            // Initial visualization update
            UpdateVisualization();

            Debug.Log("[BodyPartVisualizer] Initialized successfully");
        }

        /// <summary>
        /// Validates that all required UI component references are assigned
        /// </summary>
        private bool ValidateReferences()
        {
            bool isValid = true;

            if (headImage == null)
            {
                Debug.LogWarning("[BodyPartVisualizer] Head Image is not assigned");
                isValid = false;
            }

            if (torsoImage == null)
            {
                Debug.LogWarning("[BodyPartVisualizer] Torso Image is not assigned");
                isValid = false;
            }

            if (leftArmImage == null)
            {
                Debug.LogWarning("[BodyPartVisualizer] Left Arm Image is not assigned");
                isValid = false;
            }

            if (rightArmImage == null)
            {
                Debug.LogWarning("[BodyPartVisualizer] Right Arm Image is not assigned");
                isValid = false;
            }

            if (leftLegImage == null)
            {
                Debug.LogWarning("[BodyPartVisualizer] Left Leg Image is not assigned");
                isValid = false;
            }

            if (rightLegImage == null)
            {
                Debug.LogWarning("[BodyPartVisualizer] Right Leg Image is not assigned");
                isValid = false;
            }

            if (feedbackText == null)
            {
                Debug.LogWarning("[BodyPartVisualizer] Feedback Text is not assigned");
                // Non-critical, don't set isValid to false
            }

            return isValid;
        }

        #endregion

        #region Detection Logic

        /// <summary>
        /// Updates detection status for all body parts based on current landmarks
        /// </summary>
        private void UpdateBodyPartDetection()
        {
            // Validate game manager and tracker
            if (gameManager == null || gameManager.HandPoseTracker == null)
            {
                MarkAllPartsAsNotDetected();
                return;
            }

            // Get current landmarks
            var landmarks = gameManager.HandPoseTracker.CurrentLandmarks;

            // No landmarks available
            if (landmarks == null || landmarks.Count == 0)
            {
                MarkAllPartsAsNotDetected();
                return;
            }

            // Detect each body part
            DetectHeadPart(landmarks);
            DetectTorsoPart(landmarks);
            DetectArmParts(landmarks);
            DetectLegParts(landmarks);
        }

        /// <summary>
        /// Detects head visibility based on facial landmarks
        /// </summary>
        private void DetectHeadPart(IReadOnlyList<NormalizedLandmark> landmarks)
        {
            _bodyPartDetectionStatus[BodyPart.Head] = IsBodyPartDetected(
                landmarks,
                NOSE,
                LEFT_EYE,
                RIGHT_EYE
            );
        }

        /// <summary>
        /// Detects torso visibility based on shoulder and hip landmarks
        /// </summary>
        private void DetectTorsoPart(IReadOnlyList<NormalizedLandmark> landmarks)
        {
            _bodyPartDetectionStatus[BodyPart.Torso] = IsBodyPartDetected(
                landmarks,
                LEFT_SHOULDER,
                RIGHT_SHOULDER,
                LEFT_HIP,
                RIGHT_HIP
            );
        }

        /// <summary>
        /// Detects arm visibility with mirror mode support
        /// </summary>
        private void DetectArmParts(IReadOnlyList<NormalizedLandmark> landmarks)
        {
            if (mirrorMode)
            {
                // Mirror mode: User's physical left appears on screen right
                // So we swap the landmark mapping
                _bodyPartDetectionStatus[BodyPart.LeftArm] = IsBodyPartDetected(
                    landmarks,
                    RIGHT_SHOULDER,
                    RIGHT_ELBOW,
                    RIGHT_WRIST
                );

                _bodyPartDetectionStatus[BodyPart.RightArm] = IsBodyPartDetected(
                    landmarks,
                    LEFT_SHOULDER,
                    LEFT_ELBOW,
                    LEFT_WRIST
                );
            }
            else
            {
                // Normal mode: Direct mapping
                _bodyPartDetectionStatus[BodyPart.LeftArm] = IsBodyPartDetected(
                    landmarks,
                    LEFT_SHOULDER,
                    LEFT_ELBOW,
                    LEFT_WRIST
                );

                _bodyPartDetectionStatus[BodyPart.RightArm] = IsBodyPartDetected(
                    landmarks,
                    RIGHT_SHOULDER,
                    RIGHT_ELBOW,
                    RIGHT_WRIST
                );
            }
        }

        /// <summary>
        /// Detects leg visibility with mirror mode support
        /// </summary>
        private void DetectLegParts(IReadOnlyList<NormalizedLandmark> landmarks)
        {
            if (mirrorMode)
            {
                // Mirror mode: Swap landmark mapping
                _bodyPartDetectionStatus[BodyPart.LeftLeg] = IsBodyPartDetected(
                    landmarks,
                    RIGHT_HIP,
                    RIGHT_KNEE,
                    RIGHT_ANKLE
                );

                _bodyPartDetectionStatus[BodyPart.RightLeg] = IsBodyPartDetected(
                    landmarks,
                    LEFT_HIP,
                    LEFT_KNEE,
                    LEFT_ANKLE
                );
            }
            else
            {
                // Normal mode: Direct mapping
                _bodyPartDetectionStatus[BodyPart.LeftLeg] = IsBodyPartDetected(
                    landmarks,
                    LEFT_HIP,
                    LEFT_KNEE,
                    LEFT_ANKLE
                );

                _bodyPartDetectionStatus[BodyPart.RightLeg] = IsBodyPartDetected(
                    landmarks,
                    RIGHT_HIP,
                    RIGHT_KNEE,
                    RIGHT_ANKLE
                );
            }
        }

        /// <summary>
        /// Determines if a body part is detected based on visibility of constituent landmarks
        /// A body part is considered detected if at least 50% of its landmarks exceed the visibility threshold
        /// </summary>
        private bool IsBodyPartDetected(IReadOnlyList<NormalizedLandmark> landmarks, params int[] landmarkIndices)
        {
            if (landmarks == null || landmarks.Count == 0)
                return false;

            int visibleCount = 0;
            int validLandmarkCount = 0;

            foreach (int index in landmarkIndices)
            {
                // Validate landmark index
                if (index < 0 || index >= landmarks.Count)
                    continue;

                validLandmarkCount++;

                var landmark = landmarks[index];

                // Check if landmark visibility exceeds threshold
                if (landmark.visibility > visibilityThreshold)
                {
                    visibleCount++;
                }
            }

            // Require at least 50% of landmarks to be visible
            if (validLandmarkCount == 0)
                return false;

            float visibilityRatio = (float)visibleCount / validLandmarkCount;
            return visibilityRatio >= 0.5f;
        }

        /// <summary>
        /// Marks all body parts as not detected (fallback state)
        /// </summary>
        private void MarkAllPartsAsNotDetected()
        {
            foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart)))
            {
                _bodyPartDetectionStatus[part] = false;
            }
        }

        #endregion

        #region Visualization

        /// <summary>
        /// Updates all visual elements based on current detection status
        /// </summary>
        private void UpdateVisualization()
        {
            UpdateBodyPartColors();
            UpdateFeedbackText();
        }

        /// <summary>
        /// Updates sprite colors for all body parts
        /// </summary>
        private void UpdateBodyPartColors()
        {
            SetImageColor(headImage, _bodyPartDetectionStatus[BodyPart.Head]);
            SetImageColor(torsoImage, _bodyPartDetectionStatus[BodyPart.Torso]);
            SetImageColor(leftArmImage, _bodyPartDetectionStatus[BodyPart.LeftArm]);
            SetImageColor(rightArmImage, _bodyPartDetectionStatus[BodyPart.RightArm]);
            SetImageColor(leftLegImage, _bodyPartDetectionStatus[BodyPart.LeftLeg]);
            SetImageColor(rightLegImage, _bodyPartDetectionStatus[BodyPart.RightLeg]);
        }

        /// <summary>
        /// Sets the color of an image based on detection status
        /// </summary>
        private void SetImageColor(Image image, bool isDetected)
        {
            if (image != null)
            {
                image.color = isDetected ? detectedColor : notDetectedColor;
            }
        }

        /// <summary>
        /// Updates the feedback text with information about missing body parts
        /// </summary>
        private void UpdateFeedbackText()
        {
            if (feedbackText == null)
                return;

            List<string> missingParts = GetMissingBodyParts();

            feedbackText.text = GenerateFeedbackMessage(missingParts);
        }

        /// <summary>
        /// Retrieves a list of body parts that are not currently detected
        /// </summary>
        private List<string> GetMissingBodyParts()
        {
            List<string> missingParts = new List<string>();

            if (!_bodyPartDetectionStatus[BodyPart.Head])
                missingParts.Add("head");

            if (!_bodyPartDetectionStatus[BodyPart.Torso])
                missingParts.Add("body");

            if (!_bodyPartDetectionStatus[BodyPart.LeftArm])
                missingParts.Add("left arm");

            if (!_bodyPartDetectionStatus[BodyPart.RightArm])
                missingParts.Add("right arm");

            if (!_bodyPartDetectionStatus[BodyPart.LeftLeg])
                missingParts.Add("left leg");

            if (!_bodyPartDetectionStatus[BodyPart.RightLeg])
                missingParts.Add("right leg");

            return missingParts;
        }

        /// <summary>
        /// Generates a user-friendly feedback message based on missing parts
        /// </summary>
        private string GenerateFeedbackMessage(List<string> missingParts)
        {
            if (missingParts.Count == 0)
            {
                return "All body parts detected!";
            }

            if (missingParts.Count == 6)
            {
                return "I can't see you!\nPlease stand in front of the camera.";
            }

            // Build message with bullet points
            System.Text.StringBuilder message = new System.Text.StringBuilder();
            message.AppendLine("I can't see your:");

            foreach (string part in missingParts)
            {
                message.AppendLine($"• {part}");
            }

            return message.ToString().TrimEnd();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Enables or disables mirror mode at runtime
        /// </summary>
        public void SetMirrorMode(bool enabled)
        {
            mirrorMode = enabled;
        }

        /// <summary>
        /// Checks if a specific body part is currently detected
        /// </summary>
        /// <param name="partName">Name of the body part (case-insensitive)</param>
        /// <returns>True if detected, false otherwise</returns>
        public bool IsPartDetected(string partName)
        {
            if (System.Enum.TryParse<BodyPart>(partName, true, out BodyPart part))
            {
                return _bodyPartDetectionStatus.ContainsKey(part) && _bodyPartDetectionStatus[part];
            }

            Debug.LogWarning($"[BodyPartVisualizer] Invalid body part name: {partName}");
            return false;
        }

        /// <summary>
        /// Sets custom colors for detection states
        /// </summary>
        public void SetColors(Color detected, Color notDetected)
        {
            detectedColor = detected;
            notDetectedColor = notDetected;
        }

        /// <summary>
        /// Sets the visibility threshold for landmark detection
        /// </summary>
        public void SetVisibilityThreshold(float threshold)
        {
            visibilityThreshold = Mathf.Clamp01(threshold);
        }

        #endregion
    }
}
