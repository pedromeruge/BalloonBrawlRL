using UnityEngine;

public class BalloonSpawner : MonoBehaviour
{
    [Header("Settings")]
    public GameObject balloonPrefab;
    public Vector2 spawnAreaSize = new Vector2(4f, 4f);
    public float respawnTime = 5.0f;
    public float spawnHeight = 1.0f;

    private GameObject currentInstance;
    public GameObject ActiveBalloon => currentInstance;
    private float timer = 0f;

    void Start()
    {
        SpawnBalloon();
    }

    void Update()
    {
        if (currentInstance == null)
        {
            timer += Time.deltaTime;

            if (timer >= respawnTime)
            {
                SpawnBalloon();
                timer = 0f;
            }
        }
    }

    void SpawnBalloon()
    {
        if (balloonPrefab == null) return;

        float randomX = Random.Range(-spawnAreaSize.x / 2, spawnAreaSize.x / 2);
        float randomZ = Random.Range(-spawnAreaSize.y / 2, spawnAreaSize.y / 2);

        Vector3 spawnPos = transform.position + new Vector3(randomX, spawnHeight, randomZ);

        currentInstance = Instantiate(balloonPrefab, spawnPos, Quaternion.identity, transform);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Vector3 center = transform.position + Vector3.up * spawnHeight;
        Vector3 size = new Vector3(spawnAreaSize.x, 0.1f, spawnAreaSize.y);
        Gizmos.DrawCube(center, size);
        Gizmos.DrawWireCube(center, size);
    }
}