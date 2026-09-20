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
        /// <summary>Squash on impact: briefly wider and shorter, the way a body absorbs a hit.</summary>
        public const float Squash = 0.18f;

        private Transform model;
        private Vector3 restPosition;
        private Vector3 restScale;
        private float bobPhase;
        private float bobHeight;
        private float bobSpeed;

        private Vector3 knock;
        private float knockAt = -99f;
        private float spawnAt;
        private bool idleMotion;

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
            idleMotion = breathes;
            // Scaled to the body: a swarmer twitching as hard as a brute looks like a glitch, not a hit.
            bobHeight = Mathf.Clamp(radius * 0.12f, 0.02f, 0.14f);
            bobSpeed = Random.Range(1.5f, 2.3f);
            bobPhase = Random.value * Mathf.PI * 2f;
            spawnAt = Time.time;
            knockAt = -99f;
            knock = Vector3.zero;
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
        }

        private void LateUpdate()
        {
            if (model == null) return;
            Vector3 position = restPosition;
            Vector3 scale = restScale;

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
