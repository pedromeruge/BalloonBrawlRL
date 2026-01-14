using UnityEngine;
using Unity.MLAgents;
using System.Collections;
using System.Collections.Generic;
using System;
using Random=UnityEngine.Random;

public class BattleArena : MonoBehaviour
{
    [Header("Teams (auto-populated from object children)")]
    public List<List<BattleBotAgent>> teams = new List<List<BattleBotAgent>>();
    [SerializeField] private List<Material> teamMaterial = new List<Material>();  // configurable in inspector
    public List<String> teamNames = new List<String>();
    private List<SimpleMultiAgentGroup> agentGroups = new List<SimpleMultiAgentGroup>();

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
    [SerializeField] private GameObject obstaclesRoot; // Reference to the parent of all wall obstacles

    [Header("Episode Settings")]
    [Tooltip("Hard timeout in environment steps (physics steps). Set to 0 to disable.")]
    public int maxEnvironmentSteps = 5000;

    [Header("Rewards (MA-POCA)")]
    public float winGroupReward = 1.0f;   // Given to the whole winning team
    public float loseGroupReward = -1.0f; // Given to the whole losing team
    public float balloonPopReward = 0.2f; // [Encourage Combat] Increased from 0.1f
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
    private float currentSpawnAreaFrac = 1.0f;    
    private bool matchIsEnding = false;

    [Header("Time")]
    public float matchDuration = 45f;
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

