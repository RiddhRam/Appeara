using Armory.AI;
using Armory.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Armory
{
    /// <summary>
    /// Single scene entry point. Put this on one "Armory" object (Armory → Setup SpaceStation Scene does it).
    /// Builds the arena, core, rig, AI and wave systems at runtime so the scene file stays merge-friendly.
    /// Child speed pads placed in the scene are used as-is; otherwise four default pads are created.
    /// Debug keys: N skip wave, R restart from wave 1.
    /// </summary>
    public sealed class ArmoryGame : MonoBehaviour
    {
        public static ArmoryGame Instance { get; private set; }

        public AiSettings Ai = new AiSettings();
        [Tooltip("Build a placeholder floor disc. Turn off once the real station map is under the arena.")]
        public bool BuildFloor = true;
        public float FloorRadius = 45f;
        public float PadRadius = 5f;
        [Tooltip("Aliens deal core damage once this close to the arena centre (raise it when the core is a big prop).")]
        public float CoreReachRadius = 1.5f;
        [Tooltip("Build the placeholder core pillar. Off when the station's own centrepiece plays the core.")]
        public bool BuildCoreVisual = true;

        /// <summary>The player's own shields; aliens that reach you drain them, and they recharge after a lull.</summary>
        public readonly PlayerVitals Vitals = new PlayerVitals();
        public readonly CombatLog WaveLog = new CombatLog();
        public ArmoryRig Rig { get; private set; }

        private Hud hud;
        private WaveDirector director;

        private void Awake()
        {
            Instance = this;
            DisableOtherCameras();
            // Space outside the windows: the default sky is bright daylight, which ruins every shot.
            SpaceSky.Apply();
            // This scene prop is now spawned as the final encounter. Only hide it in the live session.
            foreach (var root in gameObject.scene.GetRootGameObjects())
                if (root.name == "Alien Animal_Fbx_7.4" && root.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                    root.SetActive(false);
            if (BuildFloor) BuildArenaFloor();
            // The imported station has no colliders; grenades and mines need something to land on.
            var floorCollider = new GameObject("Arena Floor Collider").AddComponent<BoxCollider>();
            floorCollider.transform.SetParent(transform, false);
            floorCollider.size = new Vector3(FloorRadius * 2f, 0.2f, FloorRadius * 2f);
            floorCollider.center = new Vector3(0f, -0.1f, 0f);

            var core = new GameObject("Station Core");
            core.transform.SetParent(transform, false);
            core.SetActive(false);
            var stationCore = core.AddComponent<StationCore>();
            stationCore.ReachRadius = CoreReachRadius;
            stationCore.BuildVisual = BuildCoreVisual;
            core.SetActive(true);

            if (GetComponentsInChildren<TeleportPad>().Length == 0) CreateDefaultPads();

            new GameObject("Mothership").AddComponent<Mothership>().transform.SetParent(transform, false);
            new GameObject("Performance Monitor").AddComponent<ArmoryPerformanceMonitor>().transform.SetParent(transform, false);

            Rig = new GameObject("Armory Rig").AddComponent<ArmoryRig>();
            Rig.transform.SetParent(transform, false);

            var xrPerformance = new GameObject("XR Performance Tuner").AddComponent<XRPerformanceTuner>();
            xrPerformance.transform.SetParent(transform, false);
            xrPerformance.Rig = Rig;

            // Inactive while wiring so ShipAI.Awake sees the configured settings.
            var aiObject = new GameObject("Ship AI");
            aiObject.SetActive(false);
            aiObject.transform.SetParent(transform, false);
            var ai = aiObject.AddComponent<ShipAI>();
            ai.Rig = Rig;
            ai.Settings = Ai;
            aiObject.SetActive(true);

            var locomotion = new GameObject("Locomotion").AddComponent<Locomotion>();
            locomotion.transform.SetParent(transform, false);
            locomotion.Rig = Rig;
            locomotion.LookTarget = transform.position;

            director = new GameObject("Wave Director").AddComponent<WaveDirector>();
            director.transform.SetParent(transform, false);

            hud = new GameObject("HUD").AddComponent<Hud>();
            hud.transform.SetParent(transform, false);
            hud.Rig = Rig;

            var deck = new GameObject("Mission Deck").AddComponent<MissionDeck>();
            deck.transform.SetParent(transform, false);
            deck.Rig = Rig;

            var pointer = new GameObject("UI Pointer").AddComponent<UiPointer>();
            pointer.transform.SetParent(transform, false);
            pointer.Rig = Rig;

            var prompt = new GameObject("Armory Prompt").AddComponent<ArmoryPrompt>();
            prompt.transform.SetParent(transform, false);
            prompt.Rig = Rig;

            // What the judges watch: a clean third-person render on the monitor while the rig drives the headset.
            SpectatorView.Create(Rig);
        }

        private void DisableOtherCameras()
        {
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                cam.gameObject.SetActive(false);
                Debug.Log("Armory: disabled scene camera '" + cam.name + "' (the rig provides the VR camera).");
            }
        }

        private void BuildArenaFloor()
        {
            var floor = Mats.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, -0.05f, 0f), new Vector3(FloorRadius * 2f, 0.05f, FloorRadius * 2f), Mats.Lit(new Color(0.09f, 0.1f, 0.12f)), collider: true, name: "Arena Floor");
            floor.GetComponent<Renderer>().receiveShadows = true;
            // Guide rings so distance reads in VR.
            for (int i = 1; i <= 4; i++)
            {
                float r = i * 10f;
                Mats.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.005f + i * 0.001f, 0f), new Vector3(r * 2f, 0.001f, r * 2f), Mats.Glow(new Color(0.1f, 0.4f, 0.6f), 0.08f), name: "Range Ring " + r + "m");
            }
        }

        private void CreateDefaultPads()
        {
            for (int i = 0; i < 4; i++)
            {
                var pad = new GameObject("Speed Pad " + (i + 1));
                pad.transform.SetParent(transform, false);
                pad.transform.localPosition = Quaternion.Euler(0f, 180f + i * 90f, 0f) * Vector3.forward * PadRadius;
                pad.AddComponent<TeleportPad>();
            }
        }

        /// <summary>Aliens within reach of the player claw at them; shields recover once you break away.</summary>
        private void UpdateVitals()
        {
            float now = Time.time;
            Vitals.Tick(Time.deltaTime, now);
            if (Rig == null) return;

            Vector3 feet = Rig.FeetPosition;
            foreach (var enemy in Enemy.All)
            {
                if (enemy == null || !enemy.Alive) continue;
                if (Vector3.Distance(enemy.Center, feet + Vector3.up) > enemy.Radius + 1.4f) continue;
                float taken = Vitals.Damage(enemy.CoreDamage * 0.6f, now);
                if (taken <= 0f) continue;
                Effects.Flash(feet + Vector3.up * 1.4f, UiKit.Alien, 1.2f);
                ProceduralSfx.PlayAt(ProceduralSfx.Hit, feet + Vector3.up, 0.8f);
                if (Vitals.Down) ShipAI.Instance?.SayShip("Shields down. Fall back to a pad.", "SHIELDS DOWN");
                break;
            }
        }

        public void RecordDamage(ParsedWeapon weapon, float amount)
        {
            WaveLog.Record(weapon, amount);
        }

        public void OnWeaponEquipped(ParsedWeapon spec) => Mothership.Instance?.BeginWeaponAnalysis(spec);

        public void ShowBanner(string text, Color color) => hud?.ShowBanner(text, color);

        private void Update()
        {
            UpdateVitals();
            var keyboard = Keyboard.current;
            if (keyboard == null || Rig.TextEntryActive) return;
            if (keyboard.nKey.wasPressedThisFrame) director.SkipWave();
            if (keyboard.rKey.wasPressedThisFrame)
            {
                Mothership.Instance.Clear();
                director.RestartWave(0, 2f);
            }
        }
    }
}
