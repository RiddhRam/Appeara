using UnityEngine;

namespace Armory
{
    /// <summary>
    /// The difference between an alien and a mesh sliding towards you. None of this is authored animation - it is
    /// all driven off the model transform - because three of the five kinds have no rig to animate and the two
    /// that do only carry idle and walk.
    ///
    /// Three things, in order of how much they are felt:
    /// a hit knocks the model back and squashes it, so shooting something reads as hitting it rather than as
    /// watching a number go down; a spawn scales the model up, so a wave materialises instead of popping into
    /// existence; and everything breathes slightly, so a stunned or shielded enemy is never a statue.
    ///
    /// It is deliberately all local to the model transform and never touches the enemy root, because the root
    /// carries the collider, Radius and health-bar anchor that combat and targeting are measured from. Recoiling
    /// the hitbox would make aiming feel unreliable for the sake of a flourish.
    /// </summary>
    public sealed class EnemyPresence : MonoBehaviour
    {
        /// <summary>How far a hit pushes the model, before the weapon's own damage scales it.</summary>
        public const float KnockDistance = 0.22f;
        public const float RecoverSeconds = 0.22f;
        public const float SpawnSeconds = 0.28f;
        public const float DeathSeconds = 0.9f;
        /// <summary>Squash on impact: briefly wider and shorter, the way a body absorbs a hit.</summary>
        public const float Squash = 0.18f;

        private Transform model;
        private Vector3 restPosition;
        private Vector3 restScale;
        private Quaternion restRotation;
        private float bobPhase;
        private float bobHeight;
        private float bobSpeed;

        private Vector3 knock;
        private float knockAt = -99f;
        private float spawnAt;
        private bool idleMotion;
        private float deathAt = -99f;
        private bool dying;
        private float attackAt = -99f;
        private float attackSeconds;

        public static EnemyPresence Attach(Transform model, float radius, bool idleMotion)
        {
            if (model == null) return null;
            var presence = model.gameObject.GetComponent<EnemyPresence>();
            if (presence == null) presence = model.gameObject.AddComponent<EnemyPresence>();
            presence.Begin(model, radius, idleMotion);
            return presence;
        }

        private void Begin(Transform target, float radius, bool breathes)
        {
            model = target;
            restPosition = model.localPosition;
            restScale = model.localScale;
            restRotation = model.localRotation;
            idleMotion = breathes;
            // Scaled to the body: a swarmer twitching as hard as a brute looks like a glitch, not a hit.
            bobHeight = Mathf.Clamp(radius * 0.12f, 0.02f, 0.14f);
            bobSpeed = Random.Range(1.5f, 2.3f);
            bobPhase = Random.value * Mathf.PI * 2f;
            spawnAt = Time.time;
            knockAt = -99f;
            knock = Vector3.zero;
            deathAt = -99f;
            dying = false;
            attackAt = -99f;
        }

        /// <summary>
        /// Called when the enemy takes damage. The direction is where the shot came from, so heavy hits shove
        /// the body back along the shot and light ones barely register.
        /// </summary>
        public void Hit(Vector3 fromDirection, float severity)
        {
            if (model == null) return;
            Vector3 push = fromDirection;
            push.y = 0f;
            if (push.sqrMagnitude < 0.0001f) push = -model.forward;
            knock = push.normalized * (KnockDistance * Mathf.Clamp(severity, 0.25f, 1.6f));
            knockAt = Time.time;
        }

        /// <summary>Wakes the pop-in again, for a body coming back out of the enemy pool.</summary>
        public void Respawned()
        {
            spawnAt = Time.time;
            knockAt = -99f;
            knock = Vector3.zero;
            deathAt = -99f;
            dying = false;
            attackAt = -99f;
            if (model != null)
            {
                model.localPosition = restPosition;
                model.localRotation = restRotation;
                model.localScale = restScale;
            }
        }

        /// <summary>Starts the pooled heavy-body tip and sink without moving its gameplay root.</summary>
        public void Die()
        {
            if (model == null) return;
            dying = true;
            deathAt = Time.time;
            knockAt = -99f;
        }

        /// <summary>A short forward lean that telegraphs attacks on models with no authored attack state.</summary>
        public void Attack(float windupSeconds)
        {
            if (model == null || dying) return;
            attackAt = Time.time;
            attackSeconds = Mathf.Max(0.01f, windupSeconds);
        }

        private void LateUpdate()
        {
            if (model == null) return;
            Vector3 position = restPosition;
            Vector3 scale = restScale;

            if (dying)
            {
                float death = Mathf.Clamp01((Time.time - deathAt) / DeathSeconds);
                float eased = death * death * (3f - 2f * death);
                model.localPosition = restPosition + Vector3.down * (restScale.y * 0.45f * eased);
                model.localRotation = restRotation * Quaternion.Euler(78f * eased, 0f, 12f * eased);
                model.localScale = restScale;
                return;
            }

            // Materialising, so a wave arrives rather than appearing.
            float spawn = (Time.time - spawnAt) / SpawnSeconds;
            if (spawn < 1f)
            {
                float eased = 1f - (1f - spawn) * (1f - spawn);
                scale *= Mathf.Lerp(0.35f, 1f, eased);
            }

            if (idleMotion)
            {
                bobPhase += Time.deltaTime * bobSpeed;
                position += Vector3.up * (Mathf.Sin(bobPhase) * bobHeight);
            }


            float attack = (Time.time - attackAt) / attackSeconds;
            if (attack >= 0f && attack < 1f)
            {
                float lean = Mathf.Sin(attack * Mathf.PI * 0.5f);
                position += Vector3.forward * (0.16f * lean);
                model.localRotation = restRotation * Quaternion.Euler(12f * lean, 0f, 0f);
            }
            else model.localRotation = restRotation;

            float since = Time.time - knockAt;
            if (since < RecoverSeconds)
            {
                // Snap back on the hit, then ease home: the recovery is what sells the weight.
                float t = since / RecoverSeconds;
                float amount = (1f - t) * (1f - t);
                position += model.parent != null ? model.parent.InverseTransformVector(knock) * amount : knock * amount;
                scale = new Vector3(scale.x * (1f + Squash * amount), scale.y * (1f - Squash * amount), scale.z * (1f + Squash * amount));
            }

            model.localPosition = position;
            model.localScale = scale;
        }
    }
}
