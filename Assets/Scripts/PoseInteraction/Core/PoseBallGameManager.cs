using UnityEngine;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using PoseInteraction.Config;
using PoseInteraction.Physics;
using PoseInteraction.Utilities;
using System.Collections.Generic;

namespace PoseInteraction.Core
{
    /// <summary>
    /// Main coordinator for pose-based ball interaction system
    /// Connects MediaPipe pose detection with Unity physics
    /// </summary>
    public class PoseBallGameManager : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private PoseInteractionConfig config;

        [Header("Core Components")]
        [SerializeField] private HandPoseTracker handPoseTracker;

        [Header("Ball References")]
        [SerializeField] private List<PhysicsBallInteraction> balls = new List<PhysicsBallInteraction>();

        [Header("Hand Colliders")]
        [SerializeField] private VirtualHandCollider rightHandCollider;
        [SerializeField] private VirtualHandCollider leftHandCollider;

        [Header("Auto Setup")]
        [Tooltip("Automatically find and setup components on Start")]
        [SerializeField] private bool autoSetup = true;

        [Header("Debug UI")]
        [SerializeField] private bool showDebugUI = true;
        [SerializeField] private GUIStyle debugStyle;

        // State
        private bool _isInitialized;
        private int _totalHits;
        private float _sessionStartTime;

        // Performance tracking
        private float _lastUpdateTime;
        private int _frameCount;
        private float _fps;

        // Threading: Queue for MediaPipe callback thread -> main thread
        private Queue<PoseDataPacket> _poseDataQueue = new Queue<PoseDataPacket>();
        private readonly object _queueLock = new object();

        private struct PoseDataPacket
        {
            public PoseLandmarkerResult result;
            public int imageWidth;
            public int imageHeight;
        }

        private void Start()
        {
            if (autoSetup)
            {
                AutoSetupComponents();
            }

            Initialize();
        }

        private void AutoSetupComponents()
        {
            // Find HandPoseTracker
            if (handPoseTracker == null)
            {
                handPoseTracker = FindObjectOfType<HandPoseTracker>();
                if (handPoseTracker == null)
                {
                    Debug.LogWarning("[PoseBallGameManager] HandPoseTracker not found. Please assign it manually or add it to the camera.");
                }
            }

            // Find or create hand colliders
            if (rightHandCollider == null && config != null && config.trackRightHand)
            {
                rightHandCollider = CreateHandCollider("RightHandCollider", VirtualHandCollider.HandType.Right);
            }

            if (leftHandCollider == null && config != null && config.trackLeftHand)
            {
                leftHandCollider = CreateHandCollider("LeftHandCollider", VirtualHandCollider.HandType.Left);
            }

            // Find balls if not assigned
            if (balls.Count == 0)
            {
                var foundBalls = FindObjectsOfType<PhysicsBallInteraction>();
                balls.AddRange(foundBalls);

                if (balls.Count == 0)
                {
                    Debug.LogWarning("[PoseBallGameManager] No PhysicsBallInteraction components found. Please assign ball references.");
                }
            }

            // Setup CameraCalibrator for accurate coordinate conversion
            SetupCameraCalibrator();
        }

        private VirtualHandCollider CreateHandCollider(string name, VirtualHandCollider.HandType handType)
        {
            GameObject colliderObj = new GameObject(name);
            colliderObj.transform.SetParent(transform);

            var collider = colliderObj.AddComponent<VirtualHandCollider>();

            // Set config and hand type using public method (avoids Awake timing issues)
            collider.SetConfig(config, handType);

            return collider;
        }

