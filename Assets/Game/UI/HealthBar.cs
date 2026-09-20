using TMPro;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Small billboarded bar that floats over a thing that can be hurt. Enemy bars stay hidden until something
    /// actually damages them, so a full wave does not fill the arena with UI.
    /// </summary>
    public sealed class HealthBar : MonoBehaviour
    {
        private Material fill;
        private TextMeshPro label;
        private Transform card;
        private float hideAt;
        private bool alwaysVisible;

        public static HealthBar Create(Transform parent, Vector3 localOffset, float width, Color color, bool alwaysVisible, string title = null)
        {
            var root = new GameObject("Health Bar").transform;
            root.SetParent(parent, false);
            root.localPosition = localOffset;

            var bar = root.gameObject.AddComponent<HealthBar>();
            bar.alwaysVisible = alwaysVisible;
            bar.card = root;

            float height = width * 0.12f;
            var backing = UiKit.Panel(root, "Backing", new Vector2(width * 1.06f, height * 2.1f), 0f, color);
            backing.SetColor("_Fill", new Color(0.02f, 0.03f, 0.06f, 0.75f));
            bar.fill = UiKit.Bar(root, "Fill", new Vector3(-width * 0.5f, height * 0.5f, -0.002f), new Vector2(width, height), 16, color);

            if (title != null)
            {
                bar.label = UiKit.Text(root, "Label", new Vector3(0f, height * 1.5f, -0.002f), height * 1.1f, UiKit.Label,
                    color, TextAlignmentOptions.Center, width: width * 1.4f, tracking: 10f, uppercase: true);
                bar.label.text = title;
            }
            root.gameObject.AddComponent<Billboard>();
            if (!alwaysVisible) root.gameObject.SetActive(false);
            return bar;
        }

        /// <summary>Shows the bar for a few seconds after damage; full-health enemies fade back out.</summary>
        public void Set(float fraction, float showSeconds = 3f)
        {
            fraction = Mathf.Clamp01(fraction);
            fill.SetFloat("_Fill", fraction);
            var color = fraction > 0.5f ? UiKit.Cyan : fraction > 0.25f ? UiKit.Amber : UiKit.Alien;
            fill.SetColor("_On", color);
            fill.SetFloat("_Pulse", fraction <= 0.25f ? 1f : 0f);
            if (label != null) label.color = color;

            if (alwaysVisible) return;
            hideAt = Time.time + showSeconds;
            if (!card.gameObject.activeSelf) card.gameObject.SetActive(true);
        }

        public void SetTitle(string text)
        {
            if (label != null) label.text = text;
        }

        private void LateUpdate()
        {
            if (alwaysVisible || !card.gameObject.activeSelf) return;
            if (Time.time > hideAt) card.gameObject.SetActive(false);
        }
    }
}
