using System;
using TMPro;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// World-space button pressed by pointing the right controller at it and pulling the trigger.
    /// Uses its own collider; <see cref="MissionDeck"/> does the raycasting so nothing else needs a UI layer.
    /// </summary>
    public sealed class HudButton : MonoBehaviour
    {
        public Action OnPress;
        public bool Enabled = true;

        private Material background;
        private TextMeshPro label;
        private Color tint;
        private bool hovered;

        public static HudButton Create(Transform parent, Vector3 localPosition, Vector2 size, string text, Color color, Action onPress)
        {
            var root = new GameObject("Button: " + text);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            var button = root.AddComponent<HudButton>();
            button.tint = color;
            button.OnPress = onPress;
            button.background = UiKit.Panel(root.transform, "Face", size, 0f, color);
            button.label = UiKit.Text(root.transform, "Label", new Vector3(0f, 0f, -0.002f), size.y * 0.42f, UiKit.Label,
                color, TextAlignmentOptions.Center, width: size.x * 0.95f, tracking: 10f, uppercase: true);
            button.label.text = text;

            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, 0.02f);
            button.SetHovered(false);
            return button;
        }

        public void SetLabel(string text) => label.text = text;

        public void SetHovered(bool on)
        {
            hovered = on && Enabled;
            Color face = !Enabled ? new Color(0.35f, 0.4f, 0.48f) : hovered ? Color.white : tint;
            background.SetColor("_Line", new Color(face.r, face.g, face.b, hovered ? 0.95f : 0.5f));
            background.SetColor("_Fill", hovered
                ? new Color(tint.r * 0.35f, tint.g * 0.35f, tint.b * 0.4f, 0.92f)
                : new Color(0.012f, 0.02f, 0.045f, 0.88f));
            label.color = Enabled ? (hovered ? Color.white : tint) : new Color(0.45f, 0.5f, 0.58f);
        }

        public void Press()
        {
            if (!Enabled) return;
            ProceduralSfx.PlayAt(ProceduralSfx.Blip, transform.position, 0.7f);
            OnPress?.Invoke();
        }
    }
}
