# Shared character and wall-facing fix — 2026-09-17

Canonical model documentation: `Assets/Characters/Player/README.md` (relative to Unity project root).

PlayerMovement now asks PlayerAnimationBridge.FaceClimbSurface to orient PlayerVisualRoot on entry to Climbing, after moving-wall compensation and after a new wall normal is sampled. Previously walking left a local yaw on the visual child, while climbing rotated only its parent. Two editor checks reproduce the old issue and pass after the fix.

PlayerAnimationBridge resolves its Animator from the common visual prefab at startup and assigns its serialized animationController. This supports replacing the child Humanoid model without preserving a reference to the removed Animator. No procedural animation or IK was introduced.

Migration outside this nested code repository:
- Assets/Characters/Player/PlayerVisual.prefab: shared model instance, now connected in Main.
- Assets/Characters/Player/Model/PlayerModel.fbx: moved from Fast Run.fbx, preserving GUID; Import Animation disabled.
- Assets/Animations/Player: standalone .anim clips; six working clip hashes unchanged.
- SourceAssets/PlayerAnimations/Mixamo: preserved original FBX sources, no longer imported by Unity.
- Assets/Editor/PlayerAnimationUnifier.cs: validates/assigns existing .anim files and can extract a selected new Humanoid animation.
- Updated Main.unity and the animation documentation.

Checks: 16/16 editor scenarios pass. SharedRigAnimations checks all six controller states, valid Humanoid animation, bone pose changes, the same mesh instance, lack of FBX dependencies for .anim and lack of AnimationClip subassets in the canonical model. Play Mode confirms Climbing and forward/negative-normal dot 0.9999999 after a moving wall rotates 45 degrees. Console after the run has no errors or warnings. Screenshot: AuditBackups/2026-09-17-shared-model-validation/climb-facing-wall.png.

Backup: AuditBackups/2026-09-17-before-shared-player-model. No commit created. Changes to model assets, scene and tools outside Assets/CODE-salvage-game must be versioned/backed up alongside this nested repository; a commit to the code repository alone does not contain the asset migration.
