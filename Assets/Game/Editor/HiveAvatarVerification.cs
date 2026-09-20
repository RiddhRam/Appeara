using System;
using System.IO;
using System.Linq;
using Armory.Core;
using UnityEditor;
using UnityEngine;

namespace Armory.Editor
{
    /// <summary>Repeatable live smoke check. Run in desktop Play Mode; writes evidence under Temp.</summary>
    [InitializeOnLoad]
    public static class HiveAvatarVerification
    {
        private static int stage = -1;
        private static double deadline;
        private static Enemy defeated;
        static HiveAvatarVerification() => EditorApplication.update += Tick;

        [MenuItem("Armory/Verify Hive Avatar (Play Mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || WaveDirector.Instance == null) return;
            File.WriteAllText("Temp/hive-verification.txt", "RUNNING\n");
            Time.timeScale = 1f;
            if (!WaveDirector.Instance.RestartBossWave(0f))
            {
                File.AppendAllText("Temp/hive-verification.txt", "SKIPPED boss wave is not configured\n");
                Debug.LogWarning("The Hive Avatar wave is not configured in this run.");
                return;
            }
            stage = 0;
            deadline = EditorApplication.timeSinceStartup + 25f;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            File.AppendAllText("Temp/hive-verification.txt", "PASS " + message + "\n");
        }

        private static void Tick()
        {
            if (stage < 0) return;
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode ended before verification finished.");
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Boss did not become ready.");
                if (stage == 0)
                {
                    var boss = HiveAvatar.Active;
                    if (boss == null || !boss.Ready) return;
                    Time.timeScale = 0f;
                    var skin = boss.GetComponentInChildren<SkinnedMeshRenderer>();
                    Require(skin != null && skin.bounds.size.y > 5f, "Imported alien has visible boss scale");
                    Capture(boss);
                    var plasma = WeaponSpecParser.Parse("{\"payload\":\"plasma\"}");
                    boss.Adapt("plasma");
                    Require(Mathf.Abs(boss.Body.TakeHit(plasma, 100f) - 30f) < 0.1f, "Active plating reduces matching damage");
                    boss.Adapt("cryo");
                    Require(Mathf.Abs(boss.Body.TakeHit(plasma, 100f) - 100f) < 0.1f, "Switching plating releases old resistance");
                    string[] names = { "LEFT CLAW", "RIGHT CLAW", "SPORE SAC", "CREST" };
                    for (int i = 0; i < names.Length; i++)
                    {
                        var organ = boss.transform.Find(names[i]);
                        Require(organ != null && organ.GetComponent<Collider>().enabled, names[i] + " is a shootable target");
                        boss.Body.TakeHit(plasma, HiveAvatarState.OrganHealth, organ.position);
                        Require(boss.Rules.IsBroken((HiveOrgan)i) && !boss.Rules.CanAttack((HiveAttack)i), names[i] + " breaks and disables its attack");
                    }
                    var pod = HiveThreat.Launch(boss.transform.parent, boss.AimPoint, boss.Body.Target, true);
                    var podEnemy = pod.GetComponent<Enemy>();
                    podEnemy.TakeHit(plasma, 1000f);
                    Require(!podEnemy.Alive && !Enemy.All.Contains(podEnemy), "Spore pod can be shot down through normal weapon damage");
                    // Let the encounter create threats itself during separate visual inspection. This checks death
                    // and a complete fresh encounter, including the public factory/director damage path.
                    defeated = boss.Body;
                    boss.Body.TakeHit(plasma, 10000f);
                    Require(HiveAvatar.Active == null && !Enemy.All.Contains(defeated), "Boss death releases wave tracking immediately");
                    Require(defeated.GetComponentsInChildren<Collider>().All(c => !c.enabled), "Death animation cannot block shots");
                    stage = 1;
                }
                else if (stage == 1)
                {
                    Require(UnityEngine.Object.FindObjectsByType<HiveThreat>(FindObjectsSortMode.None).Length == 0, "No shootable threats survive the defeated encounter");
                    Time.timeScale = 1f;
                    if (!WaveDirector.Instance.RestartBossWave(0f))
                        throw new InvalidOperationException("Boss wave was removed during verification.");
                    stage = 2;
                    deadline = EditorApplication.timeSinceStartup + 25f;
                }
                else if (stage == 2)
                {
                    var boss = HiveAvatar.Active;
                    if (boss == null || !boss.Ready) return;
                    Require(boss.Body != defeated && boss.Rules.BrokenCount == 0 && boss.Rules.Plating == null, "Restart creates a fresh boss with intact organs and no stale plating");
                    File.AppendAllText("Temp/hive-verification.txt", "COMPLETE\n");
                    stage = -1;
                    Debug.Log("Hive Avatar live smoke check passed. Evidence: Temp/hive-verification.txt and Temp/hive-boss.png");
                }
            }
            catch (Exception error)
            {
                stage = -1;
                Time.timeScale = 1f;
                File.AppendAllText("Temp/hive-verification.txt", "FAIL " + error.Message + "\n");
                Debug.LogException(error);
            }
        }

        private static void Capture(HiveAvatar boss)
        {
            var go = new GameObject("Hive Verification Camera");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = boss.transform.position + boss.transform.forward * 21f + boss.transform.right * 8f + Vector3.up * 9f;
            camera.transform.LookAt(boss.transform.position + Vector3.up * 7f);
            camera.fieldOfView = 55f;
            var target = new RenderTexture(1280, 720, 24);
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                texture.Apply();
                File.WriteAllBytes("Temp/hive-boss.png", texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(texture);
                UnityEngine.Object.Destroy(go);
            }
        }
    }
}
