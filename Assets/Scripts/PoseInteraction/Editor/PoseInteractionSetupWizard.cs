using UnityEngine;
using UnityEditor;
using PoseInteraction.Core;
using PoseInteraction.Physics;
using PoseInteraction.Config;

namespace PoseInteraction.Editor
{
    /// <summary>
    /// Setup wizard to automatically configure pose-based ball interaction system
    /// Access via: Tools > Pose Interaction > Setup Wizard
    /// </summary>
    public class PoseInteractionSetupWizard : EditorWindow
    {
        private PoseInteractionConfig config;
        private GameObject ballObject;
        private Camera trackingCamera;
        private bool createDemoBall = true;

        [MenuItem("Tools/Pose Interaction/Setup Wizard")]
        public static void ShowWindow()
        {
            var window = GetWindow<PoseInteractionSetupWizard>("Pose Interaction Setup");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Pose-Based Ball Interaction Setup", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "This wizard will help you setup the pose-based ball interaction system quickly.",
                MessageType.Info);

            GUILayout.Space(10);

            // Step 1: Configuration
            GUILayout.Label("Step 1: Configuration", EditorStyles.boldLabel);
            config = (PoseInteractionConfig)EditorGUILayout.ObjectField(
                "Config Asset",
                config,
                typeof(PoseInteractionConfig),
                false);

            if (config == null)
            {
                if (GUILayout.Button("Create New Config"))
                {
                    CreateConfigAsset();
                }
            }

            GUILayout.Space(10);

            // Step 2: Camera
            GUILayout.Label("Step 2: Tracking Camera", EditorStyles.boldLabel);
            trackingCamera = (Camera)EditorGUILayout.ObjectField(
                "Camera",
                trackingCamera,
                typeof(Camera),
                true);

            if (trackingCamera == null)
            {
                if (GUILayout.Button("Use Main Camera"))
                {
                    trackingCamera = Camera.main;
                }
            }

            GUILayout.Space(10);

            // Step 3: Ball
            GUILayout.Label("Step 3: Ball Setup", EditorStyles.boldLabel);
            createDemoBall = EditorGUILayout.Toggle("Create Demo Ball", createDemoBall);

            if (!createDemoBall)
            {
                ballObject = (GameObject)EditorGUILayout.ObjectField(
                    "Existing Ball",
                    ballObject,
                    typeof(GameObject),
                    true);
            }

            GUILayout.Space(20);

            // Setup button
            GUI.enabled = config != null && trackingCamera != null;

            if (GUILayout.Button("Setup System", GUILayout.Height(40)))
            {
                SetupSystem();
            }

            GUI.enabled = true;

            GUILayout.Space(10);

            // Help
            EditorGUILayout.HelpBox(
                "After setup:\n" +
                "1. Assign PoseResultAdapter to forward MediaPipe results\n" +
                "2. Adjust config values to tune behavior\n" +
                "3. See SETUP_GUIDE.md for detailed instructions",
                MessageType.Info);
        }

