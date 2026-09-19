using System.Collections;
using System.Collections.Generic;
using Armory.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Armory
{
    /// <summary>Animated final encounter. Owns its attack clock, organs, plating and all spawned threats.</summary>
    public sealed class HiveAvatar : MonoBehaviour
    {
        public static HiveAvatar Active { get; private set; }
        public readonly HiveAvatarState Rules = new HiveAvatarState();
        public Enemy Body { get; private set; }
        public string Phase { get; private set; } = "ARRIVING";
        public string CurrentAttack { get; private set; } = "";
        public Vector3 AimPoint => transform.TransformPoint(new Vector3(0f, 6f, 4f));
        public bool Enraged => Body != null && Body.Health <= Body.MaxHealth * 0.35f;
        public bool Ready { get; private set; }

        private static readonly Color HiveColor = new Color(1f, 0.25f, 0.48f);
        private static readonly string[] OrganNames = { "LEFT CLAW", "RIGHT CLAW", "SPORE SAC", "CREST" };
        private readonly Transform[] organs = new Transform[4];
        private readonly Transform[] anchors = new Transform[4];
        private readonly List<Renderer> plates = new List<Renderer>();
        private readonly List<HiveThreat> threats = new List<HiveThreat>();
        private HiveAvatarAssets assets;
        private Transform model;
        private Transform hazards;
        private TextMeshPro title, status;
        private Material healthBar;
        private PlayableGraph animationGraph;
        private AnimationClipPlayable animation;
        private AnimationClip currentClip;
        private bool loopClip;
        private float clipTime;
        private bool dying;

        public void Initialize(Enemy body, HiveAvatarAssets presentation)
        {
            Active = this;
            Body = body;
            Body.Avatar = this;
            Body.ExternallyDriven = true;
            assets = presentation;
            hazards = new GameObject("Hive Hazards").transform;
            hazards.SetParent(transform.parent, false);
            if (assets != null && assets.Model != null) BuildModel();
            else Mats.Shape(PrimitiveType.Capsule, transform, Vector3.up * 6f, new Vector3(8f, 6f, 8f), Mats.Lit(HiveColor), name: "Avatar Fallback");
            BuildTargets();
            BuildHealthDisplay();
            StartCoroutine(Encounter());
        }

        private void BuildModel()
        {
            model = Instantiate(assets.Model, transform).transform;
            model.name = "Hive Avatar Body";
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;
            foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
            Play(assets.Idle, true);
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
            var bones = model.GetComponentsInChildren<Transform>();
            string[] names = { "Bone.005_L.004", "Bone.005_R.004", "Bone.005", "Bone.028" };
            for (int i = 0; i < names.Length; i++)
                foreach (var bone in bones) if (bone.name == names[i]) { anchors[i] = bone; break; }
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
            Vector3 start = transform.position;
            Vector3 outward = (start - Body.Target).normalized;
            float coreReach = StationCore.Instance != null ? StationCore.Instance.ReachRadius : 5f;
            Vector3 destination = Body.Target + outward * Mathf.Max(27f, coreReach + 20f);
            Play(assets != null ? assets.Walk : null, true);
            for (float t = 0f; t < 4f; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, destination, Mathf.SmoothStep(0f, 1f, t / 4f));
                yield return null;
            }
            transform.position = destination;
            Ready = true;
            Play(assets != null ? assets.Idle : null, true);
            ArmoryGame.Instance?.ShowBanner("HIVE AVATAR / BREAK THE GLOWING ORGANS", HiveColor);
            int attackIndex = 0;
            while (Body.Alive)
            {
                Phase = Enraged ? "ENRAGED" : "HUNTING";
                yield return new WaitForSeconds(Enraged ? 2.8f : 4.5f);
                var attack = (HiveAttack)(attackIndex++ % 4);
                if (!Rules.CanAttack(attack)) continue;
                CurrentAttack = attack.ToString();
                if (attack == HiveAttack.SweepLeft || attack == HiveAttack.SweepRight) yield return Sweep(attack);
                else yield return LaunchThreats(attack);
                CurrentAttack = "";
                Play(assets != null ? assets.Idle : null, true);
            }
        }

        private IEnumerator Sweep(HiveAttack attack)
        {
            Vector3 point = PlayerGround();
            const float radius = 3.2f, warning = 3f;
            Phase = "SWEEP / TELEPORT OUT / CORE AT RISK";
            ArmoryGame.Instance?.ShowBanner("CLAW SWEEP / TELEPORT OUT OF THE RING", HiveColor);
            var marker = HiveTelegraph.Create(hazards, point, radius, HiveColor);
            for (float t = 0; t < warning; t += Time.deltaTime)
            {
                marker.SetProgress(t / warning);
                if (!Rules.CanAttack(attack)) { Destroy(marker.gameObject); yield break; }
                yield return null;
            }
            Play(assets != null ? assets.Attack : null, false);
            Effects.Lightning(AimPoint, point + Vector3.up, HiveColor);
            Effects.Burst(point + Vector3.up * 0.5f, HiveColor, radius * 2f);
            Destroy(marker.gameObject);
            // The marine has no HP: failing the dodge transfers the impact to station integrity.
            if (Vector3.Distance(PlayerGround(), point) < radius) StationCore.Instance?.TakeDamage(12f);
            yield return new WaitForSeconds(1f);
        }

        private IEnumerator LaunchThreats(HiveAttack attack)
        {
            bool spores = attack == HiveAttack.Spores;
            Phase = spores ? "SPORES / SHOOT BEFORE THEY HATCH" : "WRECK / SHOOT TO PROTECT CORE";
            ArmoryGame.Instance?.ShowBanner(spores ? "SPORE PODS / SHOOT THEM DOWN" : "INCOMING WRECK / SHOOT IT DOWN", HiveColor);
            Play(assets != null ? assets.Spit : null, false);
            yield return new WaitForSeconds(0.8f);
            if (!Rules.CanAttack(attack)) yield break;
            int count = spores ? 3 : 1;
            for (int i = 0; i < count; i++)
            {
                Vector3 destination = spores ? Body.Target + Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 14f : Body.Target + Vector3.up * 2f;
                threats.Add(HiveThreat.Launch(hazards, AimPoint, destination, spores));
            }
        }

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
            foreach (var plate in plates)
            {
                Effects.Flash(plate.transform.position, color, 1.8f);
                plate.sharedMaterial = Mats.Lit(color, 0.8f);
            }
            ArmoryGame.Instance?.ShowBanner("PLATING: " + primitive.ToUpperInvariant() + " / SWITCH WEAPON", color);
        }

        public void Defeated(bool killed)
        {
            dying = true;
            Ready = false;
            Phase = "DEFEATED";
            StopAllCoroutines();
            ClearThreats();
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var organ in organs) if (organ != null) organ.gameObject.SetActive(false);
            if (killed) Play(assets != null ? assets.Death : null, false);
            if (Active == this) Active = null;
        }

        private void ClearThreats()
        {
            foreach (var threat in threats) if (threat != null) threat.Cancel();
            threats.Clear();
            if (hazards != null) Destroy(hazards.gameObject);
        }

        private void OnDestroy()
        {
            if (animationGraph.IsValid()) animationGraph.Destroy();
            ClearThreats();
            if (Active == this) Active = null;
        }
    }
}
