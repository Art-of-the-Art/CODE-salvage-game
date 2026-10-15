using UnityEngine;

// One clock drives both physical speed and the ground-animation blend.
public sealed class GroundGaitTransition
{
    public int Mode { get; private set; }
    float walkCooldown;
    float runCooldown;
    float elapsed;
    float startSpeed;
    float targetSpeed;
    float transitionDuration = 0.4f;
    bool wasMoving;
    bool queuedWalkPress;


    public float Step(bool moving, bool sprint, float measuredSpeed, float walkSpeed,
        float runSpeed, float duration, float cooldownDuration, float deltaTime)
    {
        walkCooldown = Mathf.Max(0f, walkCooldown - deltaTime);
        runCooldown = Mathf.Max(0f, runCooldown - deltaTime);
        bool walkPressedThisStep = moving && !wasMoving;
        wasMoving = moving;

        // Remember at most one rejected W press. Further spam cannot restart
        // the cooldown; the remembered press is replayed when that clock ends.
        if (walkPressedThisStep && Mode == 0 && walkCooldown > 0f)
            queuedWalkPress = true;

        int requested = moving ? (sprint ? 2 : 1) : 0;
        bool replayQueuedWalkPress = requested == 0 && queuedWalkPress && walkCooldown <= 0f;
        if (replayQueuedWalkPress)
            requested = 1;
        float cooldown = requested == 2 ? runCooldown : walkCooldown;
        // Release always works. A held request can enter when the cooldown expires.
        if (requested != Mode && (requested < Mode || cooldown <= 0f))
        {
            // Starting Walk must not consume the first Run request.
            if (requested > Mathf.Max(0, Mode))
            {
                if (requested == 2) runCooldown = cooldownDuration;
                else walkCooldown = cooldownDuration;
            }
            Mode = requested;
            startSpeed = measuredSpeed;
            targetSpeed = Mode == 2 ? runSpeed : Mode == 1 ? walkSpeed : 0f;
            elapsed = 0f;
            transitionDuration = Mathf.Max(0.01f, duration);
            if (replayQueuedWalkPress)
            {
                // A queued tap produces one visible physics step even though
                // the key is no longer held when the cooldown expires.
                elapsed = transitionDuration;
                queuedWalkPress = false;
            }

        }
        elapsed += deltaTime;
        float time = Mathf.Clamp01(elapsed / transitionDuration);
        // Quintic S-curve has zero acceleration at both ends and reaches the target exactly.
        float blend = time * time * time * (time * (time * 6f - 15f) + 10f);
        return Mathf.Lerp(startSpeed, targetSpeed, blend);
    }

    public void Reset(float speed = 0f)
    {
        Mode = -1;
        walkCooldown = runCooldown = elapsed = 0f;
        startSpeed = targetSpeed = speed;
        wasMoving = false;
        queuedWalkPress = false;

    }
}
