using UnityEngine;

namespace PoseInteraction.Utilities
{
    /// <summary>
    /// Automatically calibrates camera-to-world coordinate conversion for consistent hand tracking
    /// across different devices with varying screen sizes, aspect ratios, and camera FOVs.
    /// This ensures hands and game objects exist in the same coordinate space.
    /// </summary>
    public class CameraCalibrator : MonoBehaviour
    {
        [Header("Calibration Target")]
        [Tooltip("Reference object to calibrate against (e.g., the ball)")]
        [SerializeField] private Transform calibrationTarget;

        [Header("Calibration Settings")]
        [Tooltip("Automatically calibrate on start")]
        [SerializeField] private bool autoCalibrate = true;

        [Tooltip("Show debug visualization")]
        [SerializeField] private bool showDebugInfo = true;

        // Calibrated values
        private float _calibratedDepth;
        private Vector2 _screenCenter;
        private float _worldUnitsPerViewportUnit;
        private bool _isCalibrated = false;

        public float CalibratedDepth => _calibratedDepth;
        public bool IsCalibrated => _isCalibrated;

        private Camera _camera;

        /// <summary>
        /// Set calibration target programmatically
        /// </summary>
        public void SetCalibrationTarget(Transform target)
        {
            calibrationTarget = target;
            Debug.Log($"[CameraCalibrator] Calibration target set to: {target?.name ?? "NULL"}");
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                _camera = Camera.main;
            }
        }

        private void Start()
        {
            if (autoCalibrate)
            {
                CalibrateCamera();
            }
        }

        /// <summary>
        /// Performs automatic camera calibration based on target object position
        /// </summary>
        public void CalibrateCamera()
        {
            if (_camera == null)
            {
                Debug.LogError("[CameraCalibrator] Camera not found!");
                return;
            }

            if (calibrationTarget == null)
            {
                Debug.LogWarning("[CameraCalibrator] No calibration target set. Using default depth.");
                _calibratedDepth = 2.0f;
                _isCalibrated = true;
                return;
            }

            // Calculate depth from camera to target
            Vector3 cameraToTarget = calibrationTarget.position - _camera.transform.position;
            float targetDistance = cameraToTarget.magnitude;

            // Project target onto camera's forward plane to get depth
            _calibratedDepth = Vector3.Dot(cameraToTarget, _camera.transform.forward);

            // Calculate screen center
            _screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            // Calculate world units per viewport unit at target depth
            // This tells us how to scale normalized coordinates to world space
            _worldUnitsPerViewportUnit = CalculateWorldUnitsPerViewportUnit(_calibratedDepth);

            _isCalibrated = true;

            Debug.Log($"[CameraCalibrator] ✅ Calibration Complete!");
            Debug.Log($"  Platform: {Application.platform}");
            Debug.Log($"  Screen: {Screen.width}x{Screen.height} (Aspect: {_camera.aspect:F2})");
            Debug.Log($"  Camera FOV: {_camera.fieldOfView:F1}°");
            Debug.Log($"  Camera Position: {_camera.transform.position}");
            Debug.Log($"  Target Position: {calibrationTarget.position}");
            Debug.Log($"  Target Distance: {targetDistance:F2}m");
            Debug.Log($"  Calibrated Depth: {_calibratedDepth:F2}m");
            Debug.Log($"  Viewport Width at depth: {2.0f * _calibratedDepth * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * _camera.aspect:F2}m");
            Debug.Log($"  World Units/Viewport: {_worldUnitsPerViewportUnit:F2}");
        }

        /// <summary>
        /// Calculate how many world units correspond to one viewport unit at given depth
        /// </summary>
        private float CalculateWorldUnitsPerViewportUnit(float depth)
        {
            // Calculate the height of the viewport at the target depth
            float halfFovRad = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float viewportHeightAtDepth = 2.0f * depth * Mathf.Tan(halfFovRad);

            return viewportHeightAtDepth;
        }

