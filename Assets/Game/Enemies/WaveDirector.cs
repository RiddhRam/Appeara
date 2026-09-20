using System;
using System.Collections;
using System.Collections.Generic;
using Armory.AI;
using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>Scripted demo waves (≈5-7 min): grunts → swarm → armored → fast+shielded → adaptive boss.</summary>
    public sealed class WaveDirector : MonoBehaviour
    {
        public static WaveDirector Instance { get; private set; }

        [Serializable]
        public sealed class Group
        {
            public EnemyKind Kind;
            public int Count;
            public float Interval;
            public Group(EnemyKind kind, int count, float interval) { Kind = kind; Count = count; Interval = interval; }
        }

        public sealed class Wave
        {
            public string Name;
            public string Hint;
            public Group[] Groups;
        }

        public float SpawnRadius = 38f;
        [Tooltip("Minimum seconds in the armory phase before the wave can be started.")]
        public float ArmoryMinimumSeconds = 3f;
        [Tooltip("Core integrity repaired per second while in the armory phase.")]
        public float ArmoryRepairPerSecond = 2f;

        public int WaveIndex { get; private set; } = -1;
        public string State { get; private set; } = "Standing by";
        public int Alive => Enemy.All.Count;
        public int PendingSpawns { get; private set; }
        public Wave Current => WaveIndex >= 0 && WaveIndex < Waves.Count ? Waves[WaveIndex] : null;

        public readonly List<Wave> Waves = new List<Wave>
        {
            new Wave { Name = "First Contact", Hint = "Grunts. Anything works. Try your default rifle.", Groups = new[] { new Group(EnemyKind.Grunt, 12, 0.9f) } },
            new Wave { Name = "The Swarm", Hint = "Dozens of tiny swarmers. Area damage shreds them.", Groups = new[] { new Group(EnemyKind.Swarm, 45, 0.18f), new Group(EnemyKind.Grunt, 4, 2f) } },
            new Wave { Name = "Heavy Plating", Hint = "Armored brutes shrug off bullets. Pierce or blow them up.", Groups = new[] { new Group(EnemyKind.Armored, 6, 2.5f), new Group(EnemyKind.Grunt, 8, 1.2f) } },
            new Wave { Name = "Blitz", Hint = "Fast runners and shielded escorts. Homing, cryo, and electricity.", Groups = new[] { new Group(EnemyKind.Fast, 14, 0.8f), new Group(EnemyKind.Shielded, 6, 2f) } },
            new Wave { Name = "The Mothership Avatar", Hint = "It adapts to whatever you use. Keep inventing.", Groups = new[] { new Group(EnemyKind.Boss, 1, 0f), new Group(EnemyKind.Swarm, 20, 1.5f) } },
        };

        private Coroutine flow;
        private readonly List<Coroutine> spawners = new List<Coroutine>();
        private Sentry.ITransactionTracer activeWaveTrace;

        private void Awake() => Instance = this;

        /// <summary>True while the player is between waves and free to design a weapon.</summary>
        public bool InArmory { get; private set; }
        /// <summary>True only while a wave is spawning or has living enemies; counter analysis advances only here.</summary>
        public bool CombatActive { get; private set; }

        private bool startRequested;

        private void Start() => flow = StartCoroutine(Run(0, 0f));

        /// <summary>Player said "ready" or pressed the button; the wave starts on the next frame.</summary>
        public void RequestWaveStart() => startRequested = true;

        private IEnumerator Run(int startIndex, float delay)
        {
            if (delay > 0f)
            {
                State = "Incoming in " + Mathf.CeilToInt(delay) + "s";
                yield return new WaitForSeconds(delay);
            }
            for (WaveIndex = startIndex; WaveIndex < Waves.Count; WaveIndex++)
            {
                var wave = Waves[WaveIndex];
                yield return Armory(wave);
                ArmoryGame.Instance.WaveLog.Clear();
                State = $"WAVE {WaveIndex + 1}: {wave.Name}";
                int enemyCount = 0;
                foreach (var group in wave.Groups) enemyCount += group.Count;
                activeWaveTrace = ArmoryTelemetry.StartWave(WaveIndex + 1, wave.Name, enemyCount);
                CombatActive = true;
                yield return SpawnWave(wave);
                while (Alive > 0 || PendingSpawns > 0) yield return null;
                CombatActive = false;
                ArmoryTelemetry.FinishWave(activeWaveTrace, "cleared");
                activeWaveTrace = null;

                if (WaveIndex == Waves.Count - 1) break;
                State = "Wave cleared. Return to the armory.";
            }
            State = "VICTORY. The station holds.";
            ShipAI.Instance?.SayShip("The mothership is retreating. Not bad for a pile of improvised weapons.", "VICTORY");
        }

        /// <summary>
        /// Untimed armory phase: nothing spawns, the core repairs, and ARIA briefs the player. The wave starts only
        /// when the player says "ready" or presses the button, so there is always time to design a weapon.
        /// </summary>
        private IEnumerator Armory(Wave wave)
        {
            InArmory = true;
            CombatActive = false;
            startRequested = false;
            float earliest = Time.time + ArmoryMinimumSeconds;
            string prompt = WaveIndex == 0 ? "Say \"ready\" when you want the first wave." : "Say \"ready\" when you want them.";
            ShipAI.Instance?.SayShip($"Armory phase. Next: {wave.Name}. {wave.Hint} {prompt}", "ARMORY  ·  " + wave.Name);

            while (!startRequested || Time.time < earliest)
            {
                State = "ARMORY · design a weapon · say \"ready\" to begin";
                var core = StationCore.Instance;
                if (core != null && ArmoryRepairPerSecond > 0f) core.Repair(ArmoryRepairPerSecond * Time.deltaTime);
                yield return null;
            }
            InArmory = false;
            startRequested = false;
            ShipAI.Instance?.SayShip($"Wave {WaveIndex + 1}. Good luck.", $"Wave {WaveIndex + 1:00}  ·  {wave.Name}");
        }

        private IEnumerator SpawnWave(Wave wave)
        {
            spawners.Clear();
            PendingSpawns = 0;
            foreach (var group in wave.Groups) PendingSpawns += group.Count;
            foreach (var group in wave.Groups) spawners.Add(StartCoroutine(SpawnGroup(group)));
            yield return null;
        }

        private IEnumerator SpawnGroup(Group group)
        {
            bool spread = Mothership.Instance != null && Mothership.Instance.Has(CounterKind.Spread);
            float baseAngle = UnityEngine.Random.value * 360f;
            for (int i = 0; i < group.Count; i++)
            {
                // Clustered arcs normally; evenly spread around the whole ring once the mothership learns to spread.
                float angle = spread ? baseAngle + i * (360f / group.Count) + UnityEngine.Random.Range(-10f, 10f)
                                     : baseAngle + UnityEngine.Random.Range(-35f, 35f);
                var position = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * SpawnRadius + transform.position;
                if (group.Kind == EnemyKind.Boss && ArmoryGame.Instance != null && ArmoryGame.Instance.Rig != null)
                {
                    Vector3 approach = ArmoryGame.Instance.Rig.Head.transform.position - transform.position;
                    approach.y = 0f;
                    if (approach.sqrMagnitude < 1f) approach = Vector3.forward;
                    position = transform.position + approach.normalized * SpawnRadius;
                }
                MothershipSpawnTrace spawnTrace = null;
                Sentry.ISpan factorySpan = null;
                if (group.Kind == EnemyKind.Boss)
                {
                    spawnTrace = ArmoryTelemetry.StartMothershipSpawn(WaveIndex + 1,
                        Vector3.Distance(position, transform.position), Mothership.Instance?.ActivePackage != null);
                    factorySpan = spawnTrace.StartSpan("unity.enemy_factory", "Construct Mothership Avatar root");
                }
                Enemy enemy;
                try
                {
                    enemy = EnemyFactory.Spawn(group.Kind, position, transform.position, spawnTrace);
                    spawnTrace?.FinishSpan(factorySpan);
                }
                catch (Exception error)
                {
                    spawnTrace?.FinishSpan(factorySpan, error);
                    spawnTrace?.Fail("enemy_factory", error);
                    throw;
                }
                if (group.Kind == EnemyKind.Boss)
                {
                    ShipAI.Instance?.SayMothership("Everything you fabricate, we will analyze and counter.", "THE MOTHERSHIP AVATAR");
                }
                PendingSpawns--;
                if (group.Interval > 0f) yield return new WaitForSeconds(group.Interval);
            }
        }

        public void OnEnemyRemoved(Enemy enemy, bool killed) { }

        public void OnCoreDestroyed()
        {
            ArmoryTelemetry.FinishWave(activeWaveTrace, "core_breached");
            activeWaveTrace = null;
            ShipAI.Instance?.SayShip("Core breached! Emergency repairs. Restarting the wave.", "CORE BREACHED");
            RestartWave(Mathf.Max(0, WaveIndex), 4f);
        }

        public void RestartWave(int index, float delay)
        {
            ArmoryTelemetry.FinishWave(activeWaveTrace, "restarted");
            activeWaveTrace = null;
            if (flow != null) StopCoroutine(flow);
            foreach (var spawner in spawners) if (spawner != null) StopCoroutine(spawner);
            spawners.Clear();
            PendingSpawns = 0;
            CombatActive = false;
            foreach (var enemy in new List<Enemy>(Enemy.All)) enemy.Die(false);
            StationCore.Instance?.Repair();
            flow = StartCoroutine(Run(Mathf.Clamp(index, 0, Waves.Count - 1), delay));
        }

        public void SkipWave() => RestartWave(WaveIndex + 1, 1f);
    }
}
