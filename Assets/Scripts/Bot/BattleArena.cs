using UnityEngine;
using Unity.MLAgents;
using System.Collections;
using System.Collections.Generic;

public class BattleArena : MonoBehaviour
{
    [Header("Teams")]
    public List<BattleBotAgent> team0Agents; // Blue Team
    public List<BattleBotAgent> team1Agents; // Red Team

    [Header("Arena Root")]
    [SerializeField] private Transform arenaRoot;

    [Header("Floor")]
    [SerializeField] private List<Collider> floorColliders = new List<Collider>();
    [SerializeField] private LayerMask floorMask = ~0;
    [SerializeField] private float spawnSkin = 0.02f;

    [Header("Arena Visuals")]
    public List<MeshRenderer> floorRenderers = new List<MeshRenderer>();
    public Material defaultFloorMaterial;
    public float winFlashDuration = 1.0f;

    [Header("Elements")]
    public List<BalloonSpawner> balloonSpawners;

    [Header("Episode Settings")]
    [Tooltip("Hard timeout in environment steps (physics steps). Set to 0 to disable.")]
    public int maxEnvironmentSteps = 5000;

    [Header("Rewards (MA-POCA)")]
    public float winGroupReward = 1.0f;   // Given to the whole winning team
    public float loseGroupReward = -1.0f; // Given to the whole losing team
    public float balloonPopReward = 0.1f; // Individual reward for popping
    public float balloonPopPenalty = -0.1f; // Individual penalty for getting popped

    [Header("Spawn Area")]
    public float arenaHalfSize = 10f; // Public for agents to read
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

    [Header("Battle Royale Wind")]
    [SerializeField] private bool enableWind = true;
    [SerializeField] private bool enableDebugCurrentWindGizmos = true;
    [SerializeField] private bool enableDebugMaxMinWindGizmos = false;
    [SerializeField] private float maxWindForce = 40f;
    [SerializeField] private AnimationCurve timeRamp = AnimationCurve.Linear(0, 0, 1, 1); // linear growth by default
    [SerializeField] private float minBattleRoyaleRadius = 5f; // dead zone near center, where no wind is applied
    [SerializeField] private float maxBattleRoyaleRadius = 25f; // starting radius at beginning of match
    [SerializeField] private Transform shrinkingZoneVisual;
    private float currentBattleRoyaleRadius; // current distance from center where wind is applying, depending on time and min/max radius specified

    private SimpleMultiAgentGroup groupTeam0;
    private SimpleMultiAgentGroup groupTeam1;
    private bool matchIsEnding = false;
    private int envStepCount = 0;

    [Header("Time")]
    public float matchDuration = 30f;
    private float timer = 0f;
    private float timerElapsedFactor = 0f;
    public bool MatchIsEnding => matchIsEnding;

    void Awake()
    {
        if (arenaRoot == null) arenaRoot = transform;

        // Procura automaticamente todos os filhos que contenham "Floor" no nome
        if (arenaRoot != null && (floorColliders.Count == 0 || floorRenderers.Count == 0))
        {
            foreach (Transform child in arenaRoot.GetComponentsInChildren<Transform>())
            {
                if (child.name.Contains("Floor"))
                {
                    Collider col = child.GetComponent<Collider>();
                    if (col != null && !floorColliders.Contains(col)) floorColliders.Add(col);

                    MeshRenderer rend = child.GetComponent<MeshRenderer>();
                    if (rend != null && !floorRenderers.Contains(rend)) floorRenderers.Add(rend);
                }
            }
        }

        //setup shrinking border visual
        if (enableDebugCurrentWindGizmos && shrinkingZoneVisual != null) {
            shrinkingZoneVisual.gameObject.SetActive(true);
        }
    }

    void Start()
    {
        // Initialize Groups
        groupTeam0 = new SimpleMultiAgentGroup();
        groupTeam1 = new SimpleMultiAgentGroup();

        // Register Team 0 (Blue)
        foreach (var agent in team0Agents)
        {
            if (agent != null)
            {
                agent.teamId = 0;
                agent.arena = this;
                groupTeam0.RegisterAgent(agent);
            }
        }

        // Register Team 1 (Red)
        foreach (var agent in team1Agents)
        {
            if (agent != null)
            {
                agent.teamId = 1;
                agent.arena = this;
                groupTeam1.RegisterAgent(agent);
            }
        }

        ResetFloorMaterial();
        ResetScene();
    }

    void FixedUpdate()
    {
        if (matchIsEnding) return;

        timer += Time.fixedDeltaTime;

        if (enableWind)
        {
            ApplyBattleRoyaleWind();
        }
        
        if (timer >= matchDuration)
        {
            DetermineWinnerByBalloons();
        }
    }

    private void ResetFloorMaterial()
    {
        foreach (var rend in floorRenderers)
        {
            if (rend != null) rend.material = defaultFloorMaterial;
        }
    }

    private int GetTotalTeamBalloons(List<BattleBotAgent> team)
    {
        int count = 0;
        foreach (var agent in team)
        {
            if (agent != null)
                count += agent.GetActiveBalloonCount();
        }
        return count;
    }

