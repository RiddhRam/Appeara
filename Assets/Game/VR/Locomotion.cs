using System.Collections.Generic;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// A translocator pad. Stand on it and it charges, then moves the player to its linked pad. Place and move
    /// these freely in the scene; ArmoryGame links them in pairs across the arena if nothing is set.
    /// </summary>
    public sealed class TeleportPad : MonoBehaviour
    {
        public static readonly List<TeleportPad> All = new List<TeleportPad>();
        public const float ChargeSeconds = 0.7f;
        public const float StandRadius = 1.1f;

        [Tooltip("Where standing on this pad sends the player. Left empty, ArmoryGame pairs pads across the arena.")]
        public TeleportPad Linked;

        private Renderer ring;
        private Material ringMaterial;
        private LineRenderer arc;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Awake()
        {
            ring = Mats.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.02f, 0f), new Vector3(2.2f, 0.01f, 2.2f),
                Mats.Glow(new Color(0.15f, 0.6f, 0.9f), 0.7f), name: "Pad Ring").GetComponent<Renderer>();
            ringMaterial = new Material(Shader.Find("Armory/HudBar"));
            ringMaterial.SetFloat("_Segments", 24f);
            ringMaterial.SetColor("_On", new Color(0.3f, 1f, 0.6f));
            ringMaterial.SetColor("_Off", new Color(0.12f, 0.45f, 0.7f, 0.5f));
            var charge = Mats.Shape(PrimitiveType.Quad, transform, new Vector3(0f, 0.03f, 0f), new Vector3(2.2f, 2.2f, 1f), ringMaterial, name: "Charge Ring");
            charge.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            SetCharge(0f);
        }

        private void Start()
        {
            if (Linked == null) return;
            // A thin light arc between paired pads shows where each one leads.
            arc = Mats.Line(transform, new Color(0.25f, 0.8f, 1f, 0.5f), 0.03f);
            arc.positionCount = 14;
            for (int i = 0; i < 14; i++)
            {
                float t = i / 13f;
                Vector3 point = Vector3.Lerp(transform.position, Linked.transform.position, t);
                point.y += Mathf.Sin(t * Mathf.PI) * 2.2f;
                arc.SetPosition(i, point);
            }
        }

        public bool IsStandingOn(Vector3 feet)
        {
            Vector3 delta = feet - transform.position;
            delta.y = 0f;
            return delta.magnitude < StandRadius && Mathf.Abs(feet.y - transform.position.y) < 2.5f;
        }

        public void SetCharge(float fraction)
        {
            if (ringMaterial != null) ringMaterial.SetFloat("_Fill", Mathf.Clamp01(fraction));
            if (ring != null)
                ring.sharedMaterial = Mats.Glow(fraction > 0.01f ? new Color(0.3f, 1f, 0.6f) : new Color(0.15f, 0.6f, 0.9f), 0.7f);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f);
            Gizmos.DrawWireSphere(transform.position, StandRadius);
            if (Linked != null) Gizmos.DrawLine(transform.position + Vector3.up, Linked.transform.position + Vector3.up);
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

    /// <summary>
    /// Smooth locomotion: left stick walks (head-relative), left stick click engages the thruster sprint, right
    /// stick snap-turns. Standing on a translocator pad for <see cref="TeleportPad.ChargeSeconds"/> moves the
    /// player to its linked pad. A comfort vignette fades in while moving fast.
    /// </summary>
    public sealed class Locomotion : MonoBehaviour
    {
        public ArmoryRig Rig;
        public Vector3 LookTarget;
        public float WalkSpeed = 2.7f;
        public float SprintMultiplier = 2.1f;
        [Tooltip("Player stays within this distance of the arena centre.")]
        public float ArenaRadius = 34f;

        private TeleportPad charging;
        private float chargeTime;
        private float cooldownUntil;
        private bool turnLatched;
        private bool wasXR;
        private Transform vignette;
        private Material vignetteMaterial;

        public bool Sprinting { get; private set; }

        private void Start()
        {
            BuildVignette();
            if (TeleportPad.All.Count > 0) Place(TeleportPad.All[0]);
        }

        private void BuildVignette()
        {
            // A dark ring hugging the camera; its alpha rises with speed to steady the stomach.
            var go = Mats.Shape(PrimitiveType.Quad, Rig.Head.transform, new Vector3(0f, 0f, 0.12f), new Vector3(0.36f, 0.26f, 1f),
                Mats.Glow(Color.black, 0f), name: "Comfort Vignette");
            vignette = go.transform;
            vignetteMaterial = go.GetComponent<Renderer>().material;
            vignette.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Rig == null) return;
            if (Rig.IsXR && !wasXR && charging == null) Place(TeleportPad.Nearest(Rig.Head.transform.position));
            wasXR = Rig.IsXR;
            if (MissionDeck.Open) { SetVignette(0f); return; }

            Move();
            SnapTurn();
            UpdatePads();
        }

        private void Move()
        {
            Vector2 input = Rig.MoveAxis;
            Sprinting = Rig.SprintHeld && input.sqrMagnitude > 0.04f;
            if (input.sqrMagnitude < 0.02f) { SetVignette(0f); return; }

            var head = Rig.Head.transform;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward) * -1f;

            float speed = WalkSpeed * (Sprinting ? SprintMultiplier : 1f);
            Vector3 step = (forward * input.y + right * input.x) * (speed * Time.deltaTime);
            Rig.transform.position += step;
            ClampToArena();
            SetVignette(Sprinting ? 0.5f : 0.22f);
        }

        private void ClampToArena()
        {
            Vector3 feet = Rig.FeetPosition;
            Vector3 offset = feet - LookTarget;
            offset.y = 0f;
            if (offset.magnitude <= ArenaRadius) return;
            Vector3 clamped = LookTarget + offset.normalized * ArenaRadius;
            Rig.transform.position += new Vector3(clamped.x - feet.x, 0f, clamped.z - feet.z);
        }

        private void SetVignette(float target)
        {
            if (vignetteMaterial == null) return;
            var color = vignetteMaterial.GetColor("_BaseColor");
            float alpha = Mathf.MoveTowards(color.a, target, 2.5f * Time.deltaTime);
            vignette.gameObject.SetActive(alpha > 0.01f);
            vignetteMaterial.SetColor("_BaseColor", new Color(0f, 0f, 0f, alpha));
            vignetteMaterial.color = new Color(0f, 0f, 0f, alpha);
        }

        private void SnapTurn()
        {
            float turn = Rig.RightStick.x;
            if (Mathf.Abs(turn) > 0.7f && !turnLatched)
            {
                Rig.SnapTurn(Mathf.Sign(turn) * 45f);
                turnLatched = true;
            }
            else if (Mathf.Abs(turn) < 0.3f) turnLatched = false;
        }

        private void UpdatePads()
        {
            Vector3 feet = Rig.FeetPosition;
            TeleportPad standing = null;
            foreach (var pad in TeleportPad.All)
                if (pad.Linked != null && pad.IsStandingOn(feet)) { standing = pad; break; }

            if (standing == null || Time.time < cooldownUntil)
            {
                if (charging != null) { charging.SetCharge(0f); charging = null; }
                chargeTime = 0f;
                return;
            }
            if (standing != charging) { if (charging != null) charging.SetCharge(0f); charging = standing; chargeTime = 0f; }

            chargeTime += Time.deltaTime;
            charging.SetCharge(chargeTime / TeleportPad.ChargeSeconds);
            if (chargeTime < TeleportPad.ChargeSeconds) return;

            var destination = charging.Linked;
            charging.SetCharge(0f);
            charging = null;
            chargeTime = 0f;
            cooldownUntil = Time.time + 1.2f;
            Effects.Flash(feet + Vector3.up, new Color(0.3f, 1f, 0.6f), 2.5f);
            Place(destination);
            Effects.Flash(destination.transform.position + Vector3.up, new Color(0.3f, 1f, 0.6f), 2.5f);
        }

        private void Place(TeleportPad pad)
        {
            if (pad == null) return;
            Vector3 outward = pad.transform.position - LookTarget;
            Rig.TeleportTo(pad.transform.position, outward.sqrMagnitude > 0.01f ? outward : pad.transform.forward);
            ProceduralSfx.PlayAt(ProceduralSfx.Blip, pad.transform.position, 0.6f);
        }
    }
}
