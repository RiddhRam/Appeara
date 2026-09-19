using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>Builds placeholder aliens. Swap visuals here when real models are ready; stats live here too.</summary>
    public static class EnemyFactory
    {
        public static Enemy Spawn(EnemyKind kind, Vector3 position, Vector3 target)
        {
            var go = new GameObject(kind.ToString());
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(target - position);
            var enemy = go.AddComponent<Enemy>();

            switch (kind)
            {
                case EnemyKind.Swarm:
                    Stats(enemy, hp: 8f, speed: 4.2f, core: 2f, radius: 0.3f, color: new Color(1f, 0.25f, 0.2f));
                    Body(go, enemy, PrimitiveType.Sphere, new Vector3(0.6f, 0.6f, 0.6f), 0.35f);
                    break;
                case EnemyKind.Armored:
                    Stats(enemy, hp: 160f, speed: 1.6f, core: 15f, radius: 1f, color: new Color(0.55f, 0.55f, 0.6f));
                    Body(go, enemy, PrimitiveType.Cube, new Vector3(1.8f, 2.2f, 1.4f), 1.1f);
                    Mats.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 1.5f, 0.75f), new Vector3(1.5f, 0.9f, 0.2f), Mats.Lit(new Color(0.35f, 0.35f, 0.4f)), name: "Plate");
                    break;
                case EnemyKind.Fast:
                    Stats(enemy, hp: 20f, speed: 7f, core: 5f, radius: 0.4f, color: new Color(1f, 0.9f, 0.2f));
                    Body(go, enemy, PrimitiveType.Capsule, new Vector3(0.6f, 0.8f, 0.6f), 0.8f);
                    break;
                case EnemyKind.Shielded:
                    Stats(enemy, hp: 40f, speed: 2.5f, core: 8f, radius: 0.6f, color: new Color(0.3f, 0.5f, 1f));
                    enemy.ShieldHealth = 80f;
                    Body(go, enemy, PrimitiveType.Capsule, new Vector3(0.9f, 1f, 0.9f), 1f);
                    break;
                case EnemyKind.Boss:
                    Stats(enemy, hp: 2400f, speed: 1.1f, core: 60f, radius: 2.5f, color: new Color(0.7f, 0.2f, 1f));
                    Body(go, enemy, PrimitiveType.Sphere, new Vector3(5f, 4f, 5f), 2.5f);
                    Mats.Shape(PrimitiveType.Sphere, go.transform, new Vector3(0f, 3.2f, 2f), Vector3.one * 1.2f, Mats.Lit(new Color(1f, 0.2f, 0.4f), 2f), name: "Eye");
                    break;
                default:
                    Stats(enemy, hp: 30f, speed: 3f, core: 5f, radius: 0.5f, color: new Color(0.3f, 0.95f, 0.4f));
                    Body(go, enemy, PrimitiveType.Capsule, new Vector3(0.8f, 0.9f, 0.8f), 0.9f);
                    break;
            }

            // Eyes on everything so direction reads at a distance.
            if (kind != EnemyKind.Boss)
                Mats.Shape(PrimitiveType.Sphere, go.transform, new Vector3(0f, enemy.Radius * 2f + 0.3f, enemy.Radius * 0.9f), Vector3.one * Mathf.Max(0.15f, enemy.Radius * 0.35f), Mats.Lit(Color.white, 1.5f), name: "Eye");

            enemy.Init(kind, target);
            return enemy;
        }

        private static void Stats(Enemy enemy, float hp, float speed, float core, float radius, Color color)
        {
            enemy.MaxHealth = enemy.Health = hp;
            enemy.Speed = speed;
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
