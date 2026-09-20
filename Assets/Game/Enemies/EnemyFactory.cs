using Armory.AI;
using Armory.Core;
using Unity.Profiling;
using UnityEngine;
using System.Collections.Generic;

namespace Armory
{
    /// <summary>
    /// Builds aliens: stats, the primitive that carries the hitbox, and the authored model on top of it.
    /// The primitives are still built for every kind even when art exists, because the collider, Radius and the
    /// health bar offsets are all derived from them - the model is decoration hung over that gameplay shape.
    /// </summary>
    public static class EnemyFactory
    {
        private static readonly ProfilerMarker SpawnMarker = new ProfilerMarker("Armory.Enemy.Spawn");
        private const float SwarmSpeedMultiplier = 0.5f;
        private const float OtherEnemySpeedMultiplier = 0.66f;
        private static readonly Dictionary<EnemyKind, Stack<Enemy>> pools = new Dictionary<EnemyKind, Stack<Enemy>>();
        private static Transform poolRoot;

        public static Enemy Spawn(EnemyKind kind, Vector3 position, Vector3 target, MothershipSpawnTrace spawnTrace = null)
        {
            using var marker = SpawnMarker.Auto();
            var enemy = TakeFromPool(kind);
            bool isNew = enemy == null;
            var go = isNew ? new GameObject(kind.ToString()) : enemy.gameObject;
            if (isNew)
            {
                go.SetActive(false);
                enemy = go.AddComponent<Enemy>();
                enemy.Poolable = kind != EnemyKind.Boss;
            }
            else enemy.ResetForPool();

            go.name = kind.ToString();
            go.transform.SetParent(null, true);
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(target - position);

            switch (kind)
            {
                case EnemyKind.Swarm:
                    Stats(enemy, kind, hp: 8f, speed: 4.2f, core: 2f, radius: 0.3f, color: new Color(1f, 0.25f, 0.2f));
                    if (isNew) Body(go, enemy, PrimitiveType.Sphere, new Vector3(0.6f, 0.6f, 0.6f), 0.35f);
                    break;
                case EnemyKind.Armored:
                    Stats(enemy, kind, hp: 160f, speed: 1.6f, core: 15f, radius: 1f, color: new Color(0.55f, 0.55f, 0.6f));
                    if (isNew)
                    {
                        Body(go, enemy, PrimitiveType.Cube, new Vector3(1.8f, 2.2f, 1.4f), 1.1f);
                        Mats.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 1.5f, 0.75f), new Vector3(1.5f, 0.9f, 0.2f), Mats.Lit(new Color(0.35f, 0.35f, 0.4f)), name: "Plate");
                    }
                    break;
                case EnemyKind.Fast:
                    Stats(enemy, kind, hp: 20f, speed: 7f, core: 5f, radius: 0.4f, color: new Color(1f, 0.9f, 0.2f));
                    if (isNew) Body(go, enemy, PrimitiveType.Capsule, new Vector3(0.6f, 0.8f, 0.6f), 0.8f);
                    break;
                case EnemyKind.Shielded:
                    Stats(enemy, kind, hp: 40f, speed: 2.5f, core: 8f, radius: 0.6f, color: new Color(0.3f, 0.5f, 1f));
                    enemy.ShieldHealth = 80f;
                    if (isNew) Body(go, enemy, PrimitiveType.Capsule, new Vector3(0.9f, 1f, 0.9f), 1f);
                    break;
                case EnemyKind.Boss:
                    Stats(enemy, kind, hp: 2400f, speed: 1.1f, core: 60f, radius: 2.5f, color: new Color(0.7f, 0.2f, 1f));
                    break;
                default:
                    Stats(enemy, kind, hp: 30f, speed: 3f, core: 5f, radius: 0.5f, color: new Color(0.3f, 0.95f, 0.4f));
                    if (isNew) Body(go, enemy, PrimitiveType.Capsule, new Vector3(0.8f, 0.9f, 0.8f), 0.9f);
                    break;
            }

            // Eyes on everything so direction reads at a distance.
            if (isNew && kind != EnemyKind.Boss)
                Mats.Shape(PrimitiveType.Sphere, go.transform, new Vector3(0f, enemy.Radius * 2f + 0.3f, enemy.Radius * 0.9f), Vector3.one * Mathf.Max(0.15f, enemy.Radius * 0.35f), Mats.Lit(Color.white, 1.5f), name: "Eye");

            enemy.enabled = true;
            go.SetActive(true);
            // After the object is live and before Init: the binder settles the animator and bakes the skinned
            // pose to measure height, neither of which works on an inactive GameObject, and Init's tint pass
            // has to see the model's renderers. The boss is skipped because HiveAvatar loads and fits its own
            // model; a second one would stand inside it. A recycled body still carries the model it was built
            // with, and Attach hands that one back rather than stacking another.
            if (kind != EnemyKind.Boss)
            {
                enemy.Visual = EnemyVisualBinder.Attach(go, kind);
                // A recycled body kept its model, so nothing would replay the spawn-in without being told.
                if (!isNew) enemy.Visual?.Respawned();
            }
            enemy.Init(kind, target);
            if (kind == EnemyKind.Boss)
            {
                if (ArmoryGame.Instance != null) go.transform.SetParent(ArmoryGame.Instance.transform, true);
                var initializeSpan = spawnTrace?.StartSpan("unity.avatar_initialize", "Load model and build boss targets");
                try
                {
                    go.AddComponent<HiveAvatar>().Initialize(enemy, Resources.Load<HiveAvatarAssets>("HiveAvatarAssets"), spawnTrace);
                    spawnTrace?.FinishSpan(initializeSpan);
                }
                catch (System.Exception error)
                {
                    spawnTrace?.FinishSpan(initializeSpan, error);
                    throw;
                }
            }
            return enemy;
        }

        public static bool ReturnToPool(Enemy enemy)
        {
            if (enemy == null || !enemy.Poolable || enemy.Avatar != null || enemy.ExternallyDriven) return false;
            EnemyKind kind = enemy.Kind;
            enemy.ResetForPool();
            enemy.transform.SetParent(PoolRoot, true);
            enemy.gameObject.SetActive(false);
            if (!pools.TryGetValue(kind, out var pool)) pools[kind] = pool = new Stack<Enemy>();
            pool.Push(enemy);
            return true;
        }

        private static Enemy TakeFromPool(EnemyKind kind)
        {
            if (!pools.TryGetValue(kind, out var pool)) return null;
            while (pool.Count > 0)
            {
                var enemy = pool.Pop();
                if (enemy != null) return enemy;
            }
            return null;
        }

        private static Transform PoolRoot
        {
            get
            {
                if (poolRoot == null) poolRoot = new GameObject("Enemy Pool").transform;
                return poolRoot;
            }
        }

        private static void Stats(Enemy enemy, EnemyKind kind, float hp, float speed, float core, float radius, Color color)
        {
            enemy.MaxHealth = enemy.Health = hp;
            enemy.Speed = speed * (kind == EnemyKind.Swarm ? SwarmSpeedMultiplier : OtherEnemySpeedMultiplier);
            enemy.CoreDamage = core;
            enemy.Radius = radius;
            enemy.BaseColor = color;
        }

        private static void Body(GameObject root, Enemy enemy, PrimitiveType type, Vector3 scale, float centerHeight)
        {
            var body = Mats.Shape(type, root.transform, Vector3.up * centerHeight, scale, Mats.Lit(enemy.BaseColor, 0.25f), collider: true, name: "Body");
            body.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
}