        DiscoverTeams();
    }

    void Start()
    {

        AssignTeamProperties();

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

    private void DiscoverTeams()
    {
        teams.Clear();

        GameObject teamsObj = transform.Find("Teams")?.gameObject;
        foreach (Transform child in teamsObj.transform)
        {
            if (!child.CompareTag("Team") || !child.gameObject.activeSelf) continue;

            var teamAgents = new List<BattleBotAgent>();
            foreach (var agent in child.GetComponentsInChildren<BattleBotAgent>())
            {
                teamAgents.Add(agent);
            }

            if (teamAgents.Count > 0)
            
                teams.Add(teamAgents);

            // Debug.Log($"[BattleArena] Discovered {teams.Count} teams in arena '{teamsObj.name}'.");
        }

        agentGroups.Clear();
        for (int i = 0; i < teams.Count; i++)
        {
            agentGroups.Add(new SimpleMultiAgentGroup());
        }
    }

    private void AssignTeamProperties()
    {
        for (int t = 0; t < teams.Count; t++)
        {
            foreach (BattleBotAgent agent in teams[t])
            {
                agent.teamId = t;
                agent.arena = this;

                // assign team color if provided
                if (teamMaterial.Count > t)
                {
                    agent.setTeamMaterial(teamMaterial[t]);
                }
                else
                {
                    Debug.LogWarning($"Agent {agent.name} has no color defined for team {t}.");
                }

                agentGroups[t].RegisterAgent(agent);

                //setup observation size dynamically according to number of teams and spawners
                agent.InitializeObservationSize(teams.Count, balloonSpawners.Count);
            }
        }
    }

    private void ResetFloorMaterial()
    {
        foreach (var rend in floorRenderers)
        {
            if (rend != null) rend.material = defaultFloorMaterial;
        }
    }

    private int GetTotalTeamBalloons(int teamIndex)
    {
        int count = 0;
        foreach (var agent in teams[teamIndex])
        {
            if (agent != null) count += agent.GetActiveBalloonCount();
        }
        return count;
    }

    private void DetermineWinnerByBalloons()
    {
        var statsRecorder = Academy.Instance.StatsRecorder;
        int teamCount = teams.Count;
        int bestScore = -1;

        // compute balloons per team and record averages
        List<int> totals = new List<int>();
        for (int t = 0; t < teamCount; t++)
        {
            int total = GetTotalTeamBalloons(t);
            totals.Add(total);

            if (total > bestScore)
            {
                bestScore = total;
            }

            //count stats
            float avg = teams[t].Count > 0 ? (float)total / teams[t].Count : 0f;
            statsRecorder.Add($"FinalAverageBalloons/{teamNames[t]}", avg, StatAggregationMethod.Average);
        }

        // count how many teams achieved maxScore
        List<int> winners = new List<int>();
        for (int t = 0; t < teamCount; t++)
        {
            if (totals[t] == bestScore) winners.Add(t);
        }

        // check full draw case (aka all have same score)
        if (winners.Count == teamCount)
        {
            EndMatchFullDraw();
            return;
        }

        // Partial tie: multiple winners
        EndMatchWinners(winners);
    }

    public void OnBalloonPopped(BattleBotAgent victim, BattleBotAgent attacker)
    {
        if (matchIsEnding) return;

        var statsRecorder = Academy.Instance.StatsRecorder;

            if (attacker != null && victim != null)
        {
            if (attacker.teamId == victim.teamId)
            {
                attacker.AddReward(-0.1f); // [Tuned] Reduced Friendly Fire penalty from -0.5f

                for (int i = 0; i < teams.Count; i++)
                {
                    if (attacker.teamId == i)
                    {
                        statsRecorder.Add($"PoppedTeamMemberBallons/{teamNames[i]}", 1, StatAggregationMethod.Sum);
                    }
                    else
                    {
                        statsRecorder.Add($"PoppedTeamMemberBallons/{teamNames[i]}", 0, StatAggregationMethod.Sum);
                    }
                }
            }
            else
            {
                attacker.AddReward(balloonPopReward);
                victim.AddReward(balloonPopPenalty);

                for (int i = 0; i< teams.Count; i++)
                {
                    if (attacker.teamId == i)
                    {
                        statsRecorder.Add($"PoppedOtherTeamBallons/{teamNames[i]}", 1, StatAggregationMethod.Sum);
                    }
                    else
                    {
                        statsRecorder.Add($"PoppedOtherTeamBallons/{teamNames[i]}", 0, StatAggregationMethod.Sum);
                    }
                }
            }
        }
    }

    private void EndMatchWinners(List<int> winningTeams)
    {
        if (matchIsEnding) return;
        matchIsEnding = true;
        var statsRecorder = Academy.Instance.StatsRecorder;

        for (int t = 0; t < teams.Count; t++)
        {
            bool win = winningTeams.Contains(t);
            if (win)
            {
                agentGroups[t].AddGroupReward(winGroupReward);
                statsRecorder.Add($"Victories/{teamNames[t]}", 1, StatAggregationMethod.Sum);
            }
            else
            {
                agentGroups[t].AddGroupReward(loseGroupReward);
                statsRecorder.Add($"Victories/{teamNames[t]}", 0, StatAggregationMethod.Sum);
            }

            statsRecorder.Add($"Draws/{teamNames[t]}", 0, StatAggregationMethod.Sum);
            agentGroups[t].EndGroupEpisode();
        }

        // Flash floor using first winners team material
        var mat = teams[winningTeams[0]][0].teamMaterial;
        StartCoroutine(FlashFloor(mat));

        ResetScene();
        StartCoroutine(ClearMatchEndingNextFrame());
    }

    private void EndMatchFullDraw()
    {
        if (matchIsEnding) return;
        matchIsEnding = true;

        var statsRecorder = Academy.Instance.StatsRecorder;

        for (var t = 0; t < teams.Count; t++)
        {
            // stats
            statsRecorder.Add($"Draws/{teamNames[t]}", 1, StatAggregationMethod.Sum);
            statsRecorder.Add($"Victories/{teamNames[t]}", 0, StatAggregationMethod.Sum);

            // rewards and reset episode
            agentGroups[t].AddGroupReward(-0.1f); // small penalty for draw
            agentGroups[t].EndGroupEpisode();
        }

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
        // [Curriculum] Update parameters from Academy
        var envParams = Academy.Instance.EnvironmentParameters;

        float windEnableParam = envParams.GetWithDefault("enable_wind", enableWind ? 1.0f : 0.0f);
        enableWind = windEnableParam > 0.5f;

        maxWindForce = envParams.GetWithDefault("max_wind_force", maxWindForce);

        currentSpawnAreaFrac = envParams.GetWithDefault("spawn_area_frac", spawnAreaFracDefault);

        float obsEnableParam = envParams.GetWithDefault("enable_obstacles", 1.0f); // Default to ON for safety if param missing
        if (obstaclesRoot != null)
        {
            obstaclesRoot.SetActive(obsEnableParam > 0.5f);
        }

        timer = 0f;
        timerElapsedFactor = 0f;

        List<BattleBotAgent> allAgents = new List<BattleBotAgent>();

        for (int t = 0; t < teams.Count; t++)
        {
            var teamAgents = teams[t];
            if (teamAgents != null) allAgents.AddRange(teamAgents);
        }

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
        // Use cached value
        float limit = (arenaHalfSize - wallPadding) * currentSpawnAreaFrac;

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

        for (int i = 0; i < teams.Count; i++)
        {
            ApplyWindToTeam(teams[i], timeFactor);
        }

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
            float distFactor = Mathf.Clamp01(dist / maxBattleRoyaleRadius);
            Vector3 dirToCenter = -new Vector3(planar.x, 0f, planar.y).normalized;

            // apply wind force that increases the further outside the zone the player is
            // float distOutside = Mathf.Max(0f, dist - currentBattleRoyaleRadius);
            // if (distOutside <= 0f) continue;
            // float distFactor = Mathf.Clamp01(distOutside / (maxBattleRoyaleRadius - currentBattleRoyaleRadius));
            // Vector3 dirToCenter = -new Vector3(planar.x, 0f, planar.y).normalized;

            Vector3 force = dirToCenter * maxWindForce * distFactor * timeFactor;
            rb.AddForce(force, ForceMode.Acceleration);
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