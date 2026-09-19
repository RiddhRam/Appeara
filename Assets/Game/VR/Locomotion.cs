using System.Collections.Generic;
using UnityEngine;

namespace Armory
{
    /// <summary>A fixed spot the player can teleport to. Place/move these in the scene freely.</summary>
    public sealed class TeleportPad : MonoBehaviour
    {
        public static readonly List<TeleportPad> All = new List<TeleportPad>();
        private Renderer ring;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Awake()
        {
            ring = Mats.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.02f, 0f), new Vector3(1.2f, 0.01f, 1.2f), Mats.Glow(new Color(0.15f, 0.6f, 0.9f), 0.7f), name: "Pad Ring").GetComponent<Renderer>();
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.9f, new Vector3(1.2f, 1.8f, 1.2f));
        }

        public void SetHighlighted(bool on, bool occupied)
        {
            if (ring != null)
                ring.sharedMaterial = Mats.Glow(on ? new Color(0.3f, 1f, 0.6f) : occupied ? new Color(0.1f, 0.35f, 0.5f) : new Color(0.15f, 0.6f, 0.9f), 0.7f);
        }

        public static TeleportPad Nearest(Vector3 point)
        {
            TeleportPad best = null;
            float bestDistance = float.MaxValue;
            foreach (var pad in All)
            {
                float d = (pad.transform.position - point).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = pad; }
            }
            return best;
        }
    }

    /// <summary>Left stick forward: aim a ray at a pad; release to teleport. Right stick: 45° snap turn.</summary>
    public sealed class Locomotion : MonoBehaviour
    {
        public ArmoryRig Rig;
        public Vector3 LookTarget;

        private LineRenderer ray;
        private TeleportPad aimed;
        private TeleportPad current;
        private bool aiming;
        private bool turnLatched;
        private bool wasXR;

        private void Start()
        {
            ray = Mats.Line(transform, new Color(0.3f, 1f, 0.6f), 0.01f);
            ray.enabled = false;
            if (TeleportPad.All.Count > 0) GoTo(TeleportPad.All[0]);
        }

        private void Update()
        {
            if (Rig == null) return;

            // Re-seat once when headset tracking kicks in, so the real head position lands on the pad.
            if (Rig.IsXR && !wasXR && current != null) GoTo(current);
            wasXR = Rig.IsXR;

            if (Rig.DesktopTeleportStep != 0 && TeleportPad.All.Count > 0)
            {
                int index = Mathf.Max(0, TeleportPad.All.IndexOf(current));
                int count = TeleportPad.All.Count;
                GoTo(TeleportPad.All[((index + Rig.DesktopTeleportStep) % count + count) % count]);
            }

            Vector2 stick = Rig.LeftStick;
            if (stick.y > 0.6f)
            {
                aiming = true;
                var from = Rig.LeftHand.position;
                var direction = Rig.LeftHand.forward;
                // Aim where the ray meets the floor plane, falling back to a point 15 m out.
                Vector3 point = direction.y < -0.05f ? from + direction * (-from.y / direction.y) : from + direction * 15f;
                aimed = TeleportPad.Nearest(point);
                ray.enabled = true;
                ray.SetPosition(0, from);
                ray.SetPosition(1, aimed != null ? aimed.transform.position : point);
            }
            else if (aiming && stick.magnitude < 0.3f)
            {
                aiming = false;
                ray.enabled = false;
                if (aimed != null) GoTo(aimed);
                aimed = null;
            }

            foreach (var pad in TeleportPad.All) pad.SetHighlighted(pad == aimed, pad == current);

            float turn = Rig.RightStick.x;
            if (Mathf.Abs(turn) > 0.7f && !turnLatched)
            {
                Rig.SnapTurn(Mathf.Sign(turn) * 45f);
                turnLatched = true;
            }
            else if (Mathf.Abs(turn) < 0.3f) turnLatched = false;
        }

        private void GoTo(TeleportPad pad)
        {
            current = pad;
            // Face outward from the station so the player looks at the incoming aliens.
            Vector3 outward = pad.transform.position - LookTarget;
            Rig.TeleportTo(pad.transform.position, outward.sqrMagnitude > 0.01f ? outward : pad.transform.forward);
            ProceduralSfx.PlayAt(ProceduralSfx.Blip, pad.transform.position, 0.6f);
        }
    }
}
