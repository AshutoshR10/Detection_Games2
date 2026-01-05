using System.Collections;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using UnityEngine;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;

namespace PoseInteraction.Core
{
    /// <summary>
    /// Extended PoseLandmarkerRunner that forwards results to PoseBallGameManager
    /// Use this instead of the default PoseLandmarkerRunner for game integration
    /// </summary>
    public class CustomPoseLandmarkerRunner : PoseLandmarkerRunner
    {
        [Header("Game Integration")]
        [SerializeField] private PoseBallGameManager gameBallManager;
        [SerializeField] private bool autoFindGameManager = true;

        private int _lastImageWidth;
        private int _lastImageHeight;

        protected override IEnumerator Run()
        {
            // Find game manager if not assigned
            if (autoFindGameManager && gameBallManager == null)
            {
                gameBallManager = FindObjectOfType<PoseBallGameManager>();
            }

            // Start the base coroutine
            yield return base.Run();
        }

        /// <summary>
        /// Override to intercept pose results and forward to game manager
        /// </summary>
        protected void OnPoseResultReceived(PoseLandmarkerResult result, int width, int height)
        {
            _lastImageWidth = width;
            _lastImageHeight = height;

            // Forward to game manager
            if (gameBallManager != null && result.poseLandmarks != null && result.poseLandmarks.Count > 0)
            {
                gameBallManager.UpdatePoseDetection(result, width, height);
            }
        }

        /// <summary>
        /// Expose image dimensions
        /// </summary>
        public int GetImageWidth() => _lastImageWidth;
        public int GetImageHeight() => _lastImageHeight;
    }
}
