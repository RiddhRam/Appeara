using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Laser pointer for world-space buttons. Any <see cref="HudButton"/> in the scene can be pressed by aiming the
    /// right controller at it and pulling the trigger; while the ray is on a button the shot is swallowed so the
    /// player does not fire their weapon at the UI.
    /// </summary>
    public sealed class UiPointer : MonoBehaviour
    {
        public static UiPointer Instance { get; private set; }
        /// <summary>True when the crosshair is on a button or placing a panel, so weapons hold fire.</summary>
        public static bool OverButton => PanelGrab.Dragging || (Instance != null && Instance.hovered != null);

        public ArmoryRig Rig;

        private LineRenderer ray;
        private HudButton hovered;
        private bool triggerHeld;

        private void Awake() => Instance = this;

        private void Start()
        {
            ray = Mats.Line(transform, UiKit.Cyan, 0.006f);
            ray.enabled = false;
        }

        private void Update()
        {
            if (Rig == null) return;
            Vector3 origin = Rig.Aim.position;
            Vector3 direction = Rig.Aim.forward;

            HudButton target = null;
            float distance = 4f;
            if (Physics.Raycast(origin, direction, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                target = hit.collider.GetComponent<HudButton>();
                if (target != null) distance = hit.distance;
            }

            if (target != hovered)
            {
                if (hovered != null) hovered.SetHovered(false);
                hovered = target;
                if (hovered != null) hovered.SetHovered(true);
            }

            // The ray only shows when it is useful: on a button, while placing a panel, or while the drydock is open.
            bool show = hovered != null || MissionDeck.Open || PanelGrab.Dragging;
            ray.enabled = show;
            if (show)
            {
                ray.SetPosition(0, origin);
                ray.SetPosition(1, origin + direction * distance);
                ray.widthMultiplier = hovered != null ? 0.01f : 0.005f;
            }

            bool pressed = Rig.FireHeld;
            // While a panel is riding the ray the trigger belongs to PanelGrab: it drops the block, it does not
            // press whatever button happened to drift under the crosshair.
            if (pressed && !triggerHeld && hovered != null && !PanelGrab.Dragging) hovered.Press();
            triggerHeld = pressed;
        }
    }
}
