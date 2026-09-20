using TMPro;
using UnityEngine;

namespace Armory
{
    /// <summary>Floating combat popups (WEAK!, RESISTED, SHIELD DOWN...) in the HUD label face.</summary>
    public static class WorldText
    {
        /// <param name="size">Legacy scale knob: 0.05 ≈ a readable callout at 15-25 m.</param>
        public static void Popup(Vector3 position, string message, Color color, float size = 0.05f)
        {
            var text = UiKit.Text(null, "Popup", position, size * 5f, UiKit.Label, color, TextAlignmentOptions.Center, width: 6f, tracking: 8f, uppercase: true);
            text.transform.position = position;
            text.text = message;
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color32(4, 8, 16, 220);
            text.gameObject.AddComponent<FloatAway>();
        }

        private sealed class FloatAway : MonoBehaviour
        {
            private float age;
            private TextMeshPro text;

            private void Awake() => text = GetComponent<TextMeshPro>();

            private void Update()
            {
                age += Time.deltaTime;
                transform.position += Vector3.up * (1.2f * Time.deltaTime);
                var cam = Camera.main;
                if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
                if (text != null) text.alpha = Mathf.Clamp01((1f - age) * 3f);
                if (age > 1f) Destroy(gameObject);
            }
        }
    }

    /// <summary>Keeps a panel facing the viewer.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
