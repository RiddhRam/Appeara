using UnityEngine;

namespace Armory
{
    /// <summary>The encounter side of the authored animation events, so the receiver can be tested without a boss.</summary>
    public interface IHiveAttackEvents
    {
        void StompImpact();
        void FireballRelease();
        void LaserStart();
        void LaserEnd();
        void AttackRecovered();
    }

    /// <summary>
    /// Receives the AnimationEvents baked into the boss attack clips and forwards them to the encounter.
    /// <para>
    /// Unity delivers an AnimationEvent only to components sitting on the GameObject that carries the Animator.
    /// It does not walk up to parents or down into children, so this has to be added to the animator object
    /// inside the art prefab rather than to the HiveAvatar root, which is two levels above it.
    /// </para><para>
    /// The five methods must stay public, return void and take no argument. The authored events carry no
    /// parameter, and Unity matches purely on name and signature: a rename, a private modifier or a stray
    /// argument all resolve to "no such method". The clips also ship with DontRequireReceiver, so Unity stays
    /// silent when nothing answers - the counters and warnings here are the only evidence a hook has broken.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HiveAvatarEvents : MonoBehaviour
    {
        /// <summary>The exact method names the authored clips call, so a test can hold art and code together.</summary>
        public static readonly string[] Callbacks =
        {
            "OnStompImpact", "OnFireballRelease", "OnLaserStart", "OnLaserEnd", "OnAttackRecovered",
        };

        private IHiveAttackEvents owner;
        private bool detached;

        /// <summary>Events that arrived with nothing listening. Anything but zero means the wiring broke.</summary>
        public int Orphaned { get; private set; }
        /// <summary>Events delivered to the encounter; a boss that attacks and reports zero never fired a clip.</summary>
        public int Delivered { get; private set; }

        /// <summary>
        /// Attaches the receiver to the animator's own GameObject, which is the only object Unity will deliver
        /// this clip's events to.
        /// </summary>
        public static HiveAvatarEvents Bind(Animator animator, IHiveAttackEvents owner)
        {
            if (animator == null || owner == null) return null;
            // A culled animator evaluates nothing and fires nothing, and the boss spends much of the fight
            // outside a VR player's narrow field of view.
            animator.fireEvents = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var receiver = animator.gameObject.GetComponent<HiveAvatarEvents>();
            if (receiver == null) receiver = animator.gameObject.AddComponent<HiveAvatarEvents>();
            receiver.owner = owner;
            receiver.detached = false;
            return receiver;
        }

        /// <summary>
        /// Stops forwarding once the encounter is over. Without this the death clip's blend would log a missing
        /// owner for every late event, which is noise sitting exactly where a real fault would appear.
        /// </summary>
        public void Detach()
        {
            owner = null;
            detached = true;
        }

        public void OnStompImpact() { if (Claim("OnStompImpact")) owner.StompImpact(); }
        public void OnFireballRelease() { if (Claim("OnFireballRelease")) owner.FireballRelease(); }
        public void OnLaserStart() { if (Claim("OnLaserStart")) owner.LaserStart(); }
        public void OnLaserEnd() { if (Claim("OnLaserEnd")) owner.LaserEnd(); }
        public void OnAttackRecovered() { if (Claim("OnAttackRecovered")) owner.AttackRecovered(); }

        private bool Claim(string callback)
        {
            // The owner is held as an interface, so C# equality applies and a destroyed encounter would read as
            // a live reference. Ask the Object side of it whether it is still there.
            bool alive = owner != null && (!(owner is Object unityOwner) || unityOwner != null);
            if (alive)
            {
                Delivered++;
                return true;
            }
            if (detached) return false;
            Orphaned++;
            Debug.LogWarning($"HiveAvatarEvents: '{callback}' fired on '{name}' with no encounter bound. " +
                "The attack will fall back to its authored timing; check HiveAvatarEvents.Bind ran on the Animator GameObject.");
            return false;
        }
    }
}