    private void DetermineWinnerByBalloons()
    {
        int team0Balloons = GetTotalTeamBalloons(team0Agents);
        int team1Balloons = GetTotalTeamBalloons(team1Agents);

        if (team0Balloons > team1Balloons)
        {
            EndMatchWin(0);
        }
        else if (team1Balloons > team0Balloons)
        {
            EndMatchWin(1);
        }
        else
        {
            EndMatchDraw();
        }
    }

    public void OnBalloonPopped(BattleBotAgent victim, BattleBotAgent attacker)
    {
        if (matchIsEnding) return;

        var statsRecorder = Academy.Instance.StatsRecorder;

            if (attacker != null && victim != null)
        {
            if (attacker.teamId == victim.teamId)
            {
                attacker.AddReward(-0.5f);

                if (attacker.teamId == 0)
                {
                    statsRecorder.Add("PopedTeamMemberBallons/Red", 1, StatAggregationMethod.Sum);
                    statsRecorder.Add("PopedTeamMemberBallons/Blue", 0, StatAggregationMethod.Sum);
                }
                else
                {
                    statsRecorder.Add("PopedTeamMemberBallons/Red", 0, StatAggregationMethod.Sum);
                    statsRecorder.Add("PopedTeamMemberBallons/Blue", 1, StatAggregationMethod.Sum);
                }
            }
            else
            {
                attacker.AddReward(balloonPopReward);
                victim.AddReward(balloonPopPenalty);

                if (attacker.teamId == 0)
                {
                    statsRecorder.Add("PopedOtherTeamBallons/Red", 1, StatAggregationMethod.Sum);
                    statsRecorder.Add("PopedOtherTeamBallons/Blue", 0, StatAggregationMethod.Sum);
                }
                else
                {
                    statsRecorder.Add("PopedOtherTeamBallons/Red", 0, StatAggregationMethod.Sum);
                    statsRecorder.Add("PopedOtherTeamBallons/Blue", 1, StatAggregationMethod.Sum);
                }
            }
        }
    }

    private void EndMatchWin(int winningTeamId)
    {
        if (matchIsEnding) return;
        matchIsEnding = true;

        Material winnerMat = null;

        var statsRecorder = Academy.Instance.StatsRecorder;

        float totalBalloonsT0 = GetTotalTeamBalloons(team0Agents);
        float avgBalloonsT0 = team0Agents.Count > 0 ? totalBalloonsT0 / team0Agents.Count : 0f;

        float totalBalloonsT1 = GetTotalTeamBalloons(team1Agents);
        float avgBalloonsT1 = team1Agents.Count > 0 ? totalBalloonsT1 / team1Agents.Count : 0f;

        statsRecorder.Add("FinalAverageBalloons/Blue", avgBalloonsT0, StatAggregationMethod.Average);
        statsRecorder.Add("FinalAverageBalloons/Red", avgBalloonsT1, StatAggregationMethod.Average);

        if (winningTeamId == 0)
        {
            groupTeam0.AddGroupReward(winGroupReward);
            groupTeam1.AddGroupReward(loseGroupReward);
            if (team0Agents.Count > 0 && team0Agents[0] != null)
                winnerMat = team0Agents[0].teamMaterial;

            statsRecorder.Add("Victories/Red", 1, StatAggregationMethod.Sum);
            statsRecorder.Add("Victories/Blue", 0, StatAggregationMethod.Sum);
        }
        else
        {
            groupTeam1.AddGroupReward(winGroupReward);
            groupTeam0.AddGroupReward(loseGroupReward);
            if (team1Agents.Count > 0 && team1Agents[0] != null)
                winnerMat = team1Agents[0].teamMaterial;

            statsRecorder.Add("Victories/Red", 0, StatAggregationMethod.Sum);
            statsRecorder.Add("Victories/Blue", 1, StatAggregationMethod.Sum);
        }

        if (winnerMat != null) StartCoroutine(FlashFloor(winnerMat));

        groupTeam0.EndGroupEpisode();
        groupTeam1.EndGroupEpisode();

        ResetScene();
        StartCoroutine(ClearMatchEndingNextFrame());
    }

    private void EndMatchDraw()
    {
        if (matchIsEnding) return;
        matchIsEnding = true;

        groupTeam0.AddGroupReward(-0.1f);
        groupTeam1.AddGroupReward(-0.1f);

        groupTeam0.EndGroupEpisode();
        groupTeam1.EndGroupEpisode();

        ResetScene();
        StartCoroutine(ClearMatchEndingNextFrame());
    }

    IEnumerator FlashFloor(Material winnerMat)
    {
        if (floorRenderers.Count > 0 && winnerMat != null)
        {
            foreach (var rend in floorRenderers)
            {
                if (rend != null) rend.material = winnerMat;
            }

            yield return new WaitForSeconds(winFlashDuration);
            ResetFloorMaterial();
        }
    }

    IEnumerator ClearMatchEndingNextFrame()
    {
        yield return null;
        matchIsEnding = false;
    }

