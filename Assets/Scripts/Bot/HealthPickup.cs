using UnityEngine;

public class HealthPickup : MonoBehaviour
{
    [Header("Reward")]
    public float healReward = 0.075f;

    private bool consumed = false;

    void OnTriggerEnter(Collider other)
    {
        if (consumed) return;

        // Find the agent (Handle cases where collider is on a child object)
        BattleBotAgent agent = other.GetComponentInParent<BattleBotAgent>();
        if (agent == null) return;

        // 1. Check if match is already deciding winner
        if (agent.arena != null && agent.arena.MatchIsEnding) return;

        // 2. Try to heal (BattleBotAgent.RestoreBalloon handles the IsDead check)
        bool wasHealed = agent.RestoreBalloon(true);
        
        // If full health or dead, RestoreBalloon returns false, so we don't consume the pickup
        if (!wasHealed) return; 

        consumed = true;

        // 3. Apply Individual Reward
        agent.AddReward(healReward);

        // 4. Destroy the balloon object
        Destroy(gameObject);
    }
}