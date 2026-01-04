using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class BattleBotAgent : Agent
{
    [Header("Game References")]
    public BattleArena arena;
    public List<BalloonUnit> myBalloons;

    [Header("Team Settings")]
    public int teamId; // 0 = Blue, 1 = Red (Assigned by Arena)
    public Material teamMaterial;
    public Material deadMaterial; // Assign a grey material here
    public MeshRenderer bodyRenderer;

    [Header("Movement Settings")]
    public float moveSpeed = 10f;
    public float turnSpeed = 200f;
    public float acceleration = 50f;

    [Header("Boost Settings")]
    public float boostMultiplier = 2f;
    public float boostDuration = 2f;
    public float boostCooldown = 5f;

    [Header("Shaping Penalties (Capped)")]
    public float stepPenalty = -0.00005f;
    public float maxStepPenaltyPerEpisode = -0.2f;
    public float wallHitPenalty = -0.0001f;
    public float maxWallPenaltyPerEpisode = -0.05f;

    private Rigidbody rBody;
    private Color originalColor;

    private float m_MoveInput;
    private float m_TurnInput;
    private bool m_BoostInput;

    private bool isBoosting = false;
    private bool canBoost = true;
    private float m_BoostTimer = 0f;

    private float stepPenaltyAcc = 0f;
    private float wallPenaltyAcc = 0f;


    void Start()
    {
        if (bodyRenderer != null && teamMaterial != null)
        {
            bodyRenderer.material = teamMaterial;
            originalColor = teamMaterial.color;
        }
    }

    public override void Initialize()
    {
        rBody = GetComponent<Rigidbody>();
        if (rBody == null)
        {
            Debug.LogError($"{nameof(BattleBotAgent)} requires a Rigidbody.");
            return;
        }

        rBody.useGravity = true;
        rBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rBody.interpolation = RigidbodyInterpolation.None;
    }

    public override void OnEpisodeBegin()
    {
        ResetAgent();
    }

    public void ResetAgent()
    {

        if (rBody != null)
        {
            rBody.isKinematic = false; // Allow movement again
            rBody.linearVelocity = Vector3.zero;
            rBody.angularVelocity = Vector3.zero;
            // Rotation is handled by Arena placement
        }

        m_MoveInput = 0f;
        m_TurnInput = 0f;
        m_BoostInput = false;

        isBoosting = false;
        canBoost = true;
        m_BoostTimer = 0f;

        stepPenaltyAcc = 0f;
        wallPenaltyAcc = 0f;

        if (bodyRenderer != null && teamMaterial != null)
        {
            bodyRenderer.material = teamMaterial;
            bodyRenderer.material.color = originalColor;
        }

        if (myBalloons != null)
        {
            for (int i = 0; i < myBalloons.Count; i++)
            {
                if (i < 3) 
                {
                    myBalloons[i].ResetBalloon();
                }
                else 
                {
                    myBalloons[i].Pop();
                }
            }
        }
    }

    void FixedUpdate()
    {
        if (arena != null && arena.MatchIsEnding) return;
        if (rBody == null) return;

        HandleBoostLogic();
        HandleMovementPhysics();
    }

    private void HandleBoostLogic()
    {
        if (m_BoostTimer > 0f)
        {
            m_BoostTimer -= Time.fixedDeltaTime;
        }
        else if (isBoosting)
        {
            isBoosting = false;
            m_BoostTimer = boostCooldown;
            if (bodyRenderer != null) bodyRenderer.material.color = originalColor;
        }
        else if (!canBoost && m_BoostTimer <= 0f)
        {
            canBoost = true;
        }

        if (m_BoostInput && canBoost)
        {
            isBoosting = true;
            canBoost = false;
            m_BoostTimer = boostDuration;
            m_BoostInput = false;
            if (bodyRenderer != null) bodyRenderer.material.color = Color.white;
        }
    }

    private void HandleMovementPhysics()
    {
        float yawDelta = m_TurnInput * turnSpeed * Time.fixedDeltaTime;
        Quaternion newRot = rBody.rotation * Quaternion.Euler(0f, yawDelta, 0f);
        rBody.MoveRotation(newRot);

        float currentMaxSpeed = isBoosting ? moveSpeed * boostMultiplier : moveSpeed;
        Vector3 forward = transform.forward;

        Vector3 targetVelocity = forward * (m_MoveInput * currentMaxSpeed);

        Vector3 currentVelocity = rBody.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);

        Vector3 velocityChange = targetVelocity - horizontalVelocity;

        rBody.AddForce(velocityChange * acceleration, ForceMode.Acceleration);
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        if (arena != null && arena.MatchIsEnding) return;

        ApplyCappedPenalty(stepPenalty, ref stepPenaltyAcc, maxStepPenaltyPerEpisode);

        m_MoveInput = actionBuffers.ContinuousActions[0];
        m_TurnInput = actionBuffers.ContinuousActions[1];
        m_BoostInput = actionBuffers.DiscreteActions[0] == 1;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. Local Velocity (2 floats)
        var localVel = transform.InverseTransformDirection(rBody != null ? rBody.linearVelocity : Vector3.zero);
        sensor.AddObservation(localVel.x);
        sensor.AddObservation(localVel.z);

        // 2. Boost Info (2 floats)
        sensor.AddObservation(canBoost ? 1.0f : 0.0f);
        sensor.AddObservation(isBoosting ? 1.0f : 0.0f);

        // 3. Health (1 float)
        sensor.AddObservation(GetActiveBalloonCount() / 3.0f);

        // 4. Arena Position (2 floats)
        Vector3 localPos = Vector3.zero;
        if (arena != null)
        {
            localPos = arena.transform.InverseTransformPoint(transform.position);
            float normFactor = arena.arenaHalfSize + 2f;
            sensor.AddObservation(localPos.x / normFactor);
            sensor.AddObservation(localPos.z / normFactor);
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }

        // 5. Orientation (2 floats)
        Vector3 localFwd = Vector3.forward;
        if (arena != null)
        {
            localFwd = arena.transform.InverseTransformDirection(transform.forward);
        }
        sensor.AddObservation(localFwd.x);
        sensor.AddObservation(localFwd.z);

        // 6. Team ID (2 floats - One Hot)
        sensor.AddOneHotObservation(teamId, 2);

        // 7. Health Balloons (N * 3 floats)
        if (arena != null && arena.balloonSpawners != null)
        {
            foreach (var spawner in arena.balloonSpawners)
            {
                if (spawner != null && spawner.ActiveBalloon != null)
                {
                    sensor.AddObservation(1.0f);
                    Vector3 toBalloon = transform.InverseTransformPoint(spawner.ActiveBalloon.transform.position);
                    sensor.AddObservation(toBalloon.x / 20.0f);
                    sensor.AddObservation(toBalloon.z / 20.0f);
                }
                else
                {
                    sensor.AddObservation(0.0f);
                    sensor.AddObservation(0.0f);
                    sensor.AddObservation(0.0f);
                }
            }
        }

        // factor of round progression (0 to 1) (1 float) - for wind scaling awareness
        if (arena != null)
        {
            sensor.AddObservation(arena.getTimerElapsedFactor());
        }
    }

    public bool RestoreBalloon(bool forceRestore = false)
    {
        if (myBalloons == null) return false;
        for (int i = 0; i < myBalloons.Count; i++)
        {
            var balloon = myBalloons[i];
            if (balloon != null && !balloon.gameObject.activeSelf){
                if (forceRestore)
                {
                    myBalloons[i].ResetBalloon();
                }
                else
                {
                    if (i < 3)
                    {
                        myBalloons[i].ResetBalloon();
                    }
                    else
                    {
                        myBalloons[i].Pop();
                    }
                }
                return true;
            }
        }
        return false;
    }


    public int GetActiveBalloonCount()
    {
        int count = 0;
        if (myBalloons == null) return count;
        foreach (var b in myBalloons)
        {
            if (b != null && b.gameObject.activeSelf) count++;
        }
        return count;
    }

    public void ApplyWallHitPenalty()
    {
        if (arena != null && arena.MatchIsEnding) return;
        ApplyCappedPenalty(wallHitPenalty, ref wallPenaltyAcc, maxWallPenaltyPerEpisode);
    }

    void OnCollisionStay(Collision collision)
    {
        if (arena != null && arena.MatchIsEnding) return;
        if (collision.gameObject.CompareTag("Wall"))
        {
            ApplyWallHitPenalty();
        }
    }

    private void ApplyCappedPenalty(float perEventPenalty, ref float accumulator, float cap)
    {
        if (perEventPenalty == 0f || cap == 0f) return;

        if (cap > 0f)
        {
            if (accumulator >= cap) return;
            float remaining = cap - accumulator;
            float delta = Mathf.Min(perEventPenalty, remaining);
            AddReward(delta);
            accumulator += delta;
            return;
        }

        if (accumulator <= cap) return;
        float remainingNeg = cap - accumulator;
        float deltaNeg = Mathf.Max(perEventPenalty, remainingNeg);
        AddReward(deltaNeg);
        accumulator += deltaNeg;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActionsOut = actionsOut.ContinuousActions;
        var discreteActionsOut = actionsOut.DiscreteActions;

        continuousActionsOut[0] = Input.GetAxis("Vertical");
        continuousActionsOut[1] = Input.GetAxis("Horizontal");
        discreteActionsOut[0] = Input.GetKey(KeyCode.Space) ? 1 : 0;
    }
}