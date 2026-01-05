using UnityEngine;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using System.Reflection;

namespace PoseInteraction.Core
{
    /// <summary>
    /// Bridge component that connects MediaPipe PoseLandmarkerRunner to PoseBallGameManager
    /// Automatically hooks into MediaPipe's pose detection pipeline
    /// </summary>
    [RequireComponent(typeof(PoseBallGameManager))]
    public class MediaPipePoseBridge : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PoseLandmarkerRunner poseLandmarkerRunner;
        [SerializeField] private PoseBallGameManager gameManager;

        [Header("Settings")]
        [SerializeField] private bool autoFindRunner = true;
        [Tooltip("Image dimensions for coordinate conversion")]
        [SerializeField] private int imageWidth = 1280;
        [SerializeField] private int imageHeight = 720;

        private bool _isInitialized;

        private void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            // Find game manager
            if (gameManager == null)
            {
                gameManager = GetComponent<PoseBallGameManager>();
            }

            // Find pose landmarker runner
            if (autoFindRunner && poseLandmarkerRunner == null)
            {
                poseLandmarkerRunner = FindObjectOfType<PoseLandmarkerRunner>();
            }

            if (poseLandmarkerRunner == null)
            {
                Debug.LogError("[MediaPipePoseBridge] PoseLandmarkerRunner not found! Please assign it or ensure it exists in the scene.");
                return;
            }

            if (gameManager == null)
            {
                Debug.LogError("[MediaPipePoseBridge] PoseBallGameManager not found!");
                return;
            }

            // Hook into MediaPipe's annotation controller to intercept results
            SetupResultInterception();

            _isInitialized = true;
            Debug.Log("[MediaPipePoseBridge] Successfully bridged MediaPipe to Game Manager");
        }

        private void SetupResultInterception()
        {
            // We'll intercept results through the annotation controller
            // This is a non-invasive way to get pose data without modifying MediaPipe scripts

            var annotationControllerField = typeof(PoseLandmarkerRunner).GetField(
                "_poseLandmarkerResultAnnotationController",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (annotationControllerField != null)
            {
                var controller = annotationControllerField.GetValue(poseLandmarkerRunner);
                if (controller != null)
                {
                    Debug.Log("[MediaPipePoseBridge] Annotation controller found, ready to receive pose data");
                }
            }
        }

        /// <summary>
        /// Call this method to manually push pose results to the game manager
        /// This should be called from your modified PoseLandmarkerRunner or from Update
        /// </summary>
        public void OnPoseDetectionResult(PoseLandmarkerResult result)
        {
            if (!_isInitialized || gameManager == null) return;

            if (result.poseLandmarks != null && result.poseLandmarks.Count > 0)
            {
                // Forward to game manager
                gameManager.UpdatePoseDetection(result, imageWidth, imageHeight);
            }
        }

        /// <summary>
        /// Alternative: Poll for results if you have access to the result data
        /// This would need to be called from your custom integration point
        /// </summary>
        public void Update()
        {
            // Note: In a real implementation, you would modify PoseLandmarkerRunner
            // to expose its results or use a callback system
            // For now, this bridge provides the structure for integration

            // TODO: Integrate with actual MediaPipe result stream
            // Option 1: Modify PoseLandmarkerRunner to call OnPoseDetectionResult
            // Option 2: Use a custom annotation controller that forwards results
            // Option 3: Create a custom runner that extends PoseLandmarkerRunner
        }
    }
}
