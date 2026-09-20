using System.Collections;
using System.Collections.Generic;
using Armory.AI;
using Armory.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Armory
{
    /// <summary>Animated final encounter. Owns its attack clock, organs, plating and all spawned threats.</summary>
    public sealed class HiveAvatar : MonoBehaviour, IHiveAttackEvents
    {
        public static HiveAvatar Active { get; private set; }
        public readonly HiveAvatarState Rules = new HiveAvatarState();
        public Enemy Body { get; private set; }
        public string Phase { get; private set; } = "ARRIVING";
        public string CurrentAttack { get; private set; } = "";
        public Vector3 AimPoint => transform.TransformPoint(new Vector3(0f, 6f, 4f));
        public bool Enraged => Body != null && Body.Health <= Body.MaxHealth * HiveAvatarState.EnrageFraction;
        public bool Ready { get; private set; }

        /// <summary>
        /// How long the body must survive its own death. The settle clip runs 4.5s and the controller blends into
        /// it, so Enemy.Die destroying the boss at 3.5s cut the finale off in the middle of the collapse.
        /// </summary>
        public const float DeathSequenceSeconds = 5.25f;

        // Authored clip timings from ANIMATION_HANDOFF.md. They are the fallback only: every attack waits on the
        // animation event and uses these to notice the event never came.
        private const float StompImpactAt = 1.50f, StompLength = 2.30f;
        private const float FireballReleaseAt = 1.25f, FireballLength = 1.75f;
        private const float LaserStartAt = 1.50f, LaserEndAt = 3.50f, LaserLength = 4.50f;
        private const float EnrageLength = 1.50f;
        /// <summary>Slack for the controller's blend into an attack before its clip timeline starts.</summary>
        private const float EventGrace = 0.75f;
        /// <summary>Warning drawn on the deck before the wind-up begins; VR players need to see it and step.</summary>
        private const float StompWarning = 1.2f, FireballWarning = 1f, LaserWarning = 1f, SweepWarning = 2.4f;
        private const float FireballFlight = 0.9f;
        /// <summary>The authored shockwave ring grows to roughly this radius, so it can be scaled onto the hitbox.</summary>
        private const float ShockwaveArtRadius = 7.7f;

        private static readonly Color HiveColor = new Color(1f, 0.25f, 0.48f);
        private static readonly string[] OrganNames = { "LEFT CLAW", "RIGHT CLAW", "SPORE SAC", "CREST" };
        private static readonly int BaseColorParam = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock plateTint;
        private static readonly int MovingParam = Animator.StringToHash("Moving");
        private static readonly int ClawParam = Animator.StringToHash("Claw");
        private static readonly int BiteParam = Animator.StringToHash("Bite");
        private static readonly int DieParam = Animator.StringToHash("Die");
        private static readonly int StompParam = Animator.StringToHash("Stomp");
        private static readonly int FireballParam = Animator.StringToHash("Fireball");
        private static readonly int LaserParam = Animator.StringToHash("Laser");
        private static readonly int EnrageParam = Animator.StringToHash("Enrage");
        private static readonly int EnragedParam = Animator.StringToHash("Enraged");
        private readonly Transform[] organs = new Transform[4];
        private readonly Transform[] anchors = new Transform[4];
        private readonly List<Renderer> plates = new List<Renderer>();
        private readonly HiveHazards hazards = new HiveHazards();
        private HiveAvatarAssets assets;
        private HiveAvatarRig rig;
        private Animator animator;
        private HiveAvatarEvents events;
        /// <summary>True once the authored prefab's animator controller drives the body instead of raw clips.</summary>
        private bool controllerDriven;
        private Transform model;
        private Transform mouth;
        private Transform hazardRoot;
        private TextMeshPro title, status;
        private Material healthBar;
        private PlayableGraph animationGraph;
        private AnimationClipPlayable animation;
        private AnimationClip currentClip;
        private bool loopClip;
        private float clipTime;
        private bool dying;
        private int attackCursor;
        /// <summary>Set when the wind-up's organ is shot off, so the attack drops instead of resolving anyway.</summary>
        private bool attackCancelled;
        private readonly bool[] beatFired = new bool[5];
        private Vector3 laserCenter = Vector3.forward;
        private float laserReach = 18f;
        private MothershipSpawnTrace spawnTrace;
        private Sentry.ISpan arrivalSpan;
        private float arrivalStarted;

        public void Initialize(Enemy body, HiveAvatarAssets presentation, MothershipSpawnTrace trace = null)
        {
            spawnTrace = trace;
            Active = this;
            Body = body;
            Body.Avatar = this;
            Body.ExternallyDriven = true;
            assets = presentation;
            spawnTrace?.RecordPresentation(assets != null && assets.Model != null,
                assets != null && assets.Idle != null && assets.Walk != null);
            hazardRoot = new GameObject("Hive Hazards").transform;
            hazardRoot.SetParent(transform.parent, false);
            if (assets != null && (assets.Visual != null || assets.Model != null)) BuildModel();
            else Mats.Shape(PrimitiveType.Capsule, transform, Vector3.up * 6f, new Vector3(8f, 6f, 8f), Mats.Lit(HiveColor), name: "Avatar Fallback");
            BuildTargets();
            BuildHealthDisplay();
            StartCoroutine(Encounter());
        }

        private void BuildModel()
        {
            // The authored prefab is the FBX plus chitin scales, organ sockets and an animator controller.
            // Fall back to the bare model so an older HiveAvatarAssets asset still spawns a boss.
            model = Instantiate(assets.Visual != null ? assets.Visual : assets.Model, transform).transform;
            model.name = "Hive Avatar Body";
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;
            foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
            animator = model.GetComponentInChildren<Animator>();
            controllerDriven = animator != null && animator.runtimeAnimatorController != null;
            if (controllerDriven)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // The receiver has to go on the animator's own GameObject; Unity delivers clip events nowhere else.
                events = HiveAvatarEvents.Bind(animator, this);
                // Settle into the controller's default state now: the bake below measures whatever pose is applied.
                animator.Rebind();
                animator.Update(0f);
            }
            else Motion(HiveMotion.Idle);
            // Imported renderer bounds include every animation. Bake this pose to place the feet on the deck.
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin != null)
            {
                var mesh = new Mesh();
                // Include the renderer scale so vertices remain in its local space before TransformPoint.
                // This FBX has a 100x renderer transform; the default overload would apply that scale twice.
                skin.BakeMesh(mesh, true);
                var bounds = mesh.bounds;
                Vector3 min = model.InverseTransformPoint(skin.transform.TransformPoint(bounds.min));
                Vector3 max = model.InverseTransformPoint(skin.transform.TransformPoint(bounds.max));
                float scale = assets.Height / Mathf.Max(1f, Mathf.Abs(max.y - min.y));
                model.localScale = Vector3.one * scale;
                model.localPosition = new Vector3(-(min.x + max.x) * 0.5f, -Mathf.Min(min.y, max.y), -(min.z + max.z) * 0.5f) * scale;
                Destroy(mesh);
            }
            rig = HiveAvatarRig.Bind(model);
            if (rig.Authored) rig.HideAnchorCores();
            for (int i = 0; i < anchors.Length; i++) anchors[i] = rig.Anchors[i];
            if (!rig.IsComplete) Debug.LogWarning("Hive Avatar: model is missing organ anchors; those organs stay at their fallback offsets.");
            foreach (var candidate in model.GetComponentsInChildren<Transform>(true))
                if (candidate.name == "Mouth_Socket") { mouth = candidate; break; }
            if (mouth == null && controllerDriven)
                Debug.LogWarning("Hive Avatar: no Mouth_Socket on the model; the fireball and laser leave the torso instead.");
        }

        private void BuildTargets()
        {
            // Torso capsule rather than a 5x7x10 box: the old box enclosed the bone-anchored organs, so every
            // shot at a claw or the sac hit the body first and the organs could not be destroyed.
            var bodyCollider = gameObject.AddComponent<CapsuleCollider>();
            bodyCollider.center = new Vector3(0f, HiveTargets.TorsoCenterY, 0f);
            bodyCollider.radius = HiveTargets.TorsoRadius;
            bodyCollider.height = HiveTargets.TorsoHeight;
            bodyCollider.direction = 1;
            Vector3[] positions = { new Vector3(-4f, 3f, 8f), new Vector3(4f, 3f, 8f), new Vector3(0f, 9f, 3f), new Vector3(0f, 8f, 11f) };
            for (int i = 0; i < organs.Length; i++)
            {
                var orb = Mats.Shape(PrimitiveType.Sphere, transform, HiveTargets.Protrude(positions[i]), Vector3.one * (HiveTargets.OrganRadius * 2f),
                    Mats.Lit(HiveColor, 2f), collider: true, name: OrganNames[i]);
                organs[i] = orb.transform;
                var label = UiKit.Text(orb.transform, "Target Label", new Vector3(0f, 0.85f, 0f), 0.15f,
                    UiKit.Label, Color.white, TextAlignmentOptions.Center, width: 4f);
                label.text = OrganNames[i];
                label.gameObject.AddComponent<Billboard>();
                var ring = Mats.Line(orb.transform, HiveColor, 0.05f);
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.positionCount = 32;
                for (int j = 0; j < 32; j++)
                {
                    float a = j * Mathf.PI / 16f;
                    ring.SetPosition(j, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.68f);
                }
            }
            if (rig != null && rig.Plates.Length > 0)
            {
                // The prefab already carries authored scales on the skeleton; building cubes on top would double them.
                plates.AddRange(rig.Plates);
                return;
            }
            for (int i = 0; i < 8; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                var plate = Mats.Shape(PrimitiveType.Cube, transform, new Vector3(side * 2.9f, 6.8f + i / 2 * 0.65f, 2f - i / 2 * 1.6f),
                    new Vector3(1.4f, 0.35f, 2.8f), Mats.Lit(new Color(0.18f, 0.13f, 0.25f), 0.2f), name: "Adaptive Scale");
                plate.transform.localRotation = Quaternion.Euler(0f, side * 20f, side * 30f);
                plates.Add(plate.GetComponent<Renderer>());
            }
        }

        private void BuildHealthDisplay()
        {
            var panel = new GameObject("Hive Health").transform;
            panel.SetParent(transform, false);
            panel.localPosition = new Vector3(0f, 16f, 0f);
            panel.gameObject.AddComponent<Billboard>();
            UiKit.Panel(panel, "Boss Panel", new Vector2(12f, 2.3f), 0.55f);
            title = UiKit.Text(panel, "Title", new Vector3(0f, 0.9f, 0f), 0.45f, UiKit.Label, HiveColor, TextAlignmentOptions.Center, width: 11f);
            title.text = "HIVE AVATAR";
            healthBar = UiKit.Bar(panel, "Integrity", new Vector3(-5.2f, 0f, 0f), new Vector2(10.4f, 0.2f), 40, HiveColor);
            status = UiKit.Text(panel, "Status", new Vector3(0f, -0.55f, 0f), 0.28f, UiKit.Mono, Color.white, TextAlignmentOptions.Center, width: 11f);
        }

        private void Update()
        {
            if (currentClip != null && animationGraph.IsValid())
            {
                clipTime += Time.deltaTime;
                animation.SetTime(loopClip ? clipTime % currentClip.length : Mathf.Min(clipTime, currentClip.length));
                animationGraph.Evaluate(0f);
            }
            if (Body == null || healthBar == null) return;
            healthBar.SetFloat("_Fill", Mathf.Clamp01(Body.Health / Body.MaxHealth));
            status.text = dying ? "SIGNAL LOST" : $"{Phase}  /  {Rules.BrokenCount}/4 ORGANS DISABLED\n" +
                (Rules.Plating == null ? "PLATING: UNADAPTED" : "RESISTS " + Rules.Plating.ToUpperInvariant() + " / SWITCH WEAPON");
        }

        private void LateUpdate()
        {
            for (int i = 0; i < organs.Length; i++)
            {
                if (anchors[i] == null || organs[i] == null) continue;
                Vector3 anchor = transform.InverseTransformPoint(anchors[i].position) + Vector3.up * (i == 2 ? 1.1f : 0.45f);
                organs[i].localPosition = HiveTargets.Protrude(anchor);
            }
        }

        private enum HiveMotion { Idle, Walk, Claw, Bite, Die, Stomp, Fireball, Laser, Enrage }

        /// <summary>
        /// The one entry point for body motion. The authored prefab ships an animator controller; the bare model
        /// has no controller and keeps the manual playable path. Running both would have them fight over the
        /// same Animator, so the encounter never names a clip directly.
        /// </summary>
        private void Motion(HiveMotion motion)
        {
            // Death is terminal: an Any State trigger fired afterwards would pull the boss out of its death clip.
            if (dying && motion != HiveMotion.Die) return;
            if (controllerDriven)
            {
                switch (motion)
                {
                    case HiveMotion.Walk: animator.SetBool(MovingParam, true); break;
                    case HiveMotion.Idle: animator.SetBool(MovingParam, false); break;
                    case HiveMotion.Claw: animator.SetTrigger(ClawParam); break;
                    case HiveMotion.Bite: animator.SetTrigger(BiteParam); break;
                    case HiveMotion.Stomp: animator.SetTrigger(StompParam); break;
                    case HiveMotion.Fireball: animator.SetTrigger(FireballParam); break;
                    case HiveMotion.Laser: animator.SetTrigger(LaserParam); break;
                    // The bool picks the faster idle and walk for good; the trigger is the one-shot roar.
                    case HiveMotion.Enrage: animator.SetBool(EnragedParam, true); animator.SetTrigger(EnrageParam); break;
                    case HiveMotion.Die: animator.SetBool(MovingParam, false); animator.SetTrigger(DieParam); break;
                }
                return;
            }
            AnimationClip clip = null;
            bool loop = false;
            if (assets != null)
                switch (motion)
                {
                    case HiveMotion.Walk: clip = assets.Walk; loop = true; break;
                    case HiveMotion.Idle: clip = assets.Idle; loop = true; break;
                    // The bare FBX has no authored attack clips, so the old imported ones stand in for them and
                    // the gameplay below still runs on the handoff's timings.
                    case HiveMotion.Claw:
                    case HiveMotion.Stomp:
                    case HiveMotion.Enrage: clip = assets.Attack; break;
                    case HiveMotion.Bite:
                    case HiveMotion.Fireball:
                    case HiveMotion.Laser: clip = assets.Spit; break;
                    case HiveMotion.Die: clip = assets.Death; break;
                }
            Play(clip, loop);
        }

        private void Play(AnimationClip clip, bool loop)
        {
            if (clip == null || model == null) return;
            if (animationGraph.IsValid()) animationGraph.Destroy();
            animationGraph = PlayableGraph.Create("Hive Avatar Animation");
            animationGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null) animator = model.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animation = AnimationClipPlayable.Create(animationGraph, clip);
            animation.SetSpeed(0f);
            AnimationPlayableOutput.Create(animationGraph, "Body", animator).SetSourcePlayable(animation);
            currentClip = clip;
            loopClip = loop;
            clipTime = 0f;
            animationGraph.Play();
            animationGraph.Evaluate(0f);
        }

        private IEnumerator Encounter()
        {
            arrivalStarted = Time.realtimeSinceStartup;
            arrivalSpan = spawnTrace?.StartSpan("gameplay.avatar_arrival", "Mothership Avatar enters the arena");
            Vector3 start = transform.position;
            Vector3 outward = (start - Body.Target).normalized;
            float coreReach = StationCore.Instance != null ? StationCore.Instance.ReachRadius : 5f;
            Vector3 destination = Body.Target + outward * Mathf.Max(27f, coreReach + 20f);
            Motion(HiveMotion.Walk);
            for (float t = 0f; t < 4f; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, destination, Mathf.SmoothStep(0f, 1f, t / 4f));
                yield return null;
            }
            transform.position = destination;
            Ready = true;
            bool inheritedCounter = Mothership.Instance != null && Mothership.Instance.ActiveDefendedPrimitive != null;
            if (inheritedCounter)
                Adapt(Mothership.Instance.ActiveDefendedPrimitive);
            spawnTrace?.FinishSpan(arrivalSpan);
            arrivalSpan = null;
            spawnTrace?.Ready(Time.realtimeSinceStartup - arrivalStarted, organs.Length, inheritedCounter);
            spawnTrace = null;
            Motion(HiveMotion.Idle);
            ArmoryGame.Instance?.ShowBanner("HIVE AVATAR / BREAK THE GLOWING ORGANS", HiveColor);
            while (Body.Alive && !dying)
            {
                Phase = Enraged ? "ENRAGED" : "HUNTING";
                yield return new WaitForSeconds(Enraged ? 2.8f : 4.5f);
                // Every gate below is re-checked after the wait: the player may have killed the boss during it,
                // and an attack trigger fired after death leaves the death state through the Any State edge.
                if (dying || !Body.Alive) yield break;
                if (Rules.ShouldEnrage(Body.Health, Body.MaxHealth)) yield return Enrage();
                if (dying || !Body.Alive) yield break;
                if (!HiveMoves.TryNext(Rules, ref attackCursor, out var move)) continue;
                CurrentAttack = move.ToString();
                switch (move)
                {
                    case HiveMove.Stomp: yield return Stomp(); break;
                    case HiveMove.Fireball: yield return Fireball(); break;
                    case HiveMove.Laser: yield return Laser(); break;
                    case HiveMove.ClawSweep: yield return Sweep(); break;
                    default: yield return LaunchThreats(move); break;
                }
                CurrentAttack = "";
                Motion(HiveMotion.Idle);
            }
        }

        private IEnumerator Enrage()
        {
            Phase = "ENRAGED";
            ArmoryGame.Instance?.ShowBanner("HIVE AVATAR ENRAGED / IT HITS FASTER NOW", HiveColor);
            Motion(HiveMotion.Enrage);
            Effects.Burst(AimPoint, HiveColor, 6f);
            ProceduralSfx.PlayAt(ProceduralSfx.Hit, AimPoint, 1f);
            yield return new WaitForSeconds(EnrageLength);
        }

        /// <summary>
        /// Ground slam. The ring is drawn a beat before the wind-up starts and never moves afterwards, so the
        /// warning the player reacted to is the warning that resolves - chasing them with it would be undodgeable.
        /// </summary>
        private IEnumerator Stomp()
        {
            Vector3 point = PlayerGround();
            Phase = "STOMP / GET OUT OF THE RING";
            ArmoryGame.Instance?.ShowBanner("GROUND STOMP / MOVE OR TELEPORT CLEAR", HiveColor);
            var marker = hazards.Track(HiveTelegraph.Create(hazardRoot, point, HiveDamage.StompRadius, HiveColor));
            yield return Wind(HiveMove.Stomp, marker, StompWarning, point, 0f, 0.4f);
            if (dying || attackCancelled) { hazards.Release(marker); yield break; }
            ArmBeats();
            Motion(HiveMotion.Stomp);
            yield return WaitFor(HiveBeat.StompImpact, StompImpactAt, marker, 0.4f, 1f);
            if (!dying)
            {
                ArtVfx.Play("BossShockwave", point, Quaternion.identity, HiveDamage.StompRadius / ShockwaveArtRadius);
                Effects.Burst(point + Vector3.up * 0.5f, HiveColor, HiveDamage.StompRadius);
                ProceduralSfx.PlayAt(ProceduralSfx.Hit, point + Vector3.up, 1f);
                if (Vector3.Distance(PlayerGround(), point) < HiveDamage.StompRadius) HitPlayer(HiveDamage.Stomp);
            }
            hazards.Release(marker);
            yield return WaitFor(HiveBeat.Recovered, StompLength - StompImpactAt);
        }

        /// <summary>Lobbed from the mouth socket onto the telegraphed ring, with a short flight to run out of.</summary>
        private IEnumerator Fireball()
        {
            Vector3 point = PlayerGround();
            Phase = "FIREBALL / KEEP MOVING";
            ArmoryGame.Instance?.ShowBanner("FIREBALL INCOMING / LEAVE THE MARKED GROUND", HiveColor);
            var marker = hazards.Track(HiveTelegraph.Create(hazardRoot, point, HiveDamage.FireballRadius, HiveColor));
            yield return Wind(HiveMove.Fireball, marker, FireballWarning, point, 0f, 0.3f);
            if (dying || attackCancelled) { hazards.Release(marker); yield break; }
            ArmBeats();
            Motion(HiveMotion.Fireball);
            yield return WaitFor(HiveBeat.FireballRelease, FireballReleaseAt, marker, 0.3f, 0.7f);
            if (dying) { hazards.Release(marker); yield break; }
            Vector3 from = MouthPoint;
            Vector3 impact = point + Vector3.up * 0.6f;
            var ball = hazards.Track("BossFireball", ArtVfx.Play("BossFireball", from, Quaternion.identity, 1.4f));
            Own(ball);
            for (float t = 0f; t < FireballFlight; t += Time.deltaTime)
            {
                if (dying) break;
                float k = t / FireballFlight;
                // An arc rather than a straight line: a flat shot from a mouth twelve metres up reads as a miss.
                if (ball != null) ball.transform.position = Vector3.Lerp(from, impact, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 2.5f);
                if (marker != null) marker.SetProgress(0.7f + k * 0.3f);
                yield return null;
            }
            hazards.Release(ball);
            if (!dying)
            {
                ArtVfx.Play("BossShockwave", point, Quaternion.identity, HiveDamage.FireballRadius / ShockwaveArtRadius);
                Effects.Burst(impact, HiveColor, HiveDamage.FireballRadius);
                ProceduralSfx.PlayAt(ProceduralSfx.Hit, impact, 0.9f);
                if (Vector3.Distance(PlayerGround(), point) < HiveDamage.FireballRadius) HitPlayer(HiveDamage.Fireball);
            }
            hazards.Release(marker);
            yield return WaitFor(HiveBeat.Recovered, FireballLength - FireballReleaseAt);
        }

        /// <summary>
        /// Mouth beam. The charge sits on the socket for the whole wind-up so the player can see which end of the
        /// arena is about to be cut, then the beam itself is the telegraph while it sweeps.
        /// </summary>
        private IEnumerator Laser()
        {
            Vector3 point = PlayerGround();
            Phase = "LASER SWEEP / BREAK THE LINE";
            ArmoryGame.Instance?.ShowBanner("MOUTH LASER CHARGING / GET OUT OF THE SWEEP", HiveColor);
            var marker = hazards.Track(HiveTelegraph.Create(hazardRoot, point, HiveDamage.LaserWarnRadius, HiveColor));
            yield return Wind(HiveMove.Laser, marker, LaserWarning, point, 0f, 0.3f);
            if (dying || attackCancelled) { hazards.Release(marker); yield break; }
            ArmBeats();
            Motion(HiveMotion.Laser);
            var charge = hazards.Track("BossLaserCharge", ArtVfx.Play("BossLaserCharge", MouthPoint, Quaternion.identity, 1.5f));
            Own(charge);
            yield return WaitFor(HiveBeat.LaserStart, LaserStartAt, marker, 0.3f, 1f, charge);
            hazards.Release(charge);
            hazards.Release(marker);
            if (dying) yield break;

            // The sweep is centred on where the player stood when the beam lit, and reaches as far out as they
            // were standing, so the arc actually crosses them instead of passing over their head.
            Vector3 flat = PlayerGround() - transform.position;
            flat.y = 0f;
            laserCenter = flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward;
            laserReach = Mathf.Clamp(flat.magnitude, 8f, 40f);
            var beam = hazards.Track("BossLaserBeam", ArtVfx.Play("BossLaserBeam", MouthPoint, Quaternion.identity));
            Own(beam);
            ProceduralSfx.PlayAt(ProceduralSfx.Hit, MouthPoint, 0.8f);
            float deadline = Time.time + (LaserEndAt - LaserStartAt) + EventGrace;
            while (!beatFired[(int)HiveBeat.LaserEnd] && Time.time < deadline && !dying)
            {
                Vector3 origin = MouthPoint;
                Vector3 tip = BeamTip(origin);
                DrawBeam(beam, origin, tip);
                if (HiveDamage.DistanceToBeam(PlayerGround() + Vector3.up, origin, tip) < HiveDamage.LaserRadius)
                    HitPlayer(HiveDamage.LaserTick);
                yield return null;
            }
            hazards.Release(beam);
            yield return WaitFor(HiveBeat.Recovered, LaserLength - LaserEndAt);
        }

        /// <summary>The original claw sweep, kept as the right claw's beat so all four organs still silence one.</summary>
        private IEnumerator Sweep()
        {
            Vector3 point = PlayerGround();
            Phase = "SWEEP / TELEPORT OUT OF THE RING";
            ArmoryGame.Instance?.ShowBanner("CLAW SWEEP / TELEPORT OUT OF THE RING", HiveColor);
            var marker = hazards.Track(HiveTelegraph.Create(hazardRoot, point, HiveDamage.ClawSweepRadius, HiveColor));
            yield return Wind(HiveMove.ClawSweep, marker, SweepWarning, point, 0f, 1f);
            if (dying || attackCancelled) { hazards.Release(marker); yield break; }
            Motion(HiveMotion.Claw);
            Effects.Lightning(AimPoint, point + Vector3.up, HiveColor);
            Effects.Burst(point + Vector3.up * 0.5f, HiveColor, HiveDamage.ClawSweepRadius * 2f);
            hazards.Release(marker);
            if (Vector3.Distance(PlayerGround(), point) < HiveDamage.ClawSweepRadius) HitPlayer(HiveDamage.ClawSweep);
            yield return new WaitForSeconds(1f);
        }

        private IEnumerator LaunchThreats(HiveMove move)
        {
            bool spores = move == HiveMove.Spores;
            Phase = spores ? "SPORES / SHOOT BEFORE THEY HATCH" : "WRECK / SHOOT TO PROTECT CORE";
            ArmoryGame.Instance?.ShowBanner(spores ? "SPORE PODS / SHOOT THEM DOWN" : "INCOMING WRECK / SHOOT IT DOWN", HiveColor);
            Motion(HiveMotion.Bite);
            yield return new WaitForSeconds(0.8f);
            if (dying || !Rules.CanAttack(move)) yield break;
            int count = spores ? 3 : 1;
            for (int i = 0; i < count; i++)
            {
                Vector3 destination = spores ? Body.Target + Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 14f : Body.Target + Vector3.up * 2f;
                hazards.Track(HiveThreat.Launch(hazardRoot, AimPoint, destination, spores));
            }
        }

        /// <summary>
        /// Turns to face the target while the ring fills. The boss used to attack in whichever direction it
        /// happened to arrive facing, which made every wind-up unreadable from inside the ring.
        /// </summary>
        private IEnumerator Wind(HiveMove move, HiveTelegraph marker, float seconds, Vector3 point, float from, float to)
        {
            attackCancelled = false;
            Quaternion start = transform.rotation;
            Vector3 flat = point - transform.position;
            flat.y = 0f;
            Quaternion facing = flat.sqrMagnitude > 0.01f ? Quaternion.LookRotation(flat) : start;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (dying) yield break;
                // Shooting the organ during the wind-up has always cancelled the attack; that feedback loop is
                // the reason the player shoots organs rather than the body.
                if (!Rules.CanAttack(move)) { attackCancelled = true; yield break; }
                float k = t / seconds;
                transform.rotation = Quaternion.Slerp(start, facing, Mathf.SmoothStep(0f, 1f, k));
                if (marker != null) marker.SetProgress(Mathf.Lerp(from, to, k));
                yield return null;
            }
            transform.rotation = facing;
        }

        /// <summary>Animation-event beats inside one attack clip.</summary>
        private enum HiveBeat { StompImpact, FireballRelease, LaserStart, LaserEnd, Recovered }

        private void ArmBeats()
        {
            for (int i = 0; i < beatFired.Length; i++) beatFired[i] = false;
        }

        /// <summary>
        /// Waits for an authored animation event, with the clip's own timing as a deadline. The events ship with
        /// DontRequireReceiver, so a broken hook is silent by design: without this the encounter would hang on an
        /// event that never arrives, and nobody would know which of the two happened.
        /// </summary>
        private IEnumerator WaitFor(HiveBeat beat, float authoredAt, HiveTelegraph marker = null,
            float from = 0f, float to = 1f, GameObject follow = null)
        {
            float deadline = Time.time + authoredAt + EventGrace;
            float span = Mathf.Max(0.01f, authoredAt);
            float started = Time.time;
            while (!beatFired[(int)beat] && Time.time < deadline)
            {
                if (dying) yield break;
                if (marker != null) marker.SetProgress(Mathf.Lerp(from, to, (Time.time - started) / span));
                if (follow != null) follow.transform.position = MouthPoint;
                yield return null;
            }
            if (marker != null) marker.SetProgress(to);
            if (beatFired[(int)beat] || !controllerDriven) yield break;
            Debug.LogWarning($"Hive Avatar: animation event for {beat} never arrived within {authoredAt + EventGrace:0.00}s. " +
                "The attack fell back to its authored timing; check HiveAvatarEvents sits on the Animator GameObject and the clip still carries the event.");
        }

        private Vector3 MouthPoint => mouth != null ? mouth.position : AimPoint;

        /// <summary>
        /// Where the beam burns the deck this frame. The authored sweep turns the head through 120 degrees but
        /// keeps it level, and a level beam leaving a mouth twelve metres up never reaches the floor, so only the
        /// sweep's yaw is taken from the socket and the pitch is solved here against the deck.
        /// </summary>
        private Vector3 BeamTip(Vector3 origin)
        {
            Vector3 body = transform.forward;
            body.y = 0f;
            Vector3 socket = mouth != null ? mouth.forward : body;
            socket.y = 0f;
            float yaw = body.sqrMagnitude > 0.0001f && socket.sqrMagnitude > 0.0001f
                ? Vector3.SignedAngle(body, socket, Vector3.up) : 0f;
            Vector3 aim = Quaternion.Euler(0f, yaw, 0f) * laserCenter;
            Vector3 tip = origin + aim * laserReach;
            tip.y = Body != null ? Body.Target.y : 0f;
            return tip;
        }

        private static void DrawBeam(GameObject beam, Vector3 origin, Vector3 tip)
        {
            if (beam == null) return;
            Vector3 along = tip - origin;
            if (along.sqrMagnitude < 0.0001f) return;
            beam.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(along.normalized));
            var line = beam.GetComponent<LineRenderer>();
            if (line == null) return;
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.forward * along.magnitude);
            // The drawn beam has to be at least as wide as the volume that hurts. The authored ribbon is 16 cm,
            // sized for a close-up, and being burned by something you cannot see reads as a bug.
            line.widthMultiplier = HiveDamage.LaserRadius * 1.8f;
        }

        /// <summary>
        /// Stops the pool reclaiming an effect the encounter is still steering. The beam is a LineRenderer with
        /// no particles, so ArtVfx would time it out on the two-second fallback halfway through the sweep.
        /// </summary>
        private static void Own(GameObject effect)
        {
            if (effect == null) return;
            var timer = effect.GetComponent<VfxLifetime>();
            if (timer != null) timer.enabled = false;
        }

        /// <summary>
        /// Boss attacks hit the marine, not the station. The old sweep drained core integrity with a comment that
        /// the marine had no HP; PlayerVitals changed that, and a boss whose attacks cannot threaten the player
        /// standing in front of it is not a boss.
        /// </summary>
        private static float HitPlayer(float amount)
        {
            var game = ArmoryGame.Instance;
            if (game == null) return 0f;
            float taken = game.Vitals.Damage(amount, Time.time);
            if (taken <= 0f) return 0f;
            Vector3 feet = game.Rig != null ? game.Rig.FeetPosition : Vector3.zero;
            Effects.Flash(feet + Vector3.up * 1.4f, HiveColor, 1.8f);
            ProceduralSfx.PlayAt(ProceduralSfx.Hit, feet + Vector3.up, 0.9f);
            if (game.Vitals.Down) ShipAI.Instance?.SayShip("Shields down. Fall back to a pad.", "SHIELDS DOWN");
            return taken;
        }

        void IHiveAttackEvents.StompImpact() => beatFired[(int)HiveBeat.StompImpact] = true;
        void IHiveAttackEvents.FireballRelease() => beatFired[(int)HiveBeat.FireballRelease] = true;
        void IHiveAttackEvents.LaserStart() => beatFired[(int)HiveBeat.LaserStart] = true;
        void IHiveAttackEvents.LaserEnd() => beatFired[(int)HiveBeat.LaserEnd] = true;
        void IHiveAttackEvents.AttackRecovered() => beatFired[(int)HiveBeat.Recovered] = true;

        private Vector3 PlayerGround()
        {
            var rig = ArmoryGame.Instance != null ? ArmoryGame.Instance.Rig : null;
            Vector3 point = rig != null ? rig.Head.transform.position : Body.Target;
            point.y = Body.Target.y;
            return point;
        }

        /// <summary>Direct projectile/beam hits carry a world point; splash/chain damage does not break organs.</summary>
        public float ResolveDamage(ParsedWeapon weapon, float damage, Vector3? point)
        {
            if (dying || !Ready || damage <= 0f) return 0f;
            float amount = damage * Rules.DamageScale(weapon);
            if (point.HasValue)
                for (int i = 0; i < organs.Length; i++)
                {
                    if (Rules.IsBroken((HiveOrgan)i) || Vector3.Distance(organs[i].position, point.Value) > HiveTargets.RouteRadius) continue;
                    if (Rules.HitOrgan((HiveOrgan)i, amount))
                    {
                        organs[i].gameObject.SetActive(false);
                        Effects.Burst(organs[i].position, HiveColor, 3f);
                        ArmoryGame.Instance?.ShowBanner(OrganNames[i] + " DESTROYED / ATTACK DISABLED", Color.cyan);
                    }
                    return amount * 1.75f;
                }
            return amount;
        }

        public void Adapt(string primitive)
        {
            if (!Ready || dying || !AdaptationRules.IsPrimitive(primitive)) return;
            Rules.Adapt(primitive);
            Color color = primitive == "cryo" ? Color.cyan : primitive == "electric" ? Color.yellow :
                primitive == "plasma" ? new Color(0.8f, 0.25f, 1f) : primitive == "explosive" ? new Color(1f, 0.4f, 0.05f) : HiveColor;
            bool authoredPlates = rig != null && rig.Authored;
            foreach (var plate in plates)
            {
                if (plate == null) continue;
                Effects.Flash(plate.transform.position, color, 1.8f);
                if (authoredPlates) Tint(plate, color);
                else plate.sharedMaterial = Mats.Lit(color, 0.8f);
            }
            ArmoryGame.Instance?.ShowBanner("PLATING: " + primitive.ToUpperInvariant() + " / SWITCH WEAPON", color);
        }

        /// <summary>
        /// Recolours an authored chitin scale without throwing its material away. HiveChitin has no emission
        /// keyword, and a property block cannot switch one on, so the scales take the resist hue in base colour
        /// and keep their texture and smoothness; the flash carries the moment of adaptation.
        /// </summary>
        private static void Tint(Renderer plate, Color color)
        {
            if (plateTint == null) plateTint = new MaterialPropertyBlock();
            plate.GetPropertyBlock(plateTint);
            plateTint.SetColor(BaseColorParam, color * 0.6f);
            plate.SetPropertyBlock(plateTint);
        }

        public void Defeated(bool killed)
        {
            CancelSpawnTrace(killed ? "defeated_before_ready" : "removed_before_ready");
            dying = true;
            Ready = false;
            Phase = "DEFEATED";
            StopAllCoroutines();
            // A queued trigger survives until a transition consumes it, so an unconsumed Stomp would fire off Any
            // State the instant the death clip started and stand the boss back up mid-collapse.
            ClearAttackTriggers();
            ClearThreats();
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var organ in organs) if (organ != null) organ.gameObject.SetActive(false);
            if (killed) Motion(HiveMotion.Die);
            // The death clip carries no events, and the encounter is gone: anything still in flight is expected.
            if (events != null) events.Detach();
            if (Active == this) Active = null;
        }

        private void ClearAttackTriggers()
        {
            if (!controllerDriven || animator == null) return;
            animator.ResetTrigger(StompParam);
            animator.ResetTrigger(FireballParam);
            animator.ResetTrigger(LaserParam);
            animator.ResetTrigger(EnrageParam);
            animator.ResetTrigger(ClawParam);
            animator.ResetTrigger(BiteParam);
        }

        private void ClearThreats()
        {
            hazards.Clear();
            if (hazardRoot != null) Destroy(hazardRoot.gameObject);
        }

        private void OnDestroy()
        {
            CancelSpawnTrace("destroyed_before_ready");
            if (animationGraph.IsValid()) animationGraph.Destroy();
            ClearThreats();
            if (Active == this) Active = null;
        }

        private void CancelSpawnTrace(string reason)
        {
            if (spawnTrace == null) return;
            spawnTrace.FinishSpan(arrivalSpan);
            arrivalSpan = null;
            spawnTrace.Cancel(reason);
            spawnTrace = null;
        }
    }
}