    void ResetScene()
    {
        envStepCount = 0;
        timer = 0f;
        timerElapsedFactor = 0f;

        List<BattleBotAgent> allAgents = new List<BattleBotAgent>();
        if (team0Agents != null) allAgents.AddRange(team0Agents);
        if (team1Agents != null) allAgents.AddRange(team1Agents);

        PlaceAgentsNonOverlapping(allAgents);
    }

    void PlaceAgentsNonOverlapping(List<BattleBotAgent> agents)
    {
        List<Vector3> placedPositions = new List<Vector3>();

        foreach (var agent in agents)
        {
            if (agent == null) continue;

            float r = GetAgentSpawnRadius(agent);
            Vector3 posLocal = FindSpawnLocal(agent, r, placedPositions, separationMargin);
            placedPositions.Add(posLocal);

            TeleportAgent(agent, LocalToWorld(posLocal), YawLocalToWorld(Random.Range(0f, 360f)));
        }

        Physics.SyncTransforms();
    }

    Vector3 FindSpawnLocal(BattleBotAgent self, float selfRadius, List<Vector3> existingPositions, float requiredSeparation)
    {
        float halfH = Mathf.Max(0.05f, GetAgentHalfHeight(self));

        for (int t = 0; t < spawnTries; t++)
        {
            var p = SampleSpawnLocal();

            bool collision = false;
            foreach (var existing in existingPositions)
            {
                if (Vector3.Distance(p, existing) < (selfRadius * 2f + requiredSeparation))
                {
                    collision = true;
                    break;
                }
            }
            if (collision) continue;

            var world = LocalToWorld(p);
            float floorTop = GetFloorTopYAtXZ(world);
            world.y = floorTop + halfH + spawnSkin;

            if (!IsFreeWorld(world, selfRadius))
                continue;

            return p;
        }

        return SampleSpawnLocal();
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
        if (floorColliders.Count > 0)
        {
            float highestY = -Mathf.Infinity;
            bool hitAny = false;
            foreach (var col in floorColliders)
            {
                if (col != null)
                {
                    if (col.bounds.Contains(new Vector3(worldXZ.x, col.bounds.center.y, worldXZ.z)))
                    {
                        if (col.bounds.max.y > highestY)
                        {
                            highestY = col.bounds.max.y;
                            hitAny = true;
                        }
                    }
                }
            }
            if (hitAny) return highestY;
        }

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
            if (h.GetComponentInParent<BattleBotAgent>() != null) continue;
            return false;
        }
        return true;
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

    private void ApplyBattleRoyaleWind()
    {
        timerElapsedFactor = Mathf.Clamp01(timer / matchDuration);
        float timeFactor = timeRamp.Evaluate(timerElapsedFactor);

        currentBattleRoyaleRadius = Mathf.Lerp(maxBattleRoyaleRadius, minBattleRoyaleRadius, timeFactor);

        ApplyWindToTeam(team0Agents, timeFactor);
        ApplyWindToTeam(team1Agents, timeFactor);

        UpdateShrinkingZoneVisual();
    }

    private void ApplyWindToTeam(List<BattleBotAgent> team, float timeFactor)
    {
        foreach (var agent in team)
        {
            if (agent == null) continue;

            var rb = agent.GetComponent<Rigidbody>();
            if (rb == null) continue;

            // get position of player relative to arena (arena local space)
            Vector3 localPos = arenaRoot.InverseTransformPoint(rb.position);
            Vector2 planar = new Vector2(localPos.x, localPos.z);

            // if inside safe zone, no wind applied
            float dist = planar.magnitude;
            if (dist < currentBattleRoyaleRadius) continue; 

            // # apply wind force towards center depending on distance outside safe zone
            // constant force regardless of how far outside the zone is
            // float distFactor = Mathf.Clamp01(dist / maxBattleRoyaleRadius);
            // Vector3 dirToCenter = -new Vector3(planar.x, 0f, planar.y).normalized;

            // apply wind force that increases the further outside the zone the player is
            float distOutside = Mathf.Max(0f, dist - currentBattleRoyaleRadius);
            if (distOutside <= 0f) continue;
            float distFactor = Mathf.Clamp01(distOutside / (maxBattleRoyaleRadius - currentBattleRoyaleRadius));
            Vector3 dirToCenter = -new Vector3(planar.x, 0f, planar.y).normalized;

            Vector3 force = dirToCenter * maxWindForce * distFactor * timeFactor;
            rb.AddForce(force, ForceMode.VelocityChange);
        }
    }

    // update the shrinking zone visual representation
    private void UpdateShrinkingZoneVisual()
    {
        if (shrinkingZoneVisual == null || !enableDebugCurrentWindGizmos) return;

        // Assuming unit circle mesh of diameter 1
        float diameter = currentBattleRoyaleRadius * 2f;
        shrinkingZoneVisual.localScale = new Vector3(diameter, shrinkingZoneVisual.localScale.y, diameter);
    }

    public void OnDrawGizmos()
    {
       if (enableDebugMaxMinWindGizmos && enableWind)
       {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(arenaRoot.position, maxBattleRoyaleRadius);
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(arenaRoot.position, minBattleRoyaleRadius);
       }
    }

    public float getTimerElapsedFactor()
    {
        return timerElapsedFactor;
    }
}