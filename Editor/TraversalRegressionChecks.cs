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
        Check("Walk-to-idle animation finishes its transition", IdleTransition);
        Check("All six clips animate one shared Humanoid rig", SharedRigAnimations);
        report.AppendLine("Failures: " + failures.Count);
        return report.ToString();
    }

    static void Check(string name, Action test)
    {
        Scene original = SceneManager.GetActiveScene();
        testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
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
        motor.AttachToWall(hit, rb, go.transform);
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
        player.GetComponent<PlayerClimbMotor>().AttachToWall(hit, player.GetComponent<Rigidbody>(), player.transform);
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
        Vector3 start = motor.transform.position;
        Vector3 right = motor.transform.right;
        motor.MoveOnWall(motor.transform, Vector2.right);
        Require(Vector3.Dot(motor.transform.position - start, right) > 0f, "Right input moves left");
    }

    static void WallDiagonal()
    {
        var motor = Climber(out _);
        Vector3 start = motor.transform.position;
        motor.MoveOnWall(motor.transform, Vector2.one);
        float maximum = (float)Get(motor, "climbSpeed") * Time.fixedDeltaTime;
        Require((motor.transform.position - start).magnitude <= maximum + 0.002f, "Diagonal speed exceeds straight speed");
    }

    static PlayerClimbMotor Mount(out GameObject platform)
    {
        var motor = Climber(out platform);
        // Top is y=2; start just below it, facing +Z.
        motor.transform.position = Origin + new Vector3(0f, 1.5f, -0.1f);
        Require(motor.TryStartMount(motor.transform, motor.transform, default, default, 1 << 3), "Mount setup failed");
        return motor;
    }

    static void MovingMount()
    {
        var motor = Mount(out var platform);
        Set(motor, "mountStartTime", Time.time - 2f);
        motor.UpdateMount(motor.transform, out _);
        Vector3 localLanding = platform.transform.InverseTransformPoint(motor.transform.position);
        platform.transform.position += new Vector3(3f, 1f, 2f);
        platform.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
        motor.UpdateMount(motor.transform, out _);
        Near(motor.transform.position, platform.transform.TransformPoint(localLanding), "Landing target stayed in world space");
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
        reader.SendMessage("OnDisable", SendMessageOptions.DontRequireReceiver);
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
        probe.ApplyGroundDelta(rb);
        Near(rb.position, floor.transform.TransformPoint(local), "Rotation carry is incorrect");
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
        probe.ApplyGroundDelta(rb, false);
        floor.transform.position += Vector3.right;
        probe.ApplyGroundDelta(rb, false);
        Near(rb.position, Origin, "Body moved before landing");
        floor.transform.position += Vector3.right;
        probe.ApplyGroundDelta(rb);
        Near(rb.position, Origin + Vector3.right, "Landing replayed old platform displacement");
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

    static void IdleTransition()
    {
        var player = Player();
        var bridge = player.GetComponent<PlayerAnimationBridge>();
        var animator = (Animator)Get(bridge, "animator");
        animator.Rebind();
        animator.Play("Walk", 0, 0f);
        animator.Update(0f);
        bridge.SetMovementState(true, 0f, true, false, false);
        animator.Update(0.1f);
        bridge.SetMovementState(true, 0f, false, false, false);
        for (int i = 0; i < 30; i++)
        {
            Invoke(bridge, "Update");
            animator.Update(0.02f);
        }
        Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") && !animator.IsInTransition(0), "Repeated crossfade prevents settling in Idle");
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

    static object Get(object target, string field) => target.GetType().GetField(field, Fields).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    static void Invoke(object target, string method) => target.GetType().GetMethod(method, Fields).Invoke(target, null);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Near(Vector3 actual, Vector3 expected, string message) => Require(Vector3.Distance(actual, expected) < 0.01f, message + ": " + actual + " vs " + expected);
}
