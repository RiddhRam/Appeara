using UnityEngine;

namespace Armory
{
    /// <summary>
    /// The docking deck the finale is fought on. The refinery interior the first four waves use is a corridor
    /// fight: it is tight, the core sits dead centre, and a fourteen-metre avatar with a sweeping laser has
    /// nowhere to stand and no room for the player to dodge. This is an open platform hanging in space instead -
    /// clear middle, low cover you can break line of sight behind, pads on the perimeter, and the core pushed off
    /// centre so the boss's approach lane is not straight through the thing you are defending.
    ///
    /// It is built far from the station rather than replacing it, so the refinery is preserved exactly as it is
    /// and the run can move back and forth without rebuilding anything.
    /// </summary>
    public sealed class BossArena : MonoBehaviour
    {
        public static BossArena Instance { get; private set; }

        /// <summary>Far enough that no station geometry, spawn ring or stray projectile can reach across.</summary>
        public const float StationOffset = 900f;
        /// <summary>Half-extent of the deck itself; the player is clamped well inside it.</summary>
        public const float DeckHalfExtent = 50f;
        /// <summary>How far the player may walk from the centre. Leaves a visible margin before the edge.</summary>
        public const float WalkRadius = 34f;
        public const float PadRing = 25f;

        /// <summary>Off-centre so the boss cannot path at the core and the player at once.</summary>
        public static readonly Vector3 CoreOffset = new Vector3(9f, 0f, -6f);

        public Vector3 Centre => transform.position;
        public Vector3 CorePoint => transform.position + CoreOffset;
        /// <summary>Where the player lands, across the deck from the core with the approach lane in view.</summary>
        public Vector3 PlayerStart => transform.position + new Vector3(-10f, 0f, 12f);

        /// <summary>True while the run is being fought here rather than in the refinery.</summary>
        public bool Occupied { get; private set; }

        private Transform padRoot;
        private Vector3 stationCentre, stationCorePoint;
        private float stationWalkRadius;
        private bool stationRecorded;

        public static BossArena Build(Transform parent)
        {
            var arena = new GameObject("Boss Arena").AddComponent<BossArena>();
            arena.transform.SetParent(parent, false);
            arena.transform.position = new Vector3(StationOffset, 0f, StationOffset);
            arena.Construct();
            return arena;
        }

        private void Awake() => Instance = this;

        private void Construct()
        {
            var deckColor = new Color(0.1f, 0.11f, 0.14f);
            var deck = Mats.Shape(PrimitiveType.Cube, transform, new Vector3(0f, -0.5f, 0f),
                new Vector3(DeckHalfExtent * 2f, 1f, DeckHalfExtent * 2f), Mats.Lit(deckColor), collider: true, name: "Docking Deck");
            deck.GetComponent<Renderer>().receiveShadows = true;

            // Landing strips down the long axis: they read as a deck rather than a floating slab, and they give
            // the eye something to judge the boss's distance against in a headset.
            for (int i = -2; i <= 2; i++)
                Mats.Shape(PrimitiveType.Cube, transform, new Vector3(i * 16f, 0.01f, 0f), new Vector3(0.5f, 0.02f, DeckHalfExtent * 1.7f),
                    Mats.Glow(new Color(0.15f, 0.45f, 0.6f), 0.35f), name: "Landing Strip " + i);

            BuildRail();
            BuildCover();
            BuildPads();
            gameObject.SetActive(false);
        }

        /// <summary>A low rail, not a wall: the point of fighting in space is being able to see the space.</summary>
        private void BuildRail()
        {
            for (int side = 0; side < 4; side++)
            {
                var rotation = Quaternion.Euler(0f, side * 90f, 0f);
                Vector3 centre = rotation * new Vector3(0f, 0.45f, DeckHalfExtent - 0.4f);
                var rail = Mats.Shape(PrimitiveType.Cube, transform, centre, new Vector3(DeckHalfExtent * 2f, 0.9f, 0.5f),
                    Mats.Lit(new Color(0.16f, 0.17f, 0.2f)), collider: true, name: "Deck Rail " + side);
                rail.transform.localRotation = rotation;
                var lamp = Mats.Shape(PrimitiveType.Cube, transform, centre + Vector3.up * 0.5f, new Vector3(DeckHalfExtent * 2f, 0.06f, 0.12f),
                    Mats.Glow(new Color(0.9f, 0.55f, 0.15f), 1.4f), name: "Rail Lamp " + side);
                lamp.transform.localRotation = rotation;
            }
        }

