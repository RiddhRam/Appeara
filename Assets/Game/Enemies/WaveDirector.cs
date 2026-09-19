using System;
using System.Collections;
using System.Collections.Generic;
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
        public float BreakSeconds = 8f;
        public float IntroSeconds = 6f;

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
        private float nextBossAdapt;
        private bool bossAlive;

        private void Awake() => Instance = this;

        private void Start() => flow = StartCoroutine(Run(0, IntroSeconds));

        private void Update()
        {
            if (!bossAlive || Time.time < nextBossAdapt) return;
            nextBossAdapt = Time.time + Mothership.BossAdaptSeconds;
            var game = ArmoryGame.Instance;
            Mothership.Instance?.BossAdapt(game.BossWindowLog);
            game.BossWindowLog.Clear();
        }

        private IEnumerator Run(int startIndex, float delay)
        {
            State = "Incoming in " + Mathf.CeilToInt(delay) + "s";
            yield return new WaitForSeconds(delay);
            for (WaveIndex = startIndex; WaveIndex < Waves.Count; WaveIndex++)
            {
                var wave = Waves[WaveIndex];
                ArmoryGame.Instance.WaveLog.Clear();
                State = $"WAVE {WaveIndex + 1}: {wave.Name}";
                ShipAI.Instance?.SayShip($"Wave {WaveIndex + 1}. {wave.Hint}", $"Wave {WaveIndex + 1:00}  ·  {wave.Name}");
                yield return SpawnWave(wave);
                while (Alive > 0 || PendingSpawns > 0) yield return null;
                bossAlive = false;

                if (WaveIndex == Waves.Count - 1) break;
                State = "Wave cleared. Mothership adapting...";
                float breakEnds = Time.time + BreakSeconds;
                _ = Mothership.Instance.AdaptAfterWave(ArmoryGame.Instance.WaveLog, Waves[WaveIndex + 1].Name);
                while (Mothership.Instance.Thinking || Time.time < breakEnds)
                {
                    State = $"Next wave in {Mathf.CeilToInt(Mathf.Max(0f, breakEnds - Time.time))}s · ask the ship AI for a counter";
                    yield return null;
                }
            }
            State = "VICTORY. The station holds.";
            ShipAI.Instance?.SayShip("The mothership is retreating. Not bad for a pile of improvised weapons.", "VICTORY");
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
                var enemy = EnemyFactory.Spawn(group.Kind, position, transform.position);
                if (group.Kind == EnemyKind.Boss)
                {
                    bossAlive = true;
                    nextBossAdapt = Time.time + Mothership.BossAdaptSeconds;
                    ArmoryGame.Instance.BossWindowLog.Clear();
                    ShipAI.Instance?.SayMothership("You have been studied. Everything you build, we will become immune to.", "THE MOTHERSHIP AVATAR");
                }
                PendingSpawns--;
                if (group.Interval > 0f) yield return new WaitForSeconds(group.Interval);
            }
        }

        public void OnEnemyRemoved(Enemy enemy, bool killed)
        {
            if (enemy.Kind == EnemyKind.Boss && killed) bossAlive = false;
        }

        public void OnCoreDestroyed()
        {
            ShipAI.Instance?.SayShip("Core breached! Emergency repairs. Restarting the wave.", "CORE BREACHED");
            RestartWave(Mathf.Max(0, WaveIndex), 4f);
        }

        public void RestartWave(int index, float delay)
        {
            if (flow != null) StopCoroutine(flow);
            foreach (var spawner in spawners) if (spawner != null) StopCoroutine(spawner);
            spawners.Clear();
            PendingSpawns = 0;
            bossAlive = false;
            foreach (var enemy in new List<Enemy>(Enemy.All)) enemy.Die(false);
            StationCore.Instance?.Repair();
            flow = StartCoroutine(Run(Mathf.Clamp(index, 0, Waves.Count - 1), delay));
        }

        public void SkipWave() => RestartWave(WaveIndex + 1, 1f);
    }
}