        /// <summary>
        /// Setup automatic camera calibration for device-independent coordinate conversion
        /// </summary>
        private void SetupCameraCalibrator()
        {
            Debug.Log("[PoseBallGameManager] === Starting Camera Calibrator Setup ===");

            if (handPoseTracker == null)
            {
                Debug.LogWarning("[PoseBallGameManager] Cannot setup calibrator - HandPoseTracker not found");
                return;
            }

            // Get camera from HandPoseTracker
            Camera trackingCamera = handPoseTracker.GetComponent<Camera>();
            if (trackingCamera == null)
            {
                trackingCamera = Camera.main;
            }

            if (trackingCamera == null)
            {
                Debug.LogError("[PoseBallGameManager] Cannot setup calibrator - No camera found!");
                return;
            }

            Debug.Log($"[PoseBallGameManager] Found tracking camera: {trackingCamera.name}");

            // Get or add CameraCalibrator component to the camera
            var calibrator = trackingCamera.GetComponent<CameraCalibrator>();
            if (calibrator == null)
            {
                calibrator = trackingCamera.gameObject.AddComponent<CameraCalibrator>();
                Debug.Log("[PoseBallGameManager] Added CameraCalibrator to tracking camera");
            }
            else
            {
                Debug.Log("[PoseBallGameManager] CameraCalibrator already exists on camera");
            }

            // Set the first ball as calibration target
            if (balls.Count > 0 && balls[0] != null)
            {
                Debug.Log($"[PoseBallGameManager] Setting calibration target to ball: {balls[0].name} at position {balls[0].transform.position}");

                // Use public method to set calibration target
                calibrator.SetCalibrationTarget(balls[0].transform);

                // Trigger manual calibration
                Debug.Log("[PoseBallGameManager] Triggering calibration...");
                calibrator.CalibrateCamera();

                // Register calibrator with the converter
                LandmarkToWorldConverter.SetCalibrator(calibrator);
                Debug.Log("[PoseBallGameManager] ✅ Camera calibration complete - system ready for all devices!");
            }
            else
            {
                Debug.LogWarning("[PoseBallGameManager] No ball found for calibration. Calibrator will use default depth.");
            }

            Debug.Log("[PoseBallGameManager] === Camera Calibrator Setup Complete ===");
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            // Validate configuration
            if (config == null)
            {
                Debug.LogError("[PoseBallGameManager] Config is required! Create one via: Assets > Create > Pose Interaction > Config");
                return;
            }

            // Initialize hand colliders
            if (rightHandCollider != null)
            {
                rightHandCollider.Initialize(handPoseTracker);
                rightHandCollider.OnCollisionDetected += OnHandCollision;
            }

            if (leftHandCollider != null)
            {
                leftHandCollider.Initialize(handPoseTracker);
                leftHandCollider.OnCollisionDetected += OnHandCollision;
            }

            // Subscribe to ball events
            foreach (var ball in balls)
            {
                if (ball != null)
                {
                    ball.OnBallHit += OnBallHit;
                }
            }

            _sessionStartTime = Time.time;
            _isInitialized = true;

            Debug.Log("[PoseBallGameManager] System initialized successfully!");
        }

        /// <summary>
        /// Call this method with MediaPipe pose detection results (runs on callback thread)
        /// </summary>
        public void UpdatePoseDetection(PoseLandmarkerResult result, int imageWidth, int imageHeight)
        {
            if (!_isInitialized) return;

            // Queue data for processing on main thread (thread-safe)
            lock (_queueLock)
            {
                _poseDataQueue.Enqueue(new PoseDataPacket
                {
                    result = result,
                    imageWidth = imageWidth,
                    imageHeight = imageHeight
                });
            }

            _frameCount++;
        }

        private void Update()
        {
            if (!_isInitialized) return;

            // Process queued pose data on main thread (safe for Camera APIs)
            ProcessPoseDataQueue();

            // Update FPS tracking (runs on main thread, safe to use Time.time)
            if (Time.time - _lastUpdateTime >= 1f)
            {
                _fps = _frameCount / (Time.time - _lastUpdateTime);
                _frameCount = 0;
                _lastUpdateTime = Time.time;
            }

            // Check for collisions with balls
            CheckBallCollisions();
        }

        private void ProcessPoseDataQueue()
        {
            // Process all queued pose data from callback thread
            while (true)
            {
                PoseDataPacket packet;

                lock (_queueLock)
                {
                    if (_poseDataQueue.Count == 0)
                        break;

                    packet = _poseDataQueue.Dequeue();
                }

                // Process on main thread (safe to use Camera.ViewportToWorldPoint)
                if (handPoseTracker != null)
                {
                    handPoseTracker.UpdatePoseLandmarks(packet.result, packet.imageWidth, packet.imageHeight);
                }
            }
        }

