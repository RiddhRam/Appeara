using UnityEngine;
using UnityEngine.InputSystem;

namespace Armory
{
    /// <summary>
    /// Makes the floating panel block placeable instead of permanently glued to your face. Press MOVE and the whole
    /// block (comms card, drydock, armory prompt, banner) rides your aim ray; press the trigger again to drop it
    /// where it is. It then stays pinned in the world until you press DOCK, which hands it back to the head-follow.
    /// Head-following is right while you are turning to fight and wrong when you want the comms log parked on a wall.
    /// </summary>
    public sealed class PanelGrab : MonoBehaviour
    {
        /// <summary>True while the block is riding the ray, so the trigger places the panel instead of firing.</summary>
        public static bool Dragging { get; private set; }

        private const float MinDistance = 0.5f, MaxDistance = 8f, ButtonW = 0.21f, ButtonH = 0.072f;

        public ArmoryRig Rig;
        /// <summary>The anchor this moves; the owner stops driving it while <see cref="Pinned"/> is set.</summary>
        public Transform Target;

        /// <summary>Set once the block has been dropped somewhere; cleared by DOCK.</summary>
        public bool Pinned { get; private set; }

        private HudButton move, dock;
        private float distance = 2f;
        private bool triggerHeld;

        public static PanelGrab Attach(Transform target, Transform buttonParent, ArmoryRig rig, Vector3 buttonAnchor)
        {
            var grab = target.gameObject.AddComponent<PanelGrab>();
            grab.Rig = rig;
            grab.Target = target;
            grab.Build(buttonParent, buttonAnchor);
            return grab;
        }

        private void Build(Transform parent, Vector3 anchor)
        {
            move = HudButton.Create(parent, anchor, new Vector2(ButtonW, ButtonH), "Move", UiKit.Cyan, Begin);
            dock = HudButton.Create(parent, anchor + new Vector3(-ButtonW - 0.02f, 0f, 0f), new Vector2(ButtonW, ButtonH), "Dock", UiKit.Muted, Redock);
            dock.Enabled = false;
            dock.SetHovered(false);
        }

        private void Begin()
        {
            if (Rig == null || Target == null) return;
            Dragging = true;
            Pinned = true;
            // Pick the block up at the distance it is already at, so it does not jump on the first frame.
            distance = Mathf.Clamp(Vector3.Distance(Rig.Aim.position, Target.position), MinDistance, MaxDistance);
            // Consume the press that started the drag; otherwise the same trigger hold drops it immediately.
            triggerHeld = true;
            move.SetLabel("Drop");
        }

        private void Drop()
        {
            Dragging = false;
            move.SetLabel("Move");
            dock.Enabled = true;
            dock.SetHovered(false);
        }

        private void Redock()
        {
            Dragging = false;
            Pinned = false;
            move.SetLabel("Move");
            dock.Enabled = false;
            dock.SetHovered(false);
        }

        private void LateUpdate()
        {
            if (Rig == null || Target == null) return;
            bool pressed = Rig.FireHeld;
            if (!Dragging) { triggerHeld = pressed; return; }

            // Scroll pushes the block away and pulls it back; in the headset you just point further or walk.
            var mouse = Mouse.current;
            if (mouse != null && !Rig.TextEntryActive)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) distance = Mathf.Clamp(distance + Mathf.Sign(scroll) * 0.15f, MinDistance, MaxDistance);
            }

            Target.position = Rig.Aim.position + Rig.Aim.forward * distance;
            Vector3 toHead = Target.position - Rig.Head.transform.position;
            if (toHead.sqrMagnitude > 0.0001f) Target.rotation = Quaternion.LookRotation(toHead);

            // Any trigger press drops it: while dragging the block sits under the crosshair, not the MOVE button.
            if (pressed && !triggerHeld) Drop();
            triggerHeld = pressed;
        }

        private void OnDisable() => Dragging = false;
    }
}
