using UnityEngine;
using Unity.MLAgents;
using System.Collections;

public class BattleArena : MonoBehaviour
{
    [Header("Agents")]
    public BattleBotAgent agentA;
    public BattleBotAgent agentB;

    [Header("Arena Root")]
    [SerializeField] private Transform arenaRoot;

    [Header("Floor")]
    [SerializeField] private Collider floorCollider;
    [SerializeField] private LayerMask floorMask = ~0;
    [SerializeField] private float spawnSkin = 0.02f;

    [Header("Arena Visuals")]
    public MeshRenderer floorRenderer;
    public Material defaultFloorMaterial;
    public float winFlashDuration = 1.0f;

    [Header("Episode Settings")]
    [Tooltip("Hard timeout in environment steps (physics steps). Set to 0 to disable.")]
    public int maxEnvironmentSteps = 5000;

    [Header("Rewards (Self-Play Friendly)")]
    public float winReward = 1.0f;
    public float loseReward = -1.0f;
    public float balloonPopReward = 0.1f;
    public float balloonPopPenalty = -0.1f;

    public bool enforceOutcomeSignForElo = true;
    public float minWinnerFinalReward = 0.1f;
    public float maxLoserFinalReward = -0.1f;

    [Header("Spawn Area")]
    [SerializeField] private float arenaHalfSize = 10f;
    [SerializeField] private float wallPadding = 0.5f;
    [SerializeField] private float spawnAreaFracDefault = 1.0f;
    [SerializeField] private int spawnTries = 80;

    [Header("Spawn Collision / Separation")]
    [Tooltip("LayerMask for things that should block spawns (e.g., walls, props). Prefer NOT to include agents themselves.")]
    [SerializeField] private LayerMask spawnBlockers;

    [Tooltip("If > 0, overrides auto radius for spawn overlap checks. If 0, auto-computed from agent collider bounds.")]
    [SerializeField] private float agentRadiusOverride = 0f;

    [Tooltip("Extra margin added on top of (radiusA + radiusB).")]
    [SerializeField] private float separationMargin = 0.15f;

    private SimpleMultiAgentGroup agentGroup;
    private bool matchIsEnding = false;
    private int envStepCount = 0;

    public bool MatchIsEnding => matchIsEnding;

    void Awake()
    {
        if (arenaRoot == null) arenaRoot = transform;

        if (floorCollider == null && arenaRoot != null)
        {
            var floorT = arenaRoot.Find("Floor");
            if (floorT != null) floorCollider = floorT.GetComponent<Collider>();
        }
    }

    void Start()
    {
        agentGroup = new SimpleMultiAgentGroup();
        agentGroup.RegisterAgent(agentA);
        agentGroup.RegisterAgent(agentB);

        if (floorRenderer != null) floorRenderer.material = defaultFloorMaterial;

        ResetScene();
    }

    void FixedUpdate()
    {
        if (matchIsEnding) return;

        if (maxEnvironmentSteps > 0)
        {
            envStepCount++;
            if (envStepCount >= maxEnvironmentSteps)
            {
                EndMatchDraw();
            }
        }
    }

    public void OnBalloonPopped(BattleBotAgent victim, BattleBotAgent attacker)
    {
        if (matchIsEnding) return;

        attacker.AddReward(balloonPopReward);
        victim.AddReward(balloonPopPenalty);

        if (victim.GetActiveBalloonCount() <= 0)
        {
            EndMatchWin(attacker, victim);
        }
    }

    private void EndMatchWin(BattleBotAgent winner, BattleBotAgent loser)
    {
        if (matchIsEnding) return;
        matchIsEnding = true;

        winner.AddReward(winReward);
        loser.AddReward(loseReward);

        if (enforceOutcomeSignForElo)
        {
            EnforceOutcomeSigns(winner, loser);
        }

        if (floorRenderer != null && winner.teamMaterial != null)
        {
            StartCoroutine(FlashFloor(winner.teamMaterial));
        }

        agentGroup.EndGroupEpisode();
        ResetScene();
        StartCoroutine(ClearMatchEndingNextFrame());
    }

    private void EndMatchDraw()
    {
        if (matchIsEnding) return;
        matchIsEnding = true;

        agentA.SetReward(0f);
        agentB.SetReward(0f);

        agentGroup.EndGroupEpisode();
        ResetScene();
        StartCoroutine(ClearMatchEndingNextFrame());
    }

    void EnforceOutcomeSigns(BattleBotAgent winner, BattleBotAgent loser)
    {
        float winnerCum = winner.GetCumulativeReward();
        if (winnerCum <= 0f)
            winner.AddReward(minWinnerFinalReward - winnerCum);

        float loserCum = loser.GetCumulativeReward();
        if (loserCum >= 0f)
            loser.AddReward(maxLoserFinalReward - loserCum);
    }