        private void CheckBallCollisions()
        {
            foreach (var ball in balls)
            {
                if (ball == null) continue;

                var ballRigidbody = ball.GetComponent<Rigidbody>();
                if (ballRigidbody == null) continue;

                // Debug positions every 60 frames (useful for testing)
                if (Time.frameCount % 60 == 0)
                {
                    // Debug RIGHT hand
                    if (rightHandCollider != null)
                    {
                        Vector3 rightPos = rightHandCollider.CurrentPosition;
                        string rightStatus = rightHandCollider.IsTracked ? "<color=green>TRACKED</color>" : "<color=red>NOT TRACKED</color>";
                        Debug.Log($"[RIGHT HAND] Status: {rightStatus} | Speed: {rightHandCollider.CurrentSpeed:F2} m/s | Pos: ({rightPos.x:F2}, {rightPos.y:F2}, {rightPos.z:F2})");
                    }

                    // Debug LEFT hand
                    if (leftHandCollider != null)
                    {
                        Vector3 leftPos = leftHandCollider.CurrentPosition;
                        string leftStatus = leftHandCollider.IsTracked ? "<color=green>TRACKED</color>" : "<color=red>NOT TRACKED</color>";
                        Debug.Log($"[LEFT HAND] Status: {leftStatus} | Speed: {leftHandCollider.CurrentSpeed:F2} m/s | Pos: ({leftPos.x:F2}, {leftPos.y:F2}, {leftPos.z:F2})");
                    }

                    // Debug ball
                    Vector3 ballPos = ballRigidbody.position;
                    Debug.Log($"[BALL] Pos: ({ballPos.x:F2}, {ballPos.y:F2}, {ballPos.z:F2})");
                }

                // Check right hand collision
                if (rightHandCollider != null && config.trackRightHand)
                {
                    rightHandCollider.CheckCollisionWith(ballRigidbody);
                }

                // Check left hand collision
                if (leftHandCollider != null && config.trackLeftHand)
                {
                    leftHandCollider.CheckCollisionWith(ballRigidbody);
                }
            }
        }

        private void OnHandCollision(Rigidbody target, Vector3 contactPoint, Vector3 handVelocity)
        {
            // Find the ball component
            var ball = target.GetComponent<PhysicsBallInteraction>();
            if (ball != null)
            {
                ball.ApplyHit(contactPoint, handVelocity);
            }
        }

        private void OnBallHit(Vector3 direction, float force)
        {
            _totalHits++;
            Debug.Log($"[GameManager] Ball hit #{_totalHits}! Force: {force:F1}N, Direction: {direction}");
        }

        /// <summary>
        /// Add a ball to track
        /// </summary>
        public void RegisterBall(PhysicsBallInteraction ball)
        {
            if (ball != null && !balls.Contains(ball))
            {
                balls.Add(ball);
                ball.OnBallHit += OnBallHit;
            }
        }

        /// <summary>
        /// Remove a ball from tracking
        /// </summary>
        public void UnregisterBall(PhysicsBallInteraction ball)
        {
            if (ball != null && balls.Contains(ball))
            {
                balls.Remove(ball);
                ball.OnBallHit -= OnBallHit;
            }
        }

        /// <summary>
        /// Reset all balls to their starting positions
        /// </summary>
        public void ResetAllBalls()
        {
            foreach (var ball in balls)
            {
                if (ball != null)
                {
                    ball.ResetBall(ball.transform.position);
                }
            }

            _totalHits = 0;
        }

        /// <summary>
        /// Get game statistics
        /// </summary>
        public int GetTotalHits() => _totalHits;
        public float GetSessionTime() => Time.time - _sessionStartTime;

