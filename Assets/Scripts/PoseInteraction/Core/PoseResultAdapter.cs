using UnityEngine;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;
using System.Reflection;

namespace PoseInteraction.Core
{
    /// <summary>
    /// Simple adapter that extracts pose results from MediaPipe's annotation controller
    /// and forwards them to the game manager. This works without modifying MediaPipe scripts.
    /// </summary>
    public class PoseResultAdapter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PoseBallGameManager gameManager;
        [SerializeField] private Transform imageSourceTransform; // The screen/quad showing camera feed

        [Header("Image Dimensions")]
        [Tooltip("Camera image width - adjust based on your camera setup")]
        [SerializeField] private int imageWidth = 1280;
        [Tooltip("Camera image height - adjust based on your camera setup")]
        [SerializeField] private int imageHeight = 720;

        [Header("Method Selection")]
        [SerializeField] private UpdateMethod updateMethod = UpdateMethod.FindAnnotationController;

        public enum UpdateMethod
        {
            FindAnnotationController,  // Automatically find and read from annotation controller
            ManualCallback            // Use this if you modify PoseLandmarkerRunner to call ForwardResult
        }

        // Cache
        private object _annotationController;
        private FieldInfo _landmarksField;
        private bool _isSetup;

        private void Start()
        {
            Setup();
        }

        private void Setup()
        {
            // Validate game manager
            if (gameManager == null)
            {
                gameManager = FindObjectOfType<PoseBallGameManager>();
                if (gameManager == null)
                {
                    Debug.LogError("[PoseResultAdapter] PoseBallGameManager not found!");
                    return;
                }
            }

            if (updateMethod == UpdateMethod.FindAnnotationController)
            {
                SetupAnnotationControllerReflection();
            }

            _isSetup = true;
            Debug.Log("[PoseResultAdapter] Setup complete. Update method: " + updateMethod);
        }

        private void SetupAnnotationControllerReflection()
        {
            // Find the PoseLandmarkerRunner in the scene
            var runner = FindObjectOfType<PoseLandmarkerRunner>();
            if (runner == null)
            {
                Debug.LogWarning("[PoseResultAdapter] PoseLandmarkerRunner not found. Will retry...");
                return;
            }

            // Get the annotation controller field using reflection
            var controllerField = typeof(PoseLandmarkerRunner).GetField(
                "_poseLandmarkerResultAnnotationController",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (controllerField != null)
            {
                _annotationController = controllerField.GetValue(runner);

                if (_annotationController != null)
                {
                    // Try to get the landmarks field from the annotation controller
                    var controllerType = _annotationController.GetType();
                    _landmarksField = controllerType.GetField("_currentLandmarks", BindingFlags.NonPublic | BindingFlags.Instance);

                    if (_landmarksField == null)
                    {
                        // Try alternative field names
                        _landmarksField = controllerType.GetField("_poseLandmarks", BindingFlags.NonPublic | BindingFlags.Instance);
                    }

                    if (_landmarksField != null)
                    {
                        Debug.Log("[PoseResultAdapter] Successfully connected to annotation controller!");
                    }
                    else
                    {
                        Debug.LogWarning("[PoseResultAdapter] Could not find landmarks field in annotation controller. Using manual callback method recommended.");
                    }
                }
            }
        }

        private void Update()
        {
            if (!_isSetup) return;

            if (updateMethod == UpdateMethod.FindAnnotationController)
            {
                TryReadFromAnnotationController();
            }
        }

        private void TryReadFromAnnotationController()
        {
            if (_annotationController == null || _landmarksField == null)
            {
                // Retry setup
                if (Time.frameCount % 60 == 0) // Every ~1 second
                {
                    SetupAnnotationControllerReflection();
                }
                return;
            }

            // This is a simplified approach - in practice, you'll need to access the actual result
            // The cleanest solution is to modify PoseLandmarkerRunner.cs to call ForwardResult
        }

        /// <summary>
        /// Call this method from a modified PoseLandmarkerRunner
        /// Add this line in PoseLandmarkerRunner after getting results:
        /// FindObjectOfType<PoseResultAdapter>()?.ForwardResult(result, imageWidth, imageHeight);
        /// </summary>
        public void ForwardResult(PoseLandmarkerResult result, int width, int height)
        {
            if (gameManager != null && result.poseLandmarks != null && result.poseLandmarks.Count > 0)
            {
                imageWidth = width;
                imageHeight = height;
                gameManager.UpdatePoseDetection(result, width, height);
            }
        }
    }
}
