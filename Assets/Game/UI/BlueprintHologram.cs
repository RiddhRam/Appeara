using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Floating card rendered with Armory/HologramBlueprint. Shows a scanning grid while the AI image generates,
    /// then sweeps the blueprint in from the bottom.
    /// </summary>
    public sealed class BlueprintHologram : MonoBehaviour
    {
        private static readonly int HasTex = Shader.PropertyToID("_HasTex");
        private static readonly int Reveal = Shader.PropertyToID("_Reveal");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private const float RevealSeconds = 1.6f;

        private Material material;
        private TMPro.TextMeshPro caption;
        private bool yawOnly;
        private float revealStart = -1f;

        public static BlueprintHologram Create(string objectName, Transform parent, Vector3 position, float size, bool worldPosition, bool yawOnly)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(parent, false);
            if (worldPosition) root.transform.position = position;
            else root.transform.localPosition = position;
            var hologram = root.AddComponent<BlueprintHologram>();
            hologram.yawOnly = yawOnly;

            var shader = Shader.Find("Armory/HologramBlueprint");
            if (shader != null) hologram.material = new Material(shader);
            else
            {
                // Keep the generated image visible even if the custom hologram shader was stripped from a build.
                var fallbackShader = Shader.Find("Universal Render Pipeline/Unlit");
                hologram.material = new Material(fallbackShader);
                hologram.material.SetColor("_BaseColor", Color.white);
            }
            Mats.Shape(PrimitiveType.Quad, root.transform, Vector3.zero, Vector3.one * size, hologram.material, name: "Card");
            hologram.caption = UiKit.Text(root.transform, "Caption", new Vector3(0f, -size * 0.53f, 0f), size * 0.045f, UiKit.Label, UiKit.Cyan, TMPro.TextAlignmentOptions.Top, width: size * 1.4f, tracking: 14f, uppercase: true);
            root.SetActive(false);
            return hologram;
        }

        public void SetPending(string title)
        {
            gameObject.SetActive(true);
            material.SetFloat(HasTex, 0f);
            material.SetFloat(Reveal, 1.1f);
            revealStart = -1f;
            caption.text = title + "\n<size=60%><color=#6A8AA0>rendering schematic</color></size>";
        }

        public void Show(Texture2D texture, string title, string source = "ai concept render")
        {
            gameObject.SetActive(true);
            material.SetTexture(MainTex, texture);
            material.SetTexture(BaseMap, texture);
            material.SetFloat(HasTex, 1f);
            material.SetFloat(Reveal, 0f);
            revealStart = Time.time;
            caption.text = title + "\n<size=60%><color=#6A8AA0>schematic // " + source + "</color></size>";
        }

        /// <summary>A small readable schematic used only when the image endpoint or texture decode fails.</summary>
        public static Texture2D CreateFallbackTexture(string title, string designPrompt, Color accent)
        {
            const int size = 256;
            var pixels = new Color32[size * size];
            var background = new Color32(1, 5, 10, 255);
            var grid = new Color32(7, 31, 42, 255);
            var structure = new Color32(45, 105, 120, 255);
            var glow = (Color32)Color.Lerp(new Color(0.2f, 0.9f, 1f), accent, 0.45f);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;
            for (int p = 16; p < size; p += 24)
            {
                DrawRect(pixels, size, p, 8, p, size - 9, grid);
                DrawRect(pixels, size, 8, p, size - 9, p, grid);
            }

            string description = ((designPrompt ?? "") + " " + (title ?? "")).ToLowerInvariant();
            bool launcher = description.Contains("launcher") || description.Contains("rocket") || description.Contains("bazooka") || description.Contains("cannon");
            bool machineGun = description.Contains("machine gun") || description.Contains("machinegun") || description.Contains("minigun") || description.Contains("gatling");
            bool pistol = description.Contains("pistol") || description.Contains("handgun") || description.Contains("sidearm") || description.Contains("revolver");

            if (launcher)
            {
                FillRect(pixels, size, 50, 110, 211, 151, structure);
                DrawRect(pixels, size, 48, 108, 214, 153, glow);
                DrawCircle(pixels, size, 215, 131, 24, glow);
                FillRect(pixels, size, 18, 119, 50, 142, structure);
                FillRect(pixels, size, 82, 78, 103, 110, structure);
            }
            else if (machineGun)
            {
                FillRect(pixels, size, 72, 112, 166, 151, structure);
                DrawRect(pixels, size, 70, 110, 168, 153, glow);
                for (int y = 118; y <= 146; y += 14) FillRect(pixels, size, 166, y, 229, y + 5, glow);
                FillRect(pixels, size, 95, 75, 126, 111, structure);
                FillRect(pixels, size, 25, 120, 70, 143, structure);
            }
            else if (pistol)
            {
                FillRect(pixels, size, 80, 116, 210, 148, structure);
                DrawRect(pixels, size, 78, 114, 213, 150, glow);
                FillRect(pixels, size, 99, 68, 127, 115, structure);
            }
            else
            {
                FillRect(pixels, size, 55, 111, 199, 151, structure);
                DrawRect(pixels, size, 53, 109, 202, 153, glow);
                FillRect(pixels, size, 88, 72, 115, 110, structure);
                FillRect(pixels, size, 199, 122, 230, 141, glow);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Local Blueprint Fallback", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void FillRect(Color32[] pixels, int size, int x0, int y0, int x1, int y1, Color32 color)
        {
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(size - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(size - 1, x1); x++)
                    pixels[y * size + x] = color;
        }

        private static void DrawRect(Color32[] pixels, int size, int x0, int y0, int x1, int y1, Color32 color)
        {
            FillRect(pixels, size, x0, y0, x1, y0 + 2, color);
            FillRect(pixels, size, x0, y1 - 2, x1, y1, color);
            FillRect(pixels, size, x0, y0, x0 + 2, y1, color);
            FillRect(pixels, size, x1 - 2, y0, x1, y1, color);
        }

        private static void DrawCircle(Color32[] pixels, int size, int cx, int cy, int radius, Color32 color)
        {
            int outer = radius * radius;
            int inner = (radius - 3) * (radius - 3);
            for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                int distance = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                if (distance >= inner && distance <= outer && x >= 0 && x < size && y >= 0 && y < size)
                    pixels[y * size + x] = color;
            }
        }

        private void LateUpdate()
        {
            if (revealStart >= 0f)
            {
                float t = (Time.time - revealStart) / RevealSeconds;
                material.SetFloat(Reveal, Mathf.Lerp(0f, 1.1f, 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t))));
                if (t >= 1f) revealStart = -1f;
            }

            var cam = Camera.main;
            if (cam == null) return;
            Vector3 away = transform.position - cam.transform.position;
            if (yawOnly) away.y = 0f;
            if (away.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(away);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