        private void OnGUI()
        {
            if (!showDebugUI || !_isInitialized) return;

            // Setup style
            if (debugStyle == null)
            {
                debugStyle = new GUIStyle(GUI.skin.label);
                debugStyle.fontSize = 24; // Larger for phone visibility
                debugStyle.normal.textColor = Color.white;
                debugStyle.alignment = TextAnchor.UpperLeft;
            }

            // Draw debug info - LARGER AREA for phone
            GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, Screen.height - 20));
            GUILayout.BeginVertical("box");

            GUILayout.Label("<b><size=28>DEBUG INFO (PHONE)</size></b>", debugStyle);
            GUILayout.Space(10);

            // Tracking status with POSITIONS
            if (handPoseTracker != null)
            {
                // RIGHT HAND
                if (handPoseTracker.IsRightHandTracked)
                {
                    Vector3 rPos = handPoseTracker.RightHandPosition;
                    float rSpeed = handPoseTracker.RightHandVelocity?.Speed ?? 0f;
                    GUILayout.Label($"<color=green>RIGHT HAND: TRACKED</color>", debugStyle);
                    GUILayout.Label($"  Pos: ({rPos.x:F1}, {rPos.y:F1}, {rPos.z:F1})", debugStyle);
                    GUILayout.Label($"  Speed: {rSpeed:F2} m/s", debugStyle);
                }
                else
                {
                    GUILayout.Label("<color=red>RIGHT HAND: NOT TRACKED</color>", debugStyle);
                }

                GUILayout.Space(5);

                // LEFT HAND
                if (handPoseTracker.IsLeftHandTracked)
                {
                    Vector3 lPos = handPoseTracker.LeftHandPosition;
                    float lSpeed = handPoseTracker.LeftHandVelocity?.Speed ?? 0f;
                    GUILayout.Label($"<color=green>LEFT HAND: TRACKED</color>", debugStyle);
                    GUILayout.Label($"  Pos: ({lPos.x:F1}, {lPos.y:F1}, {lPos.z:F1})", debugStyle);
                    GUILayout.Label($"  Speed: {lSpeed:F2} m/s", debugStyle);
                }
                else
                {
                    GUILayout.Label("<color=red>LEFT HAND: NOT TRACKED</color>", debugStyle);
                }
            }

            GUILayout.Space(10);

            // BALL INFO
            if (balls.Count > 0 && balls[0] != null)
            {
                Vector3 ballPos = balls[0].transform.position;
                GUILayout.Label($"<color=yellow>BALL:</color>", debugStyle);
                GUILayout.Label($"  Pos: ({ballPos.x:F1}, {ballPos.y:F1}, {ballPos.z:F1})", debugStyle);

                // Distance to ball
                if (rightHandCollider != null && rightHandCollider.IsTracked)
                {
                    float dist = Vector3.Distance(rightHandCollider.CurrentPosition, ballPos);
                    GUILayout.Label($"  Right Hand Dist: <color={(dist < 2f ? "green" : "red")}>{dist:F2}m</color>", debugStyle);
                }
                if (leftHandCollider != null && leftHandCollider.IsTracked)
                {
                    float dist = Vector3.Distance(leftHandCollider.CurrentPosition, ballPos);
                    GUILayout.Label($"  Left Hand Dist: <color={(dist < 2f ? "green" : "red")}>{dist:F2}m</color>", debugStyle);
                }
            }

            GUILayout.Space(10);

            // Stats
            GUILayout.Label($"<b>Stats:</b>", debugStyle);
            GUILayout.Label($"Total Hits: {_totalHits}", debugStyle);
            GUILayout.Label($"FPS: {_fps:F0}", debugStyle);

            GUILayout.Space(5);

            // Controls
            if (GUILayout.Button("Reset Balls"))
            {
                ResetAllBalls();
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            // Cleanup event subscriptions
            if (rightHandCollider != null)
            {
                rightHandCollider.OnCollisionDetected -= OnHandCollision;
            }

            if (leftHandCollider != null)
            {
                leftHandCollider.OnCollisionDetected -= OnHandCollision;
            }

            foreach (var ball in balls)
            {
                if (ball != null)
                {
                    ball.OnBallHit -= OnBallHit;
                }
            }
        }
    }
}