        /// <summary>
        /// Converts normalized landmark (0-1) to world position using calibrated values
        /// This is more accurate than ViewportToWorldPoint for hand tracking
        /// </summary>
        public Vector3 NormalizedToWorldPosition(float normalizedX, float normalizedY, float normalizedZ = 0f)
        {
            if (!_isCalibrated)
            {
                Debug.LogWarning("[CameraCalibrator] Not calibrated yet! Call CalibrateCamera() first.");
                return Vector3.zero;
            }

            // SIMPLIFIED APPROACH: Use Unity's ViewportToWorldPoint directly
            // This ensures the indicator appears exactly where the hand is in the camera view

            // MediaPipe: X: 0 (left) to 1 (right), Y: 0 (top) to 1 (bottom)
            // Unity viewport: X: 0 (left) to 1 (right), Y: 0 (bottom) to 1 (top)

            // Adjust X coordinate toward center to fix "spreading away" offset
            // normalizedX is from MediaPipe (0-1), we need to compress it slightly
            float centeredX = normalizedX - 0.5f; // Range: -0.5 to +0.5
            centeredX *= 0.90f; // Scale down by 5% to bring hands closer to center
            float viewportX = centeredX + 0.5f; // Back to 0-1 range

            float viewportY = 1.0f - normalizedY; // Flip Y axis

            // Use calibrated depth to place indicator at same distance as ball
            Vector3 viewportPoint = new Vector3(viewportX, viewportY, _calibratedDepth);
            Vector3 worldPos = _camera.ViewportToWorldPoint(viewportPoint);

            // Debug logging (every 60 frames)
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"[CameraCalibrator] Conversion: Normalized({normalizedX:F3}, {normalizedY:F3}) → Adjusted X({viewportX:F3}) → World({worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2}) | Depth: {_calibratedDepth:F2}m");
            }

            return worldPos;
        }

        /// <summary>
        /// Converts normalized landmark with mirroring (for front-facing camera)
        /// </summary>
        public Vector3 NormalizedToWorldPositionMirrored(float normalizedX, float normalizedY, float normalizedZ = 0f)
        {
            // Mirror X coordinate for front-facing camera
            float mirroredX = 1.0f - normalizedX;
            return NormalizedToWorldPosition(mirroredX, normalizedY, normalizedZ);
        }

        /// <summary>
        /// Get world position from screen coordinates
        /// </summary>
        public Vector3 ScreenToWorldPosition(Vector2 screenPos, float depth)
        {
            Vector3 screenPoint = new Vector3(screenPos.x, screenPos.y, depth);
            return _camera.ScreenToWorldPoint(screenPoint);
        }

        private void OnGUI()
        {
            if (!showDebugInfo || !_isCalibrated) return;

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 20;
            style.normal.textColor = Color.cyan;

            GUILayout.BeginArea(new Rect(Screen.width - 310, 10, 300, 250));
            GUILayout.BeginVertical("box");

            GUILayout.Label("<b>Camera Calibration</b>", style);
            GUILayout.Label($"Depth: {_calibratedDepth:F2}m", style);
            GUILayout.Label($"FOV: {_camera.fieldOfView:F1}°", style);
            GUILayout.Label($"Aspect: {_camera.aspect:F2}", style);
            GUILayout.Label($"Screen: {Screen.width}x{Screen.height}", style);

            // Show viewport width (how wide is visible area at ball depth)
            float halfFovRad = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float viewportHeight = 2.0f * _calibratedDepth * Mathf.Tan(halfFovRad);
            float viewportWidth = viewportHeight * _camera.aspect;
            GUILayout.Label($"Viewport: {viewportWidth:F1}x{viewportHeight:F1}m", style);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void OnDrawGizmos()
        {
            if (!_isCalibrated || _camera == null) return;

            // Draw calibration plane
            Gizmos.color = Color.cyan;
            Vector3 cameraPos = _camera.transform.position;
            Vector3 planeCenter = cameraPos + _camera.transform.forward * _calibratedDepth;

            // Calculate plane dimensions
            float halfFovRad = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float planeHeight = 2.0f * _calibratedDepth * Mathf.Tan(halfFovRad);
            float planeWidth = planeHeight * _camera.aspect;

            // Draw plane corners
            Vector3 right = _camera.transform.right * planeWidth * 0.5f;
            Vector3 up = _camera.transform.up * planeHeight * 0.5f;

            Vector3 topLeft = planeCenter - right + up;
            Vector3 topRight = planeCenter + right + up;
            Vector3 bottomLeft = planeCenter - right - up;
            Vector3 bottomRight = planeCenter + right - up;

            Gizmos.DrawLine(topLeft, topRight);
            Gizmos.DrawLine(topRight, bottomRight);
            Gizmos.DrawLine(bottomRight, bottomLeft);
            Gizmos.DrawLine(bottomLeft, topLeft);

            // Draw center cross
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(planeCenter + Vector3.up * 0.1f, planeCenter - Vector3.up * 0.1f);
            Gizmos.DrawLine(planeCenter + Vector3.right * 0.1f, planeCenter - Vector3.right * 0.1f);
        }
    }
}