        private void CreateConfigAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Pose Interaction Config",
                "PoseInteractionConfig",
                "asset",
                "Create a new configuration asset");

            if (!string.IsNullOrEmpty(path))
            {
                var newConfig = CreateInstance<PoseInteractionConfig>();
                AssetDatabase.CreateAsset(newConfig, path);
                AssetDatabase.SaveAssets();
                config = newConfig;

                Debug.Log($"Created config at: {path}");
            }
        }

        private void SetupSystem()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Setup Pose Interaction System");

            // 1. Setup camera with HandPoseTracker
            var handTracker = SetupHandPoseTracker();

            // 2. Setup or create ball
            GameObject ball = createDemoBall ? CreateDemoBall() : ballObject;
            SetupBallComponents(ball);

            // 3. Create game manager
            var gameManager = SetupGameManager(handTracker, ball);

            // 4. Setup adapter
            SetupPoseAdapter(gameManager);

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());

            EditorUtility.DisplayDialog(
                "Setup Complete!",
                "Pose Interaction System has been set up successfully!\n\n" +
                "Next steps:\n" +
                "1. Review the config settings\n" +
                "2. Integrate with MediaPipe (see SETUP_GUIDE.md)\n" +
                "3. Test in Play Mode",
                "OK");

            Debug.Log("[PoseInteractionSetupWizard] Setup complete!");
        }

        private HandPoseTracker SetupHandPoseTracker()
        {
            var tracker = trackingCamera.GetComponent<HandPoseTracker>();
            if (tracker == null)
            {
                tracker = Undo.AddComponent<HandPoseTracker>(trackingCamera.gameObject);
            }

            // Use SerializedObject for proper Undo support
            var so = new SerializedObject(tracker);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("trackingCamera").objectReferenceValue = trackingCamera;
            so.ApplyModifiedProperties();

            Debug.Log("[Setup] HandPoseTracker configured on camera");
            return tracker;
        }

        private GameObject CreateDemoBall()
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "InteractiveBall";
            ball.transform.position = trackingCamera.transform.position + trackingCamera.transform.forward * 3f;
            ball.transform.localScale = Vector3.one * 0.3f;

            Undo.RegisterCreatedObjectUndo(ball, "Create Ball");

            Debug.Log("[Setup] Created demo ball");
            return ball;
        }

        private void SetupBallComponents(GameObject ball)
        {
            if (ball == null) return;

            // Add Rigidbody
            var rb = ball.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = Undo.AddComponent<Rigidbody>(ball);
            }

            rb.mass = 0.5f;
            rb.linearDamping = 0.5f;
            rb.angularDamping = 0.05f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // Add PhysicsBallInteraction
            var ballInteraction = ball.GetComponent<PhysicsBallInteraction>();
            if (ballInteraction == null)
            {
                ballInteraction = Undo.AddComponent<PhysicsBallInteraction>(ball);
            }

            var so = new SerializedObject(ballInteraction);
            so.FindProperty("config").objectReferenceValue = config;
            so.ApplyModifiedProperties();

            Debug.Log("[Setup] Ball components configured");
        }

        private PoseBallGameManager SetupGameManager(HandPoseTracker tracker, GameObject ball)
        {
            // Create manager object
            GameObject managerObj = GameObject.Find("PoseBallGameManager");
            if (managerObj == null)
            {
                managerObj = new GameObject("PoseBallGameManager");
                Undo.RegisterCreatedObjectUndo(managerObj, "Create Game Manager");
            }

            var manager = managerObj.GetComponent<PoseBallGameManager>();
            if (manager == null)
            {
                manager = Undo.AddComponent<PoseBallGameManager>(managerObj);
            }

            // Configure manager
            var so = new SerializedObject(manager);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("handPoseTracker").objectReferenceValue = tracker;
            so.FindProperty("autoSetup").boolValue = true;
            so.FindProperty("showDebugUI").boolValue = true;

            // Add ball to list
            if (ball != null)
            {
                var ballInteraction = ball.GetComponent<PhysicsBallInteraction>();
                if (ballInteraction != null)
                {
                    var ballsList = so.FindProperty("balls");
                    ballsList.arraySize = 1;
                    ballsList.GetArrayElementAtIndex(0).objectReferenceValue = ballInteraction;
                }
            }

            so.ApplyModifiedProperties();

            Debug.Log("[Setup] Game Manager configured");
            return manager;
        }

        private void SetupPoseAdapter(PoseBallGameManager manager)
        {
            var adapter = manager.GetComponent<PoseResultAdapter>();
            if (adapter == null)
            {
                adapter = Undo.AddComponent<PoseResultAdapter>(manager.gameObject);
            }

            var so = new SerializedObject(adapter);
            so.FindProperty("gameManager").objectReferenceValue = manager;
            so.FindProperty("imageWidth").intValue = 1280;
            so.FindProperty("imageHeight").intValue = 720;
            so.ApplyModifiedProperties();

            Debug.Log("[Setup] Pose Result Adapter configured");
        }
    }
}
