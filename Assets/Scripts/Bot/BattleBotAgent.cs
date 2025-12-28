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

    [Header("Visual Settings")]
    public Material teamMaterial;
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

        rBody.useGravity = false;
        rBody.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
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
            rBody.linearVelocity = Vector3.zero;
            rBody.angularVelocity = Vector3.zero;

            float yaw = rBody.rotation.eulerAngles.y;
            rBody.rotation = Quaternion.Euler(0f, yaw, 0f);
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
            foreach (var balloon in myBalloons)
            {
                if (balloon != null) balloon.ResetBalloon();
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
        rBody.angularVelocity = Vector3.zero;

        float yawDelta = m_TurnInput * turnSpeed * Time.fixedDeltaTime;
        Quaternion newRot = rBody.rotation * Quaternion.Euler(0f, yawDelta, 0f);
        rBody.MoveRotation(newRot);

        float currentMaxSpeed = isBoosting ? moveSpeed * boostMultiplier : moveSpeed;
        Vector3 forward = newRot * Vector3.forward;
        Vector3 planarTarget = forward * (m_MoveInput * currentMaxSpeed);

        Vector3 v = rBody.linearVelocity;
        Vector3 vPlanar = new Vector3(v.x, 0f, v.z);
        Vector3 vTarget = new Vector3(planarTarget.x, 0f, planarTarget.z);

        Vector3 vNew = Vector3.MoveTowards(vPlanar, vTarget, acceleration * Time.fixedDeltaTime);
        rBody.linearVelocity = new Vector3(vNew.x, 0f, vNew.z);
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
        var localVel = transform.InverseTransformDirection(rBody != null ? rBody.linearVelocity : Vector3.zero);
        sensor.AddObservation(localVel.x);
        sensor.AddObservation(localVel.z);
        sensor.AddObservation(canBoost ? 1.0f : 0.0f);
        sensor.AddObservation(isBoosting ? 1.0f : 0.0f);
        sensor.AddObservation(GetActiveBalloonCount() / 3.0f);
    }

    public bool RestoreBalloon()
    {
        if (myBalloons == null) return false;
        foreach (var balloon in myBalloons)
        {
            if (balloon != null && !balloon.gameObject.activeSelf)
            {
                balloon.ResetBalloon();
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
