using UnityEngine;

/// <summary>
/// Spawns enemies at evenly spaced positions around a circle on the XZ plane.
/// </summary>
public sealed class EnemyPerimeterSpawner : MonoBehaviour
{
    private const float SpawnRadius = 50f;

    [SerializeField, Min(1)] private int enemyCount = 8;
    [SerializeField] private GameObject enemyPrefab;

    private void Start()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("Enemy Perimeter Spawner needs an enemy prefab assigned.", this);
            return;
        }

        for (var index = 0; index < enemyCount; index++)
        {
            var angle = index * Mathf.PI * 2f / enemyCount;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var position = direction * SpawnRadius;
            var rotation = Quaternion.LookRotation(-direction, Vector3.up);

            Instantiate(enemyPrefab, position, rotation, transform);
        }
    }
}
