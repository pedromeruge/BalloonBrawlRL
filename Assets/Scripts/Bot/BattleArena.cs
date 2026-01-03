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
    [SerializeField] private Collider floorCollider;
    [SerializeField] private LayerMask floorMask = ~0;
    [SerializeField] private float spawnSkin = 0.02f;

    [Header("Arena Visuals")]
    public MeshRenderer floorRenderer;
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

    private SimpleMultiAgentGroup groupTeam0;
    private SimpleMultiAgentGroup groupTeam1;
    private bool matchIsEnding = false;
    private int envStepCount = 0;

    [Header("Time")]
    public float matchDuration = 30f; 
    private float timer = 0f;

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

        if (floorRenderer != null) floorRenderer.material = defaultFloorMaterial;

        ResetScene();
    }

    void FixedUpdate()
    {
        if (matchIsEnding) return;

        timer += Time.fixedDeltaTime;

        if (timer >= matchDuration)
        {
            DetermineWinnerByBalloons();
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
            EndMatchWin(0); // Equipa Azul ganha
        }
        else if (team1Balloons > team0Balloons)
        {
            EndMatchWin(1); // Equipa Vermelha ganha
        }
        else
        {
            EndMatchDraw(); // Empate se o número de balões for igual
        }
    }

    public void OnBalloonPopped(BattleBotAgent victim, BattleBotAgent attacker)
    {
        if (matchIsEnding) return;

        if (attacker != null && victim != null)
        {
            if (attacker.teamId == victim.teamId)
            {
                // --- CASE 1: Friendly Fire ---
                // PUNISH the traitor heavily so they learn NOT to do this.
                attacker.AddReward(-0.5f);
            }
            else
            {
                // --- CASE 2: Valid Enemy Kill ---
                attacker.AddReward(balloonPopReward); // +0.1f
                victim.AddReward(balloonPopPenalty);  // -0.1f
            }
        }
    }

    private void EndMatchWin(int winningTeamId)
    {
        if (matchIsEnding) return;
        matchIsEnding = true;

        if (winningTeamId == 0)
        {
            groupTeam0.AddGroupReward(winGroupReward);
            groupTeam1.AddGroupReward(loseGroupReward);
            
            // Visual feedback: Flash winner color
            if (floorRenderer != null && team0Agents.Count > 0 && team0Agents[0] != null) 
                StartCoroutine(FlashFloor(team0Agents[0].teamMaterial));
        }
        else
        {
            groupTeam1.AddGroupReward(winGroupReward);
            groupTeam0.AddGroupReward(loseGroupReward);
            
            if (floorRenderer != null && team1Agents.Count > 0 && team1Agents[0] != null) 
                StartCoroutine(FlashFloor(team1Agents[0].teamMaterial));
        }

        // End Group Episodes (Resets all agents in the groups)
        groupTeam0.EndGroupEpisode();
        groupTeam1.EndGroupEpisode();

        ResetScene();
        StartCoroutine(ClearMatchEndingNextFrame());
    }

    private void EndMatchDraw()
    {
        if (matchIsEnding) return;
        matchIsEnding = true;

        // Small negative or zero for draw
        groupTeam0.AddGroupReward(-0.1f);
        groupTeam1.AddGroupReward(-0.1f);

        groupTeam0.EndGroupEpisode();
        groupTeam1.EndGroupEpisode();

        ResetScene();
        StartCoroutine(ClearMatchEndingNextFrame());
    }

    IEnumerator FlashFloor(Material winnerMat)
    {
        if (floorRenderer != null && winnerMat != null)
        {
            floorRenderer.material = winnerMat;
            yield return new WaitForSeconds(winFlashDuration);
            if (floorRenderer != null) floorRenderer.material = defaultFloorMaterial;
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

        // Reset Spawners (if needed, though they usually self-manage)
        // Agents are reset automatically by EndGroupEpisode -> OnEpisodeBegin
        // But we need to place them manually to ensure no overlaps

        List<BattleBotAgent> allAgents = new List<BattleBotAgent>();
        if (team0Agents != null) allAgents.AddRange(team0Agents);
        if (team1Agents != null) allAgents.AddRange(team1Agents);

        PlaceAgentsNonOverlapping(allAgents);
    }

    void PlaceAgentsNonOverlapping(List<BattleBotAgent> agents)
    {
        List<Vector3> placedPositions = new List<Vector3>();

        foreach(var agent in agents)
        {
            if (agent == null) continue;

            // Important: Agent physics must be reset before placement or immediately after
            // (Agent.ResetAgent() handles velocity reset, so we just handle position)
            
            float r = GetAgentSpawnRadius(agent);
            
            // Find a valid position that doesn't overlap with previously placed agents in this loop
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

            // 1. Check against agents we just placed in this reset loop
            bool collision = false;
            foreach(var existing in existingPositions)
            {
                 // Simple distance check: (r1 + r2) + margin. 
                 // We assume other agents have roughly similar radius for simplicity, 
                 // or we conservatively use selfRadius * 2
                 if (Vector3.Distance(p, existing) < (selfRadius * 2f + requiredSeparation)) 
                 {
                     collision = true;
                     break;
                 }
            }
            if(collision) continue;

            // 2. Check against static environment (Walls, etc)
            var world = LocalToWorld(p);
            float floorTop = GetFloorTopYAtXZ(world);
            world.y = floorTop + halfH + spawnSkin;

            if (!IsFreeWorld(world, selfRadius))
                continue;

            return p;
        }

        // Fallback
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
            
            // Ignore collision with ANY agent (since we handle agent-agent overlap separately)
            // This prevents "self-collision" or collision with teammates blocking a valid spawn
            // if they happen to be on the spawnBlocker layer (though they shouldn't be).
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
}