using UnityEngine;
using Mediapipe.Tasks.Components.Containers;

namespace PoseInteraction.Utilities
{
    /// <summary>
    /// Utility class for converting MediaPipe normalized landmarks to Unity world space
    /// Uses CameraCalibrator for accurate, device-independent coordinate conversion
    /// </summary>
    public static class LandmarkToWorldConverter
    {
        private static CameraCalibrator _calibrator;

        /// <summary>
        /// Set the camera calibrator to use for conversions
        /// </summary>
        public static void SetCalibrator(CameraCalibrator calibrator)
        {
            _calibrator = calibrator;
            bool isCalibrated = calibrator != null && calibrator.IsCalibrated;
            Debug.Log($"[LandmarkToWorldConverter] Calibrator set successfully | IsCalibrated: {isCalibrated} | Depth: {(isCalibrated ? calibrator.CalibratedDepth.ToString("F2") + "m" : "N/A")}");
        }
        /// <summary>
        /// Convert MediaPipe normalized landmark to Unity world position
        /// Uses calibrated conversion for accurate positioning across all devices
        /// </summary>
        /// <param name="landmark">MediaPipe normalized landmark (0-1 range)</param>
        /// <param name="camera">Camera (used for fallback only)</param>
        /// <param name="baseDepth">Base distance from camera (deprecated - uses calibrated depth)</param>
        /// <param name="depthScale">Scale factor for Z depth (deprecated - normalized Z used instead)</param>
        /// <returns>World position in Unity space</returns>
        public static Vector3 ToWorldPosition(
            NormalizedLandmark landmark,
            Camera camera,
            float baseDepth = 2f,
            float depthScale = 5f)
        {
            // Use calibrator if available (recommended)
            if (_calibrator != null && _calibrator.IsCalibrated)
            {
                return _calibrator.NormalizedToWorldPosition(landmark.x, landmark.y, landmark.z);
            }

            // Fallback to old method if calibrator not set
            if (camera == null)
            {
                Debug.LogError("[LandmarkToWorldConverter] Camera is null and no calibrator set!");
                return Vector3.zero;
            }

            Debug.LogWarning("[LandmarkToWorldConverter] Using fallback conversion. Please set calibrator for better accuracy.");

            float viewportX = landmark.x;
            float viewportY = 1f - landmark.y;
            float depth = baseDepth + (landmark.z * depthScale);
            Vector3 viewportPoint = new Vector3(viewportX, viewportY, depth);
            return camera.ViewportToWorldPoint(viewportPoint);
        }

        /// <summary>
        /// Convert with automatic mirroring (useful for selfie camera)
        /// </summary>
        public static Vector3 ToWorldPositionMirrored(
            NormalizedLandmark landmark,
            Camera camera,
            float baseDepth = 2f,
            float depthScale = 5f)
        {
            // Use calibrator if available (recommended)
            if (_calibrator != null && _calibrator.IsCalibrated)
            {
                return _calibrator.NormalizedToWorldPositionMirrored(landmark.x, landmark.y, landmark.z);
            }

            // Fallback to old method if calibrator not set
            if (camera == null)
            {
                Debug.LogError("Camera is null in LandmarkToWorldConverter!");
                return Vector3.zero;
            }

            Debug.LogWarning("[LandmarkToWorldConverter] Using fallback mirrored conversion. Please set calibrator for better accuracy.");

            // Mirror X coordinate directly in viewport calculation
            float viewportX = 1f - landmark.x; // Mirrored
            float viewportY = 1f - landmark.y; // Flip Y axis

            // Calculate depth
            float depth = baseDepth + (landmark.z * depthScale);

            // Create viewport point
            Vector3 viewportPoint = new Vector3(viewportX, viewportY, depth);

            // Convert to world space
            return camera.ViewportToWorldPoint(viewportPoint);
        }

        /// <summary>
        /// Check if landmark has sufficient confidence
        /// For Pose landmarks, check BOTH presence and visibility - accept if EITHER is above threshold
        /// (MediaPipe Pose gives inconsistent scores, sometimes one is high when the other is low)
        /// </summary>
        public static bool IsLandmarkValid(NormalizedLandmark landmark, float minConfidence = 0.3f)
        {
            // Accept if EITHER presence OR visibility is above threshold
            return landmark.presence >= minConfidence || landmark.visibility >= minConfidence;
        }

        /// <summary>
        /// Calculate direction vector between two landmarks in world space
        /// </summary>
        public static Vector3 GetDirectionBetweenLandmarks(
            NormalizedLandmark from,
            NormalizedLandmark to,
            Camera camera,
            float baseDepth = 2f,
            float depthScale = 5f)
        {
            Vector3 fromWorld = ToWorldPosition(from, camera, baseDepth, depthScale);
            Vector3 toWorld = ToWorldPosition(to, camera, baseDepth, depthScale);
            return (toWorld - fromWorld).normalized;
        }

        /// <summary>
        /// Calculate distance between two landmarks in world space
        /// </summary>
        public static float GetDistanceBetweenLandmarks(
            NormalizedLandmark from,
            NormalizedLandmark to,
            Camera camera,
            float baseDepth = 2f,
            float depthScale = 5f)
        {
            Vector3 fromWorld = ToWorldPosition(from, camera, baseDepth, depthScale);
            Vector3 toWorld = ToWorldPosition(to, camera, baseDepth, depthScale);
            return Vector3.Distance(fromWorld, toWorld);
        }
    }
}
