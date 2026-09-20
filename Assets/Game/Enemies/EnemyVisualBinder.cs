using System.Collections.Generic;
using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Hangs the authored monster model off an enemy root and keeps it strictly decorative: the factory's
    /// primitive still owns the collider, and Radius, Center and the health bar are still measured from it, so
    /// new art can never move a hitbox. Every step fails soft - no table, no row, no prefab, or a prefab with
    /// nothing to draw - and leaves the primitive placeholder visible, which is what lets the table be filled
    /// one monster at a time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyVisualBinder : MonoBehaviour
    {
        public const string ResourcePath = "EnemyVisuals";

        private static EnemyVisuals table;
        private static bool tableLoaded;

        private Animator animator;
        private int movingParameter;
        private int attackParameter;
        private int hitParameter;
        private int dieParameter;
        private float deathAnimationSeconds;
        private bool moving;
        private bool movingKnown;
        // The staged AlienMonster "moving" take is identical to its idle take. This small root-only gait
        // makes locomotion read in-game until authored walking footage replaces that source clip.
        private Vector3 modelRestPosition;
        private Quaternion modelRestRotation;
        private float gaitPhase;
        private bool modelPoseKnown;
        private EnemyKind boundKind;
        private bool dying;
        private float proceduralAttackUntil;

        /// <summary>Root of the instantiated model; null once the enemy has died or when no art was bound.</summary>
        public Transform Model { get; private set; }

        /// <summary>The model's drawable renderers, in the order they were measured.</summary>
        public Renderer[] Renderers { get; private set; }

        /// <summary>True when a walk animation is wired up and <see cref="SetMoving"/> will reach a controller.</summary>
        public bool Animated => animator != null && movingParameter != 0;

        /// <summary>Hit reactions, spawn-in and idle motion; null when this kind has no model.</summary>
        public EnemyPresence Presence { get; private set; }

        /// <summary>A body out of the pool keeps its model, so the pop-in has to be restarted by hand.</summary>
        public void Respawned() { if (Presence != null) Presence.Respawned(); }

        /// <summary>Plays an authored attack when the controller exposes an Attack trigger.</summary>
        public void Attack()
        {
            SetTrigger(attackParameter);
            proceduralAttackUntil = Time.time + Enemy.AttackWindupSeconds;
            if (Presence != null) Presence.Attack(Enemy.AttackWindupSeconds);
        }

        /// <summary>Knocks the model back along the shot. Silent when this kind is a bare placeholder.</summary>
        public void Hit(Vector3 fromDirection, float severity)
        {
            // Do not replace an attack tell with a reaction pose while its already-scheduled core hit remains.
            // Procedural recoil still gives immediate hit feedback without obscuring the windup.
            if (Time.time >= proceduralAttackUntil) SetTrigger(hitParameter);
            if (Presence != null) Presence.Hit(fromDirection, severity);
        }

        /// <summary>Plays an authored death trigger and optionally adds the pooled heavy-body collapse.</summary>
        public float Die(bool collapse)
        {
            dying = true;
            SetTrigger(dieParameter);
            if (animator != null && dieParameter != 0 && deathAnimationSeconds <= 0f)
            {
                // Imported Mixamo takes sometimes retain the generic name "mixamo.com". Let the transition
                // resolve once and ask the active Die state for its real duration instead of trusting the clip name.
                animator.Update(0f);
                var state = animator.IsInTransition(0)
                    ? animator.GetNextAnimatorStateInfo(0)
                    : animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Die")) deathAnimationSeconds = state.length;
            }
            if (collapse && Presence != null) Presence.Die();
            return Mathf.Max(collapse ? EnemyPresence.DeathSeconds : 0f, deathAnimationSeconds);
        }

        /// <summary>
        /// Loaded once and kept: wave two spawns 45 aliens 0.18 seconds apart, and a Resources.Load per spawn
        /// would put a synchronous asset lookup inside the spawn loop.
        /// </summary>
        public static EnemyVisuals Table
        {
            get
            {
                if (tableLoaded) return table;
                tableLoaded = true;
                table = Resources.Load<EnemyVisuals>(ResourcePath);
                return table;
            }
        }

        /// <summary>The row authored for a kind, or null when the placeholder should stand in.</summary>
        public static EnemyVisuals.Entry EntryFor(EnemyKind kind) => Table != null ? Table.For(kind) : null;

        /// <summary>Drops the cached table so a test or a scene reload picks the asset up again.</summary>
        public static void ForgetTable()
        {
            table = null;
            tableLoaded = false;
        }

        /// <summary>
        /// Kinds that arrive in bulk draw no shadows. A swarm wave is 45 skinned meshes and a blitz is fourteen
        /// fast ones; at that count each shadow caster is a second skinned draw for a blob nobody can pick out
        /// under a dozen others. The big, slow, individually readable aliens keep theirs, because a brute's
        /// shadow is how you judge where it is about to be.
        /// </summary>
        private static bool CastsShadows(EnemyKind kind) => kind != EnemyKind.Swarm && kind != EnemyKind.Fast;

        public static EnemyVisualBinder Attach(GameObject root, EnemyKind kind)
        {
            var binder = Bind(root, EntryFor(kind));
            if (binder != null)
            {
                binder.boundKind = kind;
                binder.SetShadows(CastsShadows(kind));
            }
            return binder;
        }

        /// <summary>Shadow casting on the authored model; the placeholders it replaced no longer draw at all.</summary>
        public void SetShadows(bool on)
        {
            if (Renderers == null) return;
            var mode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var renderer in Renderers)
                if (renderer != null) renderer.shadowCastingMode = mode;
        }

        /// <summary>Binds one authored row. Returns null - meaning "keep the placeholder" - for anything unusable.</summary>
        public static EnemyVisualBinder Bind(GameObject root, EnemyVisuals.Entry entry)
        {
            if (root == null || entry == null || entry.Prefab == null) return null;
            var binder = root.GetComponent<EnemyVisualBinder>();
            if (binder == null) binder = root.AddComponent<EnemyVisualBinder>();
            // A body out of the enemy pool still carries the model it was fitted with last time. Building a
            // second one would stack two models in the same place and hide the first behind the placeholder
            // sweep below, so the existing one is handed straight back.
            else if (binder.Model != null)
            {
                // Disabling a pooled root resets its Animator to the controller's default parameters, but this
                // binder used to remember that the previous occupant was already moving. Make the next enemy
                // write Moving again instead of leaving its freshly re-enabled model in the idle pose.
                binder.ForgetMotionState();
                return binder;
            }
            // Grabbed before the model arrives so the model's own renderers are never in the hide list.
            var placeholders = root.GetComponentsInChildren<Renderer>(true);
            if (!binder.Build(entry))
            {
                Kill(binder);
                return null;
            }
            // The primitives stay in the hierarchy - one of them carries the gameplay collider - but they stop
            // drawing, so the model is the only silhouette the player sees and shots still land where they did.
            foreach (var renderer in placeholders)
                if (renderer != null) renderer.enabled = false;
            return binder;
        }

        private bool Build(EnemyVisuals.Entry entry)
        {
            var instance = Instantiate(entry.Prefab, transform);
            instance.name = "Model";
            var model = instance.transform;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.Euler(entry.EulerOffset);

            // Source FBXs often arrive with their own colliders. A second collider on the enemy would swallow
            // shots the aim never aimed at and change weapon accuracy, so only the factory body can be hit.
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

            var renderers = Collect(instance);
            if (renderers.Count == 0)
            {
                Kill(instance);
                return false;
            }
            Renderers = renderers.ToArray();
            Model = model;
            BindAnimator(instance, entry);
            Fit(model, transform, entry);
            // After Fit, so the pose it treats as home is the fitted one. Kinds with no walk cycle get the idle
            // bob, because a hovering drone that never moves a pixel reads as a prop rather than a threat.
            Presence = EnemyPresence.Attach(model, entry.Rescales ? entry.Height * 0.5f : 1f, !Animated);
            return true;
        }

        private void BindAnimator(GameObject instance, EnemyVisuals.Entry entry)
        {
            animator = instance.GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.applyRootMotion = false;
            // Keep evaluating the rig even when Unity has temporarily culled its renderers. CullCompletely can
            // leave a visible enemy frozen in the pose it had while offscreen (especially with stereo cameras);
            // CullUpdateTransforms still lets normal render culling save the draw cost while preserving motion.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (animator.runtimeAnimatorController == null)
            {
                animator = null;
                return;
            }
            // Settle into the controller's default state now: the bake in Fit measures whatever pose is applied.
            animator.Rebind();
            animator.Update(0f);
            attackParameter = Trigger(animator, "Attack");
            hitParameter = Trigger(animator, "Hit");
            dieParameter = Trigger(animator, "Die");
            deathAnimationSeconds = dieParameter != 0 ? DeathClipSeconds(animator) : 0f;
            if (string.IsNullOrEmpty(entry.MovingParameter) || !HasBool(animator, entry.MovingParameter)) return;
            movingParameter = Animator.StringToHash(entry.MovingParameter);
        }

        /// <summary>Checked once at spawn: SetBool on a controller without that parameter warns on every call.</summary>
        private static bool HasBool(Animator animator, string name)
        {
            var parameters = animator.parameters;
            foreach (var parameter in parameters)
                if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == name) return true;
            return false;
        }

        private static int Trigger(Animator target, string name)
        {
            foreach (var parameter in target.parameters)
                if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == name)
                    return Animator.StringToHash(name);
            return 0;
        }

        private static float DeathClipSeconds(Animator target)
        {
            float seconds = 0f;
            foreach (var clip in target.runtimeAnimatorController.animationClips)
            {
                if (clip == null) continue;
                string clipName = clip.name.ToLowerInvariant();
                if (clipName.Contains("death") || clipName.Contains("dying"))
                    seconds = Mathf.Max(seconds, clip.length);
            }
            return seconds;
        }

        private void SetTrigger(int parameter)
        {
            if (animator != null && parameter != 0) animator.SetTrigger(parameter);
        }

        /// <summary>Walk cycle on while the alien is actually moving; a stunned alien stands still.</summary>
        public void SetMoving(bool value)
        {
            if (movingKnown && moving == value) return;
            movingKnown = true;
            moving = value;
            if (animator != null && movingParameter != 0) animator.SetBool(movingParameter, value);
        }

        private void LateUpdate()
        {
            if (dying || Time.time < proceduralAttackUntil || !modelPoseKnown || Model == null) return;
            if (!moving)
            {
                Model.localPosition = modelRestPosition;
                Model.localRotation = modelRestRotation;
                return;
            }

            // The model root is decorative, so this never moves its gameplay collider or aim point. Swarmers
            // bob faster and lower; walkers get a readable stride lift and forward lean.
            float pace = boundKind == EnemyKind.Swarm ? 11f : 7f;
            float lift = boundKind == EnemyKind.Swarm ? 0.045f : 0.085f;
            float step = Mathf.Sin(Time.time * pace + gaitPhase);
            Model.localPosition = modelRestPosition + Vector3.up * (Mathf.Abs(step) * lift);
            Model.localRotation = modelRestRotation * Quaternion.Euler(step * (boundKind == EnemyKind.Swarm ? 4f : 7f), 0f, 0f);
        }

        private void ForgetMotionState()
        {
            movingKnown = false;
            moving = false;
            dying = false;
            proceduralAttackUntil = 0f;
            if (modelPoseKnown && Model != null)
            {
                Model.localPosition = modelRestPosition;
                Model.localRotation = modelRestRotation;
            }
        }

        /// <summary>
        /// Takes the model away the moment the enemy dies rather than waiting on the root. Safe to call twice:
        /// a second call must not reach through a stale reference and destroy an unrelated object.
        /// </summary>
        public void Release()
        {
            animator = null;
            movingParameter = 0;
            attackParameter = hitParameter = dieParameter = 0;
            deathAnimationSeconds = 0f;
            dying = false;
            proceduralAttackUntil = 0f;
            Renderers = null;
            Presence = null;
            if (Model == null) return;
            var model = Model.gameObject;
            Model = null;
            Kill(model);
        }

        private static void Fit(Transform model, Transform space, EnemyVisuals.Entry entry)
        {
            if (!TryMeasure(model, space, out var bounds)) return;
            Vector3 authored = model.localScale;
            // A row with no height still has to stand on the deck, so ask for the height it already has: that
            // gives scale 1 and the grounding offset out of the same maths the fitted models go through.
            var fit = ModelFit.Solve(bounds, entry.Rescales ? entry.Height : bounds.size.y);
            model.localScale = authored * fit.Scale;
            model.localPosition = fit.Offset;
        }

        /// <summary>
        /// Combined bounds of the model expressed in <paramref name="space"/>, with the model sitting at the
        /// origin of that space. Skinned models are baked first: their imported bounds are padded to cover every
        /// frame of every clip, so measuring those would size the alien to its widest animation, not its body.
        /// </summary>
        public static bool TryMeasure(Transform model, Transform space, out Bounds bounds)
        {
            bounds = default;
            if (model == null || space == null) return false;
            bool any = false;
            Mesh baked = null;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!Measurable(renderer)) continue;
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                {
                    if (baked == null) baked = new Mesh();
                    // Bake with the renderer scale so the vertices stay in its local space; these FBXs carry a
                    // 100x renderer transform and the TransformPoint below has to apply that exactly once.
                    skin.BakeMesh(baked, true);
                    Encapsulate(ref bounds, ref any, baked.bounds, skin.transform, space);
                }
                else Encapsulate(ref bounds, ref any, renderer.bounds, null, space);
            }
            if (baked != null) Kill(baked);
            return any;
        }

        private static List<Renderer> Collect(GameObject instance)
        {
            var found = new List<Renderer>();
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                if (Measurable(renderer)) found.Add(renderer);
            return found;
        }

        /// <summary>Particles, trails and lines draw nothing stable to measure and must not size the alien.</summary>
        private static bool Measurable(Renderer renderer) =>
            renderer != null && renderer.enabled && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer);

        private static void Encapsulate(ref Bounds bounds, ref bool any, Bounds box, Transform from, Transform space)
        {
            Vector3 min = box.min, max = box.max;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                Vector3 point = space.InverseTransformPoint(from != null ? from.TransformPoint(corner) : corner);
                if (!any)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    any = true;
                }
                else bounds.Encapsulate(point);
            }
        }

        /// <summary>Edit-mode tests build and tear these down, where a deferred Destroy never runs.</summary>
        private static void Kill(Object victim)
        {
            if (victim == null) return;
            if (Application.isPlaying) Destroy(victim);
            else DestroyImmediate(victim);
        }
    }
}
