using UnityEngine;

public class SpikeHitbox : MonoBehaviour
{
    [SerializeField] private BattleBotAgent owner;

    void Reset()
    {
        owner = GetComponentInParent<BattleBotAgent>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Balloon")) return;

        var balloon = other.GetComponent<BalloonUnit>();
        if (balloon == null) return;
        if (balloon.owner == null) return;
        if (balloon.owner == owner) return;

        balloon.Pop();
        if (owner != null && owner.arena != null)
            owner.arena.OnBalloonPopped(balloon.owner, owner);
    }
}