        /// <summary>
        /// Cargo containers at mixed radii and heights. They are low enough to shoot over from standing but tall
        /// enough to crouch or step behind, so there is always a sector out of the laser's line without any of
        /// them blocking the walk across the middle.
        /// </summary>
        private void BuildCover()
        {
            var crate = Mats.Lit(new Color(0.21f, 0.19f, 0.24f));
            var trim = Mats.Glow(new Color(0.9f, 0.45f, 0.2f), 0.5f);
            for (int i = 0; i < 10; i++)
            {
                float angle = i * 36f + 12f;
                float radius = 13f + (i % 3) * 6f;
                float height = i % 3 == 0 ? 2.4f : 1.5f;
                Vector3 position = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                // Never in front of the core: cover should hide the player, not the objective.
                if (Vector3.Distance(position, CoreOffset) < 7f) continue;
                position.y = height * 0.5f;
                var box = Mats.Shape(PrimitiveType.Cube, transform, position, new Vector3(3.2f, height, 2.2f), crate, collider: true, name: "Cargo Container " + i);
                box.transform.localRotation = Quaternion.Euler(0f, angle + 20f, 0f);
                var stripe = Mats.Shape(PrimitiveType.Cube, transform, position + Vector3.up * (height * 0.5f - 0.12f), new Vector3(3.24f, 0.1f, 2.24f), trim, name: "Container Stripe " + i);
                stripe.transform.localRotation = box.transform.localRotation;
            }
        }

        private void BuildPads()
        {
            padRoot = new GameObject("Arena Pads").transform;
            padRoot.SetParent(transform, false);
            var pads = new TeleportPad[4];
            for (int i = 0; i < 4; i++)
            {
                var pad = new GameObject("Arena Pad " + (i + 1)).AddComponent<TeleportPad>();
                pad.transform.SetParent(padRoot, false);
                pad.transform.localPosition = Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward * PadRing;
                pads[i] = pad;
            }
            // Paired across the deck, so a pad is an escape from whatever is standing over you.
            for (int i = 0; i < pads.Length; i++) pads[i].Linked = pads[(i + 2) % pads.Length];
        }

        /// <summary>Moves the run here: the player, the core they defend, and where the wave spawns from.</summary>
        public void Enter()
        {
            if (Occupied) return;
            var game = ArmoryGame.Instance;
            var core = StationCore.Instance;
            var director = WaveDirector.Instance;
            var locomotion = FindAnyObjectByType<Locomotion>();
            if (game == null || core == null || director == null) return;

            if (!stationRecorded)
            {
                stationCentre = director.transform.position;
                stationCorePoint = core.transform.position;
                stationWalkRadius = locomotion != null ? locomotion.ArenaRadius : WalkRadius;
                stationRecorded = true;
            }

            Occupied = true;
            gameObject.SetActive(true);
            SetStationPads(false);

            core.transform.position = CorePoint;
            // Aliens walk to the wave director's transform, so it is the core that must move, not the deck centre.
            director.transform.position = CorePoint;
            if (locomotion != null)
            {
                locomotion.LookTarget = Centre;
                locomotion.ArenaRadius = WalkRadius;
            }
            game.Rig.TeleportTo(PlayerStart, Centre - PlayerStart);
            ArmoryGame.Instance.ShowBanner("DOCKING DECK / OPEN GROUND", UiKit.Cyan);
        }

        /// <summary>Puts everything back in the refinery when the run restarts at an earlier wave.</summary>
        public void Leave()
        {
            if (!Occupied || !stationRecorded) return;
            Occupied = false;
            var game = ArmoryGame.Instance;
            var core = StationCore.Instance;
            var director = WaveDirector.Instance;
            var locomotion = FindAnyObjectByType<Locomotion>();

            if (core != null) core.transform.position = stationCorePoint;
            if (director != null) director.transform.position = stationCentre;
            if (locomotion != null)
            {
                locomotion.LookTarget = stationCentre;
                locomotion.ArenaRadius = stationWalkRadius;
            }
            SetStationPads(true);
            gameObject.SetActive(false);
            if (game != null && game.Rig != null)
                game.Rig.TeleportTo(stationCentre + new Vector3(0f, 0f, -8f), stationCentre - (stationCentre + new Vector3(0f, 0f, -8f)));
        }

        /// <summary>The refinery's own pads would otherwise still be the nearest thing to translocate to.</summary>
        private void SetStationPads(bool on)
        {
            var game = ArmoryGame.Instance;
            if (game == null) return;
            foreach (var pad in game.GetComponentsInChildren<TeleportPad>(true))
                if (!pad.transform.IsChildOf(transform)) pad.gameObject.SetActive(on);
        }
    }
}
