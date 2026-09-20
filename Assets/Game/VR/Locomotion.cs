using System.Collections.Generic;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// A speed pad. Stand on it to boost movement; pads can be placed freely around the arena.
    /// </summary>
    public sealed class TeleportPad : MonoBehaviour
    {
        public static readonly List<TeleportPad> All = new List<TeleportPad>();
        public const float StandRadius = 1.1f;
        [Tooltip("Movement multiplier while the player is standing on this pad.")]
        public float SpeedMultiplier = 3f;

        private Renderer ring;
        private Material ringMaterial;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Awake()
        {
            ring = Mats.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.02f, 0f), new Vector3(2.2f, 0.01f, 2.2f),
                Mats.Glow(new Color(0.15f, 0.6f, 0.9f), 0.7f), name: "Pad Ring").GetComponent<Renderer>();
            ringMaterial = new Material(Shader.Find("Armory/HudBar"));
            ringMaterial.SetFloat("_Segments", 24f);
            ringMaterial.SetColor("_On", new Color(1f, 0.75f, 0.15f));
            ringMaterial.SetColor("_Off", new Color(0.12f, 0.45f, 0.7f, 0.5f));
            var charge = Mats.Shape(PrimitiveType.Quad, transform, new Vector3(0f, 0.03f, 0f), new Vector3(2.2f, 2.2f, 1f), ringMaterial, name: "Charge Ring");
            charge.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            SetBoosted(false);
        }

        public bool IsStandingOn(Vector3 feet)
        {
            Vector3 delta = feet - transform.position;
            delta.y = 0f;
            return delta.magnitude < StandRadius && Mathf.Abs(feet.y - transform.position.y) < 2.5f;
        }

        public void SetBoosted(bool boosted)
        {
            if (ringMaterial != null) ringMaterial.SetFloat("_Fill", boosted ? 1f : 0f);
            if (ring != null)
                ring.sharedMaterial = Mats.Glow(boosted ? new Color(1f, 0.75f, 0.15f) : new Color(0.15f, 0.6f, 0.9f), 0.7f);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f);
            Gizmos.DrawWireSphere(transform.position, StandRadius);
        }
    }

    /// <summary>
    /// Smooth locomotion: left stick walks (head-relative), left stick click engages the thruster sprint, right
    /// stick snap-turns. Standing on a speed pad grants a movement boost. A comfort vignette fades in while moving fast.
    /// </summary>
    public sealed class Locomotion : MonoBehaviour
    {
        public ArmoryRig Rig;
        public Vector3 LookTarget;
        public float WalkSpeed = 2.7f;
        public float SprintMultiplier = 2.1f;
        [Tooltip("Player stays within this distance of the arena centre.")]
        public float ArenaRadius = 34f;

        private TeleportPad activeSpeedPad;
        private float padSpeedMultiplier = 1f;
        private bool turnLatched;
        private Transform vignette;
        private Material vignetteMaterial;

        public bool Sprinting { get; private set; }

        private void Start()
        {
            BuildVignette();
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
            if (MissionDeck.Open) { SetSpeedPad(null); SetVignette(0f); return; }

            UpdatePads();
            Move();
            SnapTurn();
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
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            float speed = WalkSpeed * (Sprinting ? SprintMultiplier : 1f) * padSpeedMultiplier;
            Vector3 step = (forward * input.y + right * input.x) * (speed * Time.deltaTime);
            Rig.transform.position += step;
            ClampToArena();
            SetVignette(Sprinting ? 0.5f : padSpeedMultiplier > 1f ? 0.4f : 0.22f);
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
                if (pad.IsStandingOn(feet)) { standing = pad; break; }
            SetSpeedPad(standing);
        }

        private void SetSpeedPad(TeleportPad pad)
        {
            if (activeSpeedPad == pad) return;
            if (activeSpeedPad != null) activeSpeedPad.SetBoosted(false);
            activeSpeedPad = pad;
            padSpeedMultiplier = activeSpeedPad != null ? activeSpeedPad.SpeedMultiplier : 1f;
            if (activeSpeedPad != null) activeSpeedPad.SetBoosted(true);
        }
    }
}
