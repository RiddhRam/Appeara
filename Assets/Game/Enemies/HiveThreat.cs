using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>Shootable boss ordnance. Uses Enemy so every existing weapon can intercept it.</summary>
    public sealed class HiveThreat : MonoBehaviour
    {
        private Enemy target;
        private Vector3 start, destination;
        private bool spore;
        private float age;
        private HiveTelegraph marker;

        public static HiveThreat Launch(Transform owner, Vector3 from, Vector3 to, bool spore)
        {
            var go = new GameObject(spore ? "Hive Spore Pod" : "Hive Wreck Fragment");
            go.transform.SetParent(owner, false);
            go.transform.position = from;
            var enemy = go.AddComponent<Enemy>();
            enemy.ExternallyDriven = true;
            enemy.MaxHealth = enemy.Health = spore ? 32f : 65f;
            enemy.Radius = spore ? 0.7f : 1.2f;
            enemy.BaseColor = spore ? new Color(0.45f, 1f, 0.2f) : new Color(1f, 0.45f, 0.12f);
            Mats.Shape(spore ? PrimitiveType.Sphere : PrimitiveType.Cube, go.transform, Vector3.zero,
                Vector3.one * enemy.Radius * 2f, Mats.Lit(enemy.BaseColor, 0.8f), collider: true, name: "Shootable Threat");
            // No mothership armor/shield here: interception must always be achievable during its warning window.
            enemy.Init(EnemyKind.Grunt, to);
            var threat = go.AddComponent<HiveThreat>();
            threat.target = enemy;
            threat.start = from;
            threat.destination = to;
            threat.spore = spore;
            threat.marker = HiveTelegraph.Create(owner, new Vector3(to.x, owner.position.y, to.z), spore ? 1.5f : 3f, enemy.BaseColor);
            return threat;
        }

        private void Update()
        {
            if (target == null || !target.Alive) return;
            age += Time.deltaTime;
            float duration = spore ? 5f : 6f;
            float t = Mathf.Clamp01(age / duration);
            transform.position = Vector3.Lerp(start, destination, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 5f);
            transform.Rotate(30f * Time.deltaTime, 70f * Time.deltaTime, 0f);
            if (marker != null) marker.SetProgress(t);
            if (t < 1f) return;
            if (spore)
            {
                for (int i = 0; i < 3; i++)
                {
                    var enemy = EnemyFactory.Spawn(EnemyKind.Swarm, destination + new Vector3(i - 1, 0f, 0f), StationCore.Instance != null ? StationCore.Instance.transform.position : destination);
                    enemy.transform.SetParent(transform.parent, true);
                }
            }
            // Remove before damaging the core, because that may synchronously restart and destroy the encounter.
            target.Die(false);
            if (!spore) StationCore.Instance?.TakeDamage(18f);
        }

        public void Cancel() { if (target != null && target.Alive) target.Die(false); }
        private void OnDestroy() { if (marker != null) Destroy(marker.gameObject); }
    }
}
