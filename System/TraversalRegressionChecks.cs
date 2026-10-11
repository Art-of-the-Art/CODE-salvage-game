#if UNITY_EDITOR
/*
 * Проверяет, что изменения кода не сломали движение, лазание, камеру и анимации игрока.
 * Запускается вручную в редакторе Unity при открытой Main и выключенном Play Mode:
 * Tools → Salvage → Run Traversal Regression Checks, либо вызовом Run() из редакторского кода.
 * Для каждого сценария создаёт временную сцену с тестовыми объектами и при необходимости
 * копией игрока, вызывает проверяемые методы и сравнивает результат с ожидаемым.
 * После проверки закрывает временную сцену; возвращает отчёт PASS/FAIL, который меню
 * выводит в Console. В самой игре автоматически не запускается.
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Small, deterministic regression checks using real Unity physics queries.
// Temporary objects live in an additive scene; the player's scene is never saved or edited.
public static class TraversalRegressionChecks
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector3 Origin = new Vector3(10000f, 10000f, 10000f);
    static Scene testScene;
    static readonly List<string> failures = new List<string>();
    static readonly StringBuilder report = new StringBuilder();

    [MenuItem("Tools/Salvage/Run Traversal Regression Checks")]
    public static void RunFromMenu() => Debug.Log(Run());

    public static void RunAllFromCommandLine()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        string editModeReport = Run();
        string physicsReport = RunPhysicsSimulationChecks(true);
        Debug.Log(editModeReport);
        Debug.Log(physicsReport);
        if (!editModeReport.Contains("Failures: 0") || !physicsReport.Contains("Failures: 0"))
            throw new Exception("Traversal regression checks failed.");
    }

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run these checks in Edit Mode.");
        failures.Clear();
        report.Clear();
        Check("Wall input right follows the facing direction", WallRight);
        Check("Visual faces wall after attachment from a turn", WallFacing);
        Check("Visual keeps facing a rotating robot wall", RotatingWallFacing);
        Check("Diagonal climb does not exceed climb speed", WallDiagonal);
        Check("Mount follows a translated and rotated ledge", MovingMount);
        Check("Steep surfaces cannot be mount destinations", SteepMount);
        Check("Destroyed mount surface aborts traversal", DestroyedMount);
        Check("Disabled input discards queued jump", DisabledInput);
        Check("Airborne player is not carried by nearby ground", AirborneCarry);
        Check("Missing ground prevents a fresh jump", UnsupportedJump);
        Check("Ground carry includes rotation", GroundRotation);
        Check("Landing does not replay old platform movement", NoAccumulatedCarry);
        Check("Clearing ground clears the hit", ClearGroundHit);
        Check("Camera follows without a mouse", CameraWithoutMouse);
        Check("Camera inherits the supporting platform yaw", CameraInheritsSupportYaw);
        Check("Camera ignores the supporting robot in collision checks", CameraIgnoresSupportCollision);
        Check("Locomotion stays active while its speed returns to idle", IdleTransition);
        Check("Locomotion Blend Tree has the gameplay speed thresholds", LocomotionBlendTree);
        Check("All six clips animate one shared Humanoid rig", SharedRigAnimations);
        report.AppendLine("Failures: " + failures.Count);
        return report.ToString();
    }

    static void Check(string name, Action test)
    {
        Scene original = SceneManager.GetActiveScene();
        testScene = Application.isPlaying ? SceneManager.CreateScene("Traversal physics check")
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            test();
            report.AppendLine("PASS: " + name);
        }
        catch (Exception ex)
        {
            Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            failures.Add(name);
            report.AppendLine("FAIL: " + name + " -- " + cause.Message);
        }
        finally
        {
            if (Application.isPlaying)
            {
                foreach (var root in testScene.GetRootGameObjects())
                    UnityEngine.Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(testScene);
            }
            else
                EditorSceneManager.CloseScene(testScene, true);
            SceneManager.SetActiveScene(original);
            Physics.SyncTransforms();
        }
    }

    static GameObject Create(string name, Vector3 offset)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, testScene);
        go.transform.position = Origin + offset;
        return go;
    }

    static GameObject Box(string name, Vector3 offset, Vector3 size)
    {
        var go = Create(name, offset);
        go.layer = 3;
        go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    static PlayerClimbMotor Climber(out GameObject wall)
    {
        wall = Box("Wall", new Vector3(0f, 0f, 1f), new Vector3(8f, 4f, 1f));
        var go = Create("Climber", Vector3.zero);
        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        var motor = go.AddComponent<PlayerClimbMotor>();
        Set(motor, "climbableLayer", (LayerMask)(1 << 3));
        Physics.SyncTransforms();
        Require(Physics.Raycast(go.transform.position, Vector3.forward, out var hit, 2f, 1 << 3), "Wall not found");
        motor.AttachToWall(hit, rb);
        return motor;
    }

    static PlayerMovement AttachTurnedPlayer(out GameObject wall)
    {
        var player = Player();
        var bridge = player.GetComponent<PlayerAnimationBridge>();
        bridge.ModelTransform.localRotation = Quaternion.Euler(0f, 115f, 0f);
        wall = Box("Facing wall", new Vector3(0f, 0f, 1f), new Vector3(10f, 20f, 1f));
        Physics.SyncTransforms();
        Require(Physics.Raycast(player.transform.position, Vector3.forward, out var hit, 2f, 1 << 3), "Wall not found");
        player.GetComponent<PlayerClimbMotor>().AttachToWall(hit, player.GetComponent<Rigidbody>());
        typeof(PlayerMovement).GetMethod("SwitchState", Fields).Invoke(player, new[] { Enum.Parse(typeof(PlayerMovement).GetNestedType("MoveState", BindingFlags.NonPublic), "Climbing") });
        return player;
    }

    static void WallFacing()
    {
        var player = AttachTurnedPlayer(out _);
        var motor = player.GetComponent<PlayerClimbMotor>();
        var model = player.GetComponent<PlayerAnimationBridge>().ModelTransform;
        Require(Vector3.Dot(model.forward, -motor.SurfaceNormal) > 0.999f, "Visual retained its walking yaw after attachment");
    }

    static void RotatingWallFacing()
    {
        var player = AttachTurnedPlayer(out var wall);
        wall.transform.rotation = Quaternion.Euler(0f, 70f, 0f);
        wall.transform.position += new Vector3(0.2f, 0f, 0.3f);
        Physics.SyncTransforms();
        Invoke(player, "UpdateClimbing");
        var motor = player.GetComponent<PlayerClimbMotor>();
        var model = player.GetComponent<PlayerAnimationBridge>().ModelTransform;
        Require(Get(player, "currentState").ToString() == "Climbing", "Lost the rotating wall");
        Require(Vector3.Dot(model.forward, -motor.SurfaceNormal) > 0.999f, "Visual does not face the rotating wall");
    }

    static void WallRight()
    {
        var motor = Climber(out _);
        Vector3 start = motor.GetComponent<Rigidbody>().position;
        Vector3 right = motor.transform.right;
        motor.MoveOnWall(Vector2.right);
        Require(Vector3.Dot(motor.TargetPosition - start, right) > 0f, "Right input moves left");
    }

    static void WallDiagonal()
    {
        var motor = Climber(out _);
        Vector3 start = motor.GetComponent<Rigidbody>().position;
        motor.MoveOnWall(Vector2.one);
        float maximum = (float)Get(motor, "climbSpeed") * Time.fixedDeltaTime;
        Require((motor.TargetPosition - start).magnitude <= maximum + 0.002f, "Diagonal speed exceeds straight speed");
    }

    static PlayerClimbMotor Mount(out GameObject platform)
    {
        var motor = Climber(out platform);
        // Top is y=2; start just below it, facing +Z.
        motor.GetComponent<Rigidbody>().position = Origin + new Vector3(0f, 1.5f, -0.1f);
        typeof(PlayerClimbMotor).GetProperty("TargetPosition").SetValue(motor, motor.GetComponent<Rigidbody>().position);
        Require(motor.TryStartMount(motor.transform, motor.transform, default, default, 1 << 3), "Mount setup failed");
        return motor;
    }

    static void MovingMount()
    {
        var motor = Mount(out var platform);
        Set(motor, "mountStartTime", Time.time - 2f);
        motor.UpdateMount();
        Vector3 localLanding = platform.transform.InverseTransformPoint(motor.TargetPosition);
        platform.transform.position += new Vector3(3f, 1f, 2f);
        platform.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
        motor.UpdateMount();
        Near(motor.TargetPosition, platform.transform.TransformPoint(localLanding), "Landing target stayed in world space");
    }

    static void SteepMount()
    {
        var motor = Climber(out var wall);
        UnityEngine.Object.DestroyImmediate(wall);
        var slope = Box("Steep top", new Vector3(0f, 0f, 0.8f), new Vector3(20f, 0.1f, 20f));
        slope.transform.rotation = Quaternion.Euler(0f, 0f, 70f);
        Physics.SyncTransforms();
        // Keep a real contact so this checks landing slope rejection, not missing-wall rejection.
        Set(motor, "currentWall", slope.transform);
        Set(motor, "wallCollider", slope.GetComponent<Collider>());
        Require(!motor.TryStartMount(motor.transform, motor.transform, default, default, 1 << 3), "Accepted a 70-degree landing");
    }

    static void DestroyedMount()
    {
        var motor = Mount(out var platform);
        UnityEngine.Object.DestroyImmediate(platform);
        var player = Player();
        Set(player, "climbMotor", motor);
        Set(player, "currentState", Enum.Parse(typeof(PlayerMovement).GetNestedType("MoveState", BindingFlags.NonPublic), "Mounting"));
        Invoke(player, "UpdateMounting");
        Require(Get(player, "currentState").ToString() == "Airborne", "Player remains mounting after losing the surface");
        Require(((Vector3)Get(player, "currentVelocity")).y < 0f, "Gravity did not resume");
    }

    static void DisabledInput()
    {
        var go = Create("Input", Vector3.zero);
        var reader = go.AddComponent<PlayerInputReader>();
        Set(reader, "jumpQueued", true);
        Set(reader, "moveInput", Vector2.one);
        Set(reader, "runHeld", true);
        // Invoke the lifecycle method directly. SendMessage on an editor-only temporary
        // behaviour makes Unity log an internal ShouldRunBehaviour assertion.
        Invoke(reader, "OnDisable");
        Require(!reader.ConsumeJump() && reader.MoveInput == Vector2.zero && !reader.RunHeld, "Input survives disable");
    }

    static PlayerMovement Player()
    {
        var original = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        Require(original != null, "Open Main scene before running checks");
        var clone = UnityEngine.Object.Instantiate(original.gameObject);
        SceneManager.MoveGameObjectToScene(clone, testScene);
        clone.transform.position = Origin;
        var movement = clone.GetComponent<PlayerMovement>();
        Invoke(clone.GetComponent<PlayerAnimationBridge>(), "Awake");
        Invoke(movement, "Awake");
        return movement;
    }

    static void AirborneCarry()
    {
        var player = Player();
        var platform = Box("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(20f, 0.5f, 20f));
        var probe = player.GetComponent<PlayerGroundProbe>();
        var rb = player.GetComponent<Rigidbody>();
        Physics.SyncTransforms();
        probe.Probe();
        Require(probe.IsGrounded, "Floor not found");
        Set(player, "currentState", Enum.Parse(typeof(PlayerMovement).GetNestedType("MoveState", BindingFlags.NonPublic), "Airborne"));
        rb.linearVelocity = Vector3.up * 4f;
        platform.transform.position += Vector3.right;
        Physics.SyncTransforms();
        Vector3 start = rb.position;
        Invoke(player, "FixedUpdate");
        Near(rb.position, start, "Airborne body was moved by ground delta");
    }

    static void UnsupportedJump()
    {
        var player = Player();
        Set(player, "stationaryJumpTakeoffDelay", 0f);
        Set(player.GetComponent<PlayerInputReader>(), "jumpQueued", true);
        Invoke(player, "FixedUpdate");
        Require(player.GetComponent<Rigidbody>().linearVelocity.y <= 0f, "Jump launched after ground was lost");
    }

    static void GroundRotation()
    {
        var floor = Box("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(20f, 0.5f, 20f));
        var body = Create("Body", Vector3.right * 2f);
        var rb = body.AddComponent<Rigidbody>();
        var probe = body.AddComponent<PlayerGroundProbe>();
        Set(probe, "groundLayer", (LayerMask)(1 << 3));
        Physics.SyncTransforms();
        probe.Probe();
        Vector3 local = floor.transform.InverseTransformPoint(rb.position);
        floor.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        probe.SampleGroundVelocity(rb.position);
        Near(rb.position + probe.CurrentGroundVelocity * Time.fixedDeltaTime, floor.transform.TransformPoint(local), "Rotation velocity is incorrect");
    }

    static void NoAccumulatedCarry()
    {
        var floor = Box("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(20f, 0.5f, 20f));
        var body = Create("Body", Vector3.zero);
        var rb = body.AddComponent<Rigidbody>();
        var probe = body.AddComponent<PlayerGroundProbe>();
        Set(probe, "groundLayer", (LayerMask)(1 << 3));
        Physics.SyncTransforms();
        probe.Probe();
        floor.transform.position += Vector3.right;
        probe.SampleGroundVelocity(rb.position);
        floor.transform.position += Vector3.right;
        probe.SampleGroundVelocity(rb.position);
        Near(rb.position, Origin, "Body moved before landing");
        floor.transform.position += Vector3.right;
        probe.SampleGroundVelocity(rb.position);
        Near(rb.position, Origin, "Sampling support velocity moved the body");
        Near(probe.CurrentGroundVelocity * Time.fixedDeltaTime, Vector3.right, "Landing replayed old platform displacement");
    }

    static void ClearGroundHit()
    {
        Box("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(20f, 0.5f, 20f));
        var probe = Create("Probe", Vector3.zero).AddComponent<PlayerGroundProbe>();
        Set(probe, "groundLayer", (LayerMask)(1 << 3));
        Physics.SyncTransforms();
        probe.Probe();
        Require(probe.GroundHit.collider != null, "Floor not found");
        probe.ClearGround();
        Require(probe.GroundHit.collider == null && !probe.IsGrounded, "Stale ground hit after detach");
    }

    static void CameraWithoutMouse()
    {
        var camera = Create("Camera", Vector3.zero).AddComponent<ThirdPersonCamera>();
        camera.player = Create("Target", Vector3.zero).transform;
        Set(camera, "currentDistance", camera.defaultDistance);
        camera.player.position += Vector3.right * 3f;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        try
        {
            if (mouse != null) UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            Invoke(camera, "LateUpdate");
            Require(Mathf.Abs(camera.transform.position.x - camera.player.position.x) < 0.01f, "Camera stopped following");
        }
        finally
        {
            if (mouse != null) UnityEngine.InputSystem.InputSystem.AddDevice(mouse);
        }
    }

    static void CameraInheritsSupportYaw()
    {
        var floor = Box("Turning floor", new Vector3(0f, -0.25f, 0f), new Vector3(20f, 0.5f, 20f));
        var target = Create("Camera target", Vector3.zero);
        target.AddComponent<Rigidbody>().isKinematic = true;
        var probe = target.AddComponent<PlayerGroundProbe>();
        Set(probe, "groundLayer", (LayerMask)(1 << 3));
        var camera = Create("Camera", Vector3.zero).AddComponent<ThirdPersonCamera>();
        camera.player = target.transform;
        camera.sensitivity = 0f;
        Set(camera, "groundProbe", probe);
        Set(camera, "currentDistance", camera.defaultDistance);
        Physics.SyncTransforms();
        probe.Probe();

        Invoke(camera, "LateUpdate");
        floor.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        Physics.SyncTransforms();
        Invoke(camera, "LateUpdate");

        Vector3 cameraForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
        Vector3 expectedForward = Quaternion.Euler(0f, 45f, 0f) * Vector3.forward;
        Require(Vector3.Dot(cameraForward, expectedForward) > 0.999f, "Camera kept its old world yaw");
    }

    static void CameraIgnoresSupportCollision()
    {
        var robot = Create("Camera support robot", Vector3.zero);
        robot.layer = 3;
        var body = robot.AddComponent<Rigidbody>();
        body.isKinematic = true;

        var floor = new GameObject("Floor collider");
        SceneManager.MoveGameObjectToScene(floor, testScene);
        floor.layer = 3;
        floor.transform.SetParent(robot.transform, false);
        floor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
        floor.AddComponent<BoxCollider>().size = new Vector3(20f, 0.5f, 20f);

        var mast = new GameObject("Rear mast");
        SceneManager.MoveGameObjectToScene(mast, testScene);
        mast.layer = 3;
        mast.transform.SetParent(robot.transform, false);
        mast.transform.localPosition = new Vector3(0f, 4f, -2f);
        mast.AddComponent<BoxCollider>().size = new Vector3(2f, 3f, 1f);

        var target = Create("Camera target", Vector3.zero);
        target.AddComponent<Rigidbody>().isKinematic = true;
        var probe = target.AddComponent<PlayerGroundProbe>();
        Set(probe, "groundLayer", (LayerMask)(1 << 3));
        var camera = Create("Camera", Vector3.zero).AddComponent<ThirdPersonCamera>();
        camera.player = target.transform;
        camera.sensitivity = 0f;
        Set(camera, "groundProbe", probe);
        Set(camera, "collisionMask", (LayerMask)(1 << 3));
        Set(camera, "currentDistance", camera.defaultDistance);
        Physics.SyncTransforms();
        probe.Probe();
        Require(probe.CurrentGroundCollider == floor.GetComponent<Collider>(), "Robot floor not detected");

        Invoke(camera, "LateUpdate");
        Vector3 pivotPoint = target.transform.position + Vector3.up * camera.heightOffset;
        Require(Mathf.Abs(Vector3.Distance(camera.transform.position, pivotPoint) - camera.defaultDistance) < 0.01f,
            "Robot shortened its own camera distance");
    }
    static void IdleTransition()
    {
        var player = Player();
        var bridge = player.GetComponent<PlayerAnimationBridge>();
        var animator = (Animator)Get(bridge, "animator");
        animator.Rebind();
        animator.Play("Locomotion", 0, 0f);
        animator.Update(0f);
        bridge.SetMovementState(true, 0f, 3f, true, false, false);
        animator.Update(0.1f);
        bridge.SetMovementState(true, 0f, 0f, false, false, false);
        for (int i = 0; i < 30; i++)
        {
            Invoke(bridge, "Update");
            animator.Update(0.02f);
        }
        Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") && !animator.IsInTransition(0),
            "Ground speed change left or restarted Locomotion");
        Require(animator.GetFloat("locomotionSpeed") < 0.05f, "Locomotion speed did not settle near idle");
    }

    static void LocomotionBlendTree()
    {
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
            "Assets/PlayerAnimator.controller");
        var states = controller.layers[0].stateMachine.states;
        UnityEditor.Animations.BlendTree tree = null;
        foreach (var childState in states)
            if (childState.state.name == "Locomotion")
                tree = childState.state.motion as UnityEditor.Animations.BlendTree;

        Require(tree != null, "Locomotion does not contain a Blend Tree");
        Require(tree.blendParameter == "locomotionSpeed", "Wrong Locomotion blend parameter");
        Require(tree.children.Length == 3, "Locomotion must contain exactly Idle, Walk and Run");

        var expectedThresholds = new Dictionary<string, float>
        {
            { "Idle", 0f }, { "Walk", 3f }, { "Run", 8f }
        };
        foreach (var child in tree.children)
        {
            Require(child.motion != null && expectedThresholds.ContainsKey(child.motion.name),
                "Unexpected motion in Locomotion Blend Tree");
            Require(Mathf.Abs(child.threshold - expectedThresholds[child.motion.name]) < 0.001f,
                "Wrong threshold for " + child.motion.name + ": " + child.threshold);
        }
    }

    static void SharedRigAnimations()
    {
        var player = Player();
        var animator = player.GetComponentInChildren<Animator>();
        Require(animator != null && animator.isHuman && animator.avatar.isValid, "Shared model has no valid Humanoid Avatar");
        Require(player.GetComponentsInChildren<Animator>().Length == 1, "Expected one Animator for all clips");
        Require(AssetDatabase.GetAssetPath(animator.avatar) == "Assets/Characters/Player/Model/PlayerModel.fbx", "Wrong canonical rig");
        var renderers = player.GetComponentsInChildren<SkinnedMeshRenderer>();
        Require(renderers.Length > 0, "Shared model has no skinned mesh");
        int firstMeshInstance = renderers[0].GetInstanceID();
        var bones = new List<Transform>();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            var bone = animator.GetBoneTransform((HumanBodyBones)i);
            if (bone != null) bones.Add(bone);
        }
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/PlayerAnimator.controller");
        foreach (string name in new[] { "Idle", "Walk", "Run", "Jump", "JumpInPlace", "Climb" })
        {
            string path = "Assets/Animations/Player/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Require(clip != null && clip.humanMotion && clip.length > 0f, "Invalid standalone clip: " + name);
            bool assigned = false;
            foreach (var state in controller.layers[0].stateMachine.states)
                if (state.state.name == name && state.state.motion == clip) assigned = true;
            Require(assigned, "Controller does not use standalone clip: " + name);
            foreach (var dependency in AssetDatabase.GetDependencies(path))
                Require(!dependency.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase), "Animation still depends on an FBX: " + name);
            animator.Rebind();
            animator.SetFloat("runPlaybackSpeed", 1f);
            animator.Play(name, 0, 0.15f);
            animator.Update(0f);
            var before = new Quaternion[bones.Count];
            for (int i = 0; i < bones.Count; i++) before[i] = bones[i].localRotation;
            animator.Play(name, 0, 0.45f);
            animator.Update(0f);
            float movement = 0f;
            for (int i = 0; i < bones.Count; i++) movement += Quaternion.Angle(before[i], bones[i].localRotation);
            Require(movement > 0.1f, "Clip does not animate the shared bones: " + name);
            Require(player.GetComponentsInChildren<SkinnedMeshRenderer>()[0].GetInstanceID() == firstMeshInstance, "Clip replaced the shared model");
        }
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/Player/Model/PlayerModel.fbx"))
            Require(!(asset is AnimationClip), "Model imports an embedded animation");
    }

    // Runs only in Play Mode: the real PhysX solver advances, not just position arithmetic.
    public static string RunPhysicsSimulationChecks(bool allowEditMode = false)
    {
        if (!Application.isPlaying && !allowEditMode)
            throw new InvalidOperationException("Enter Play Mode first; stop it afterwards to restore Main.");
        failures.Clear();
        report.Clear();
        SimulationMode originalMode = Physics.simulationMode;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            Check("Standing on translating robot for 8 seconds", () => StandingOnRobot(false));
            Check("Standing on rotating robot for 8 seconds", () => StandingOnRobot(true));
            Check("Robot start and stop do not launch the passenger", StartStopRobot);
            Check("Jump inherits robot velocity exactly once", JumpFromRobot);
            Check("Moving wall hang ignores collision impulses", () => HangingOnRobot(false));
            Check("Rotating wall hang keeps local anchor", () => HangingOnRobot(true));
            Check("Wall jump uses the updated wall normal", UpdatedWallJump);
            Check("Walk and run work on a translating robot", WalkOnRobot);
            Check("Mount reaches the moving top with collisions", MountWithPhysics);
            Check("Walk, stop and jump on 30/45/55 degree slopes", Slopes);
            Check("Stopping walk keeps standing on robot", StopWalking);
            Check("Prepared jump inherits speed exactly once", PreparedJump);
            Check("Ground probe does not ground a player one metre above floor", GroundDistance);
        }
        finally
        {
            Physics.simulationMode = originalMode;
        }
        report.AppendLine("Failures: " + failures.Count);
        return report.ToString();
    }

    static RobotMovement PhysicsRobot(Vector3 offset, Vector3 size)
    {
        var go = Box("Test robot", offset, size);
        var body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        var robot = go.AddComponent<RobotMovement>();
        Invoke(robot, "Awake");
        Set(robot, "hasTarget", true);
        Set(robot, "turning", false);
        Set(robot, "moveDirection", Vector3.right);
        Set(robot, "distanceLeft", 1000f);
        return robot;
    }

    static PlayerMovement PhysicsPlayer(Vector3 offset)
    {
        var player = Player();
        player.GetComponent<PlayerInputReader>().enabled = false;
        player.transform.position = Origin + offset;
        player.GetComponent<Rigidbody>().position = Origin + offset;
        Invoke(player.GetComponent<PlayerGroundProbe>(), "Awake");
        Physics.SyncTransforms();
        return player;
    }

    static void Step(RobotMovement robot, PlayerMovement player, bool rotate = false)
    {
        if (rotate)
        {
            Set(robot, "turning", true);
            Set(robot, "targetRotation", Quaternion.Euler(0f, 60f, 0f) * robot.GetComponent<Rigidbody>().rotation);
        }
        Invoke(robot, "FixedUpdate");
        Invoke(player, "FixedUpdate");
        Physics.Simulate(Time.fixedDeltaTime);
    }

    static Vector3 RobotLocal(RobotMovement robot, PlayerMovement player)
    {
        var body = robot.GetComponent<Rigidbody>();
        return Quaternion.Inverse(body.rotation) * (player.GetComponent<Rigidbody>().position - body.position);
    }

    static void StandingOnRobot(bool rotate)
    {
        var robot = PhysicsRobot(new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f));
        var player = PhysicsPlayer(new Vector3(3f, 0.01f, 0f));
        Vector3 start = RobotLocal(robot, player);
        float maxDrift = 0f;
        for (int i = 0; i < 400; i++)
        {
            Step(robot, player, rotate);
            Vector3 delta = RobotLocal(robot, player) - start;
            delta.y = 0f;
            maxDrift = Mathf.Max(maxDrift, delta.magnitude);
            Require(Get(player, "currentState").ToString() == "Grounded", "Lost ground at step " + i);
        }
        report.AppendLine("  max local drift: " + maxDrift.ToString("F4") + " m");
        Require(maxDrift < 0.08f, "Passenger drifts: " + maxDrift);
    }

    static void StartStopRobot()
    {
        var robot = PhysicsRobot(new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f));
        var player = PhysicsPlayer(Vector3.up * 0.01f);
        Vector3 start = RobotLocal(robot, player);
        for (int i = 0; i < 200; i++)
        {
            Set(robot, "moveSpeed", i < 50 || i >= 100 && i < 150 ? 7f : 0f);
            Step(robot, player);
        }
        Vector3 delta = RobotLocal(robot, player) - start;
        delta.y = 0f;
        Require(delta.magnitude < 0.08f, "Start/stop drift: " + delta);
    }

    static void JumpFromRobot()
    {
        var robot = PhysicsRobot(new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f));
        var player = PhysicsPlayer(Vector3.up * 0.01f);
        for (int i = 0; i < 30; i++) Step(robot, player);
        Set(player, "stationaryJumpTakeoffDelay", 0f);
        Set(player.GetComponent<PlayerInputReader>(), "jumpQueued", true);
        Step(robot, player);
        Vector3 velocity = player.GetComponent<Rigidbody>().linearVelocity;
        Require(Get(player, "currentState").ToString() == "Airborne", "Jump did not launch");
        Require(Mathf.Abs(velocity.x - 7f) < 0.1f, "Wrong inherited speed: " + velocity);
        float startY = player.GetComponent<Rigidbody>().position.y;
        for (int i = 0; i < 10; i++) Step(robot, player);
        Require(player.GetComponent<Rigidbody>().position.y > startY + 1f, "Jump grounded prematurely");
    }

    static PlayerMovement Hang(RobotMovement robot)
    {
        var player = PhysicsPlayer(new Vector3(0f, 0f, -1.1f));
        Physics.SyncTransforms();
        Require(Physics.Raycast(player.GetComponent<Rigidbody>().position, Vector3.forward, out var hit, 3f, 1 << 3), "Wall not found");
        player.GetComponent<PlayerClimbMotor>().AttachToWall(hit, player.GetComponent<Rigidbody>());
        typeof(PlayerMovement).GetMethod("SwitchState", Fields).Invoke(player,
            new[] { Enum.Parse(typeof(PlayerMovement).GetNestedType("MoveState", BindingFlags.NonPublic), "Climbing") });
        return player;
    }

    static void HangingOnRobot(bool rotate)
    {
        var robot = PhysicsRobot(Vector3.zero, new Vector3(12f, 12f, 1f));
        var player = Hang(robot);
        Vector3 start = RobotLocal(robot, player);
        float maxDrift = 0f;
        for (int i = 0; i < 400; i++)
        {
            if (i == 20) player.GetComponent<Rigidbody>().linearVelocity = new Vector3(12f, 5f, 3f);
            Step(robot, player, rotate);
            maxDrift = Mathf.Max(maxDrift, Vector3.Distance(RobotLocal(robot, player), start));
            Require(Get(player, "currentState").ToString() == "Climbing", "Lost wall at step " + i);
        }
        report.AppendLine("  max local hang drift: " + maxDrift.ToString("F4") + " m");
        Require(maxDrift < 0.08f, "Wall drift: " + maxDrift);
    }

    static void UpdatedWallJump()
    {
        var robot = PhysicsRobot(Vector3.zero, new Vector3(12f, 12f, 1f));
        var player = Hang(robot);
        robot.GetComponent<Rigidbody>().rotation = Quaternion.Euler(0f, 45f, 0f);
        Physics.SyncTransforms();
        Set(player.GetComponent<PlayerInputReader>(), "jumpQueued", true);
        Step(robot, player);
        Vector3 expected = Quaternion.Euler(0f, 45f, 0f) * Vector3.back;
        Vector3 planar = Vector3.ProjectOnPlane(player.GetComponent<Rigidbody>().linearVelocity, Vector3.up).normalized;
        Require(Vector3.Dot(expected, planar) > 0.99f, "Wall jump used stale direction");
    }

    static void WalkOnRobot()
    {
        var robot = PhysicsRobot(new Vector3(0f, -0.5f, 0f), new Vector3(100f, 1f, 100f));
        var player = PhysicsPlayer(Vector3.up * 0.01f);
        Set(player, "moveInput", Vector2.up);
        Set(player, "currentMoveRotation", Quaternion.identity);
        for (int i = 0; i < 100; i++) Step(robot, player);
        float walking = player.GetComponent<Rigidbody>().linearVelocity.z;
        Set(player.GetComponent<PlayerInputReader>(), "runHeld", true);
        for (int i = 0; i < 100; i++) Step(robot, player);
        float running = player.GetComponent<Rigidbody>().linearVelocity.z;
        report.AppendLine("  walk/run relative speeds: " + walking + "/" + running);
        Require(walking > 2.7f && running > 7.7f, "Friction or controller reduces movement speed");
    }

    static void MountWithPhysics()
    {
        var robot = PhysicsRobot(Vector3.zero, new Vector3(12f, 4f, 1f));
        var player = Hang(robot);
        var body = player.GetComponent<Rigidbody>();
        player.transform.position = body.position + Vector3.up * 1.5f;
        body.position = player.transform.position;
        var motor = player.GetComponent<PlayerClimbMotor>();
        // Reattach at the ledge height to establish the correct saved anchor.
        Physics.SyncTransforms();
        Require(Physics.Raycast(body.position, Vector3.forward, out var hit, 3f, 1 << 3), "Ledge not found");
        motor.AttachToWall(hit, body);
        for (int i = 0; i < 80; i++)
        {
            if (Get(player, "currentState").ToString() == "Mounting")
                Set(motor, "mountStartTime", Time.time - i * Time.fixedDeltaTime);
            Step(robot, player);
        }
        Require(Get(player, "currentState").ToString() == "Grounded", "Mount never reached Grounded");
        Require(body.position.y > Origin.y + 1.9f, "Mount blocked by wall");
    }

    static void Slopes()
    {
        foreach (float angle in new[] { 30f, 45f, 55f })
        {
            var slope = Box("Slope", new Vector3(0f, -0.1f, 0f), new Vector3(100f, 0.2f, 100f));
            slope.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            float radians = angle * Mathf.Deg2Rad;
            float startY = 0.5f / Mathf.Cos(radians) - 0.5f + 0.03f;
            var player = PhysicsPlayer(Vector3.up * startY);
            var body = player.GetComponent<Rigidbody>();
            Set(player, "moveInput", Vector2.up);
            Set(player, "currentMoveRotation", Quaternion.LookRotation(Vector3.right));
            for (int i = 0; i < 100; i++)
            {
                Invoke(player, "FixedUpdate");
                Physics.Simulate(Time.fixedDeltaTime);
            }
            Vector3 alongSlope = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
            float speed = Vector3.Dot(body.linearVelocity, alongSlope);
            Require(speed > 2.8f && speed < 3.2f, "Slope speed at " + angle + ": " + speed);
            Set(player, "moveInput", Vector2.zero);
            for (int i = 0; i < 100; i++) { Invoke(player, "FixedUpdate"); Physics.Simulate(Time.fixedDeltaTime); }
            Require(Mathf.Abs(Vector3.Dot(body.linearVelocity, alongSlope)) < 0.05f, "Sliding at " + angle);
            Set(player, "stationaryJumpTakeoffDelay", 0f);
            Set(player.GetComponent<PlayerInputReader>(), "jumpQueued", true);
            Invoke(player, "FixedUpdate");
            Require(body.linearVelocity.y > 10f, "Cannot jump on slope " + angle);
            UnityEngine.Object.DestroyImmediate(player.gameObject);
            UnityEngine.Object.DestroyImmediate(slope);
            Physics.SyncTransforms();
        }
    }

    static void StopWalking()
    {
        var robot = PhysicsRobot(new Vector3(0f, -0.5f, 0f), new Vector3(100f, 1f, 100f));
        var player = PhysicsPlayer(Vector3.up * 0.01f);
        Set(player, "moveInput", Vector2.up);
        for (int i = 0; i < 100; i++) Step(robot, player);
        Set(player, "moveInput", Vector2.zero);
        for (int i = 0; i < 100; i++) Step(robot, player);
        Vector3 start = RobotLocal(robot, player);
        for (int i = 0; i < 200; i++) Step(robot, player);
        Require(Vector3.Distance(RobotLocal(robot, player), start) < 0.03f, "Continued sliding after release");
    }

    static void PreparedJump()
    {
        var robot = PhysicsRobot(new Vector3(0f, -0.5f, 0f), new Vector3(100f, 1f, 100f));
        var player = PhysicsPlayer(Vector3.up * 0.01f);
        for (int i = 0; i < 30; i++) Step(robot, player);
        Set(player.GetComponent<PlayerInputReader>(), "jumpQueued", true);
        Step(robot, player);
        Require(Get(player, "currentState").ToString() == "JumpPreparing", "No jump preparation");
        Set(robot, "moveSpeed", 4f);
        for (int i = 0; i < 20; i++) Step(robot, player);
        Require(Get(player, "currentState").ToString() == "Airborne", "Prepared jump failed");
        Require(Mathf.Abs(player.GetComponent<Rigidbody>().linearVelocity.x - 4f) < 0.05f,
            "Prepared jump inherited old/doubled platform speed");
    }

    static void GroundDistance()
    {
        Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f));
        var player = PhysicsPlayer(Vector3.up);
        player.GetComponent<PlayerGroundProbe>().Probe();
        Require(!player.GetComponent<PlayerGroundProbe>().IsGrounded, "Grounded one metre above floor");
    }

    static object Get(object target, string field) => target.GetType().GetField(field, Fields).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    static void Invoke(object target, string method) => target.GetType().GetMethod(method, Fields).Invoke(target, null);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Near(Vector3 actual, Vector3 expected, string message) => Require(Vector3.Distance(actual, expected) < 0.01f, message + ": " + actual + " vs " + expected);
}

[InitializeOnLoad]
public static class TraversalPlayModeCommandLine
{
    const string ActiveKey = "Salvage.TraversalPlayMode.Active";
    const string ResultKey = "Salvage.TraversalPlayMode.Result";

    static TraversalPlayModeCommandLine()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static void Run()
    {
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetString(ResultKey, "Play Mode check did not finish.");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        if (state == PlayModeStateChange.EnteredPlayMode)
            new GameObject("Camera lifecycle validation").AddComponent<CameraLifecycleValidation>();
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            string result = SessionState.GetString(ResultKey, "Play Mode check did not report.");
            bool passed = result.StartsWith("PASS:");
            Debug.Log(result);
            SessionState.EraseBool(ActiveKey);
            SessionState.EraseString(ResultKey);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }

    public static void Complete(bool passed, string details)
    {
        SessionState.SetString(ResultKey, (passed ? "PASS: " : "FAIL: ") + details);
        EditorApplication.ExitPlaymode();
    }
}

[DefaultExecutionOrder(200)]
public class CameraLifecycleValidation : MonoBehaviour
{
    Rigidbody platformBody;
    Rigidbody playerBody;
    PlayerGroundProbe groundProbe;
    ThirdPersonCamera followCamera;
    readonly Vector3 localPlayerPosition = new Vector3(3f, 0.26f, 0f);
    Vector3 initialLocalCameraOffset;
    float elapsed;
    float maxLocalJitter;
    bool hasBaseline;

    void Start()
    {
        Time.captureFramerate = 60;

        var platform = new GameObject("Lifecycle platform");
        platform.layer = 3;
        platform.transform.position = new Vector3(10000f, 10000f, 10000f);
        platform.AddComponent<BoxCollider>().size = new Vector3(20f, 0.5f, 20f);
        platformBody = platform.AddComponent<Rigidbody>();
        platformBody.isKinematic = true;
        platformBody.interpolation = RigidbodyInterpolation.Interpolate;

        var player = new GameObject("Lifecycle player");
        player.transform.position = platform.transform.TransformPoint(localPlayerPosition);
        playerBody = player.AddComponent<Rigidbody>();
        playerBody.isKinematic = true;
        playerBody.interpolation = RigidbodyInterpolation.Interpolate;
        groundProbe = player.AddComponent<PlayerGroundProbe>();
        typeof(PlayerGroundProbe).GetField("groundLayer", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(groundProbe, (LayerMask)(1 << 3));

        var cameraObject = new GameObject("Lifecycle camera");
        followCamera = cameraObject.AddComponent<ThirdPersonCamera>();
        followCamera.player = player.transform;
        followCamera.sensitivity = 0f;
        followCamera.defaultDistance = 5f;
        followCamera.heightOffset = 4f;
        Physics.SyncTransforms();
        groundProbe.Probe();
    }

    void FixedUpdate()
    {
        elapsed += Time.fixedDeltaTime;
        Quaternion nextRotation = Quaternion.Euler(0f, elapsed * 30f, 0f);
        platformBody.MoveRotation(nextRotation);
        playerBody.MovePosition(platformBody.position + nextRotation * localPlayerPosition);
        groundProbe.Probe();
    }

    void LateUpdate()
    {
        try
        {
            if (elapsed < 0.5f || followCamera == null)
                return;

            Vector3 pivot = playerBody.transform.position + Vector3.up * followCamera.heightOffset;
            Vector3 localCameraOffset = Quaternion.Inverse(platformBody.transform.rotation)
                * (followCamera.transform.position - pivot);

            if (!hasBaseline)
            {
                initialLocalCameraOffset = localCameraOffset;
                hasBaseline = true;
            }
            else
                maxLocalJitter = Mathf.Max(maxLocalJitter, Vector3.Distance(initialLocalCameraOffset, localCameraOffset));

            if (elapsed < 3.5f)
                return;

            float inheritedYaw = Vector3.SignedAngle(
                Vector3.forward,
                Vector3.ProjectOnPlane(followCamera.transform.forward, Vector3.up),
                Vector3.up
            );
            float expectedYaw = platformBody.transform.eulerAngles.y;
            float yawError = Mathf.Abs(Mathf.DeltaAngle(inheritedYaw, expectedYaw));
            bool passed = maxLocalJitter < 0.03f && yawError < 0.5f;
            TraversalPlayModeCommandLine.Complete(
                passed,
                "camera lifecycle; max local jitter " + maxLocalJitter.ToString("F4")
                + " m, yaw error " + yawError.ToString("F3") + " deg"
            );
            enabled = false;
        }
        catch (Exception ex)
        {
            TraversalPlayModeCommandLine.Complete(false, ex.ToString());
            enabled = false;
        }
    }
}
#endif
