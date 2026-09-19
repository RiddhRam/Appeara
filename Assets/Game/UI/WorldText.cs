using UnityEngine;

namespace Armory
{
    /// <summary>Legacy TextMesh helpers (no TMP essentials import needed) and floating combat popups.</summary>
    public static class WorldText
    {
        private static Font font;

        public static TextMesh Create(Transform parent, Vector3 localPosition, float size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var text = go.AddComponent<TextMesh>();
            text.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.anchor = anchor;
            text.alignment = anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.UpperLeft ? TextAlignment.Left : TextAlignment.Center;
            text.fontSize = 64;
            text.characterSize = size;
            text.color = color;
            return text;
        }

        public static void Popup(Vector3 position, string message, Color color, float size = 0.05f)
        {
            var text = Create(null, position, size, color);
            text.text = message;
            text.gameObject.AddComponent<FloatAway>();
        }

        private sealed class FloatAway : MonoBehaviour
        {
            private float age;

            private void Update()
            {
                age += Time.deltaTime;
                transform.position += Vector3.up * (1.2f * Time.deltaTime);
                var cam = Camera.main;
                if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
                if (age > 1f) Destroy(gameObject);
            }
        }
    }

    /// <summary>Keeps a text panel facing the viewer.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