    IEnumerator FlashFloor(Material winnerMat)
    {
        if (floorRenderer != null) floorRenderer.material = winnerMat;
        yield return new WaitForSeconds(winFlashDuration);
        if (floorRenderer != null) floorRenderer.material = defaultFloorMaterial;
    }

    IEnumerator ClearMatchEndingNextFrame()
    {
        yield return null;
        matchIsEnding = false;
    }

    void ResetScene()
    {
        envStepCount = 0;

        agentA.ResetAgent();
        agentB.ResetAgent();

        PlaceAgentsNonOverlapping();
    }

    Vector3 SampleSpawnLocal()
    {
        float spawnAreaFrac = Academy.Instance.EnvironmentParameters.GetWithDefault("spawn_area_frac", spawnAreaFracDefault);
        float limit = (arenaHalfSize - wallPadding) * spawnAreaFrac;

        float x = Random.Range(-limit, limit);
        float z = Random.Range(-limit, limit);

        return new Vector3(x, 0f, z);
    }

    Vector3 LocalToWorld(Vector3 localPos) => arenaRoot.TransformPoint(localPos);

    Quaternion YawLocalToWorld(float yawDeg) => arenaRoot.rotation * Quaternion.Euler(0f, yawDeg, 0f);

    Collider GetPrimaryNonTriggerCollider(BattleBotAgent a)
    {
        if (a == null) return null;
        var cols = a.GetComponentsInChildren<Collider>(true);
        foreach (var c in cols)
        {
            if (c != null && !c.isTrigger) return c;
        }
        return null;
    }

    float GetAgentSpawnRadius(BattleBotAgent a)
    {
        if (agentRadiusOverride > 0f) return agentRadiusOverride;

        var col = GetPrimaryNonTriggerCollider(a);
        if (col == null) return 0.5f;

        var e = col.bounds.extents;
        return Mathf.Max(e.x, e.z);
    }

    float GetAgentHalfHeight(BattleBotAgent a)
    {
        var col = GetPrimaryNonTriggerCollider(a);
        if (col == null) return 0.5f;
        return col.bounds.extents.y;
    }

    float GetFloorTopYAtXZ(Vector3 worldXZ)
    {
        if (floorCollider != null)
            return floorCollider.bounds.max.y;

        float rayStartY = arenaRoot.position.y + 10f;
        var origin = new Vector3(worldXZ.x, rayStartY, worldXZ.z);

        if (Physics.Raycast(origin, Vector3.down, out var hit, 50f, floorMask, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        return arenaRoot.position.y;
    }

    bool IsFreeWorld(Vector3 worldPos, float radius)
    {
        var hits = Physics.OverlapSphere(worldPos, radius, spawnBlockers, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h == null) continue;
            if (agentA != null && h.transform.IsChildOf(agentA.transform)) continue;
            if (agentB != null && h.transform.IsChildOf(agentB.transform)) continue;
            return false;
        }
        return true;
    }

    Vector3 FindSpawnLocal(BattleBotAgent self, float selfRadius, Vector3? otherLocal, float requiredSeparation)
    {
        float halfH = Mathf.Max(0.05f, GetAgentHalfHeight(self));

        for (int t = 0; t < spawnTries; t++)
        {
            var p = SampleSpawnLocal();

            if (otherLocal.HasValue && Vector3.Distance(p, otherLocal.Value) < requiredSeparation)
                continue;

            var world = LocalToWorld(p);
            float floorTop = GetFloorTopYAtXZ(world);
            world.y = floorTop + halfH + spawnSkin;

            if (!IsFreeWorld(world, selfRadius))
                continue;

            return p;
        }

        return SampleSpawnLocal();
    }

    void PlaceAgentsNonOverlapping()
    {
        float rA = GetAgentSpawnRadius(agentA);
        float rB = GetAgentSpawnRadius(agentB);
        float required = rA + rB + separationMargin;

        var pALocal = FindSpawnLocal(agentA, rA, null, 0f);
        var pBLocal = FindSpawnLocal(agentB, rB, pALocal, required);

        TeleportAgent(agentA, LocalToWorld(pALocal), YawLocalToWorld(Random.Range(0f, 360f)));
        TeleportAgent(agentB, LocalToWorld(pBLocal), YawLocalToWorld(Random.Range(0f, 360f)));

        Physics.SyncTransforms();
    }

    void TeleportAgent(BattleBotAgent a, Vector3 worldPos, Quaternion worldRot)
    {
        if (a == null) return;

        float yaw = worldRot.eulerAngles.y;
        worldRot = Quaternion.Euler(0f, yaw, 0f);

        float halfH = Mathf.Max(0.05f, GetAgentHalfHeight(a));
        float floorTop = GetFloorTopYAtXZ(worldPos);
        worldPos.y = floorTop + halfH + spawnSkin;

        var rb = a.GetComponent<Rigidbody>();
        if (rb == null)
        {
            a.transform.SetPositionAndRotation(worldPos, worldRot);
            return;
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = worldPos;
        rb.rotation = worldRot;
        rb.WakeUp();
    }
}
