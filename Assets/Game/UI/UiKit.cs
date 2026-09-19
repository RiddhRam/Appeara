using TMPro;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// HUD design system: palette, IBM Plex type (runtime TMP font assets from Resources/Fonts), chamfered glass
    /// panels and segmented gauges. All sizes are metres in world space.
    /// </summary>
    public static class UiKit
    {
        // Palette: station cyan, warning amber, alien red on deep navy glass.
        public static readonly Color Cyan = new Color(0.42f, 0.88f, 1f);
        public static readonly Color CyanDim = new Color(0.42f, 0.88f, 1f, 0.55f);
        public static readonly Color Amber = new Color(1f, 0.72f, 0.28f);
        public static readonly Color Alien = new Color(1f, 0.33f, 0.43f);
        public static readonly Color Go = new Color(0.45f, 1f, 0.6f);
        public static readonly Color Ink = new Color(0.9f, 0.95f, 1f);
        public static readonly Color Muted = new Color(0.55f, 0.63f, 0.74f);

        /// <summary>TMP 3D text: world line height per point of fontSize (measured 2026-09-19: size 10 → 1.3 m line).</summary>
        public const float MetresPerPoint = 0.13f;

        private static TMP_FontAsset label, body, mono, monoStrong;
        public static TMP_FontAsset Label => label != null ? label : label = Load("IBMPlexSansCondensed-SemiBold");
        public static TMP_FontAsset Body => body != null ? body : body = Load("IBMPlexSansCondensed-Medium");
        public static TMP_FontAsset Mono => mono != null ? mono : mono = Load("IBMPlexMono-Regular");
        public static TMP_FontAsset MonoStrong => monoStrong != null ? monoStrong : monoStrong = Load("IBMPlexMono-Medium");

        private static TMP_FontAsset Load(string file)
        {
            var font = Resources.Load<Font>("Fonts/" + file);
            if (font == null) { Debug.LogWarning("UiKit: missing font " + file); return TMP_Settings.defaultFontAsset; }
            var asset = TMP_FontAsset.CreateFontAsset(font, 72, 8, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            asset.name = file;
            return asset;
        }

        /// <param name="height">Cap-to-descender line height in metres.</param>
        public static TextMeshPro Text(Transform parent, string name, Vector3 localPosition, float height, TMP_FontAsset font, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft, float width = 0f, float tracking = 0f, bool uppercase = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSize = height / MetresPerPoint;
            text.color = color;
            text.alignment = align;
            text.characterSpacing = tracking;
            text.fontStyle = uppercase ? FontStyles.UpperCase : FontStyles.Normal;
            text.enableWordWrapping = width > 0f;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            var rect = (RectTransform)go.transform;
            rect.pivot = PivotFor(align);
            rect.sizeDelta = new Vector2(width > 0f ? width : 2f, height * 2f);
            rect.localPosition = localPosition;
            text.sortingOrder = 1;
            return text;
        }

        private static Vector2 PivotFor(TextAlignmentOptions align)
        {
            switch (align)
            {
                case TextAlignmentOptions.TopRight: return new Vector2(1f, 1f);
                case TextAlignmentOptions.Top: return new Vector2(0.5f, 1f);
                case TextAlignmentOptions.Center: return new Vector2(0.5f, 0.5f);
                case TextAlignmentOptions.Right: return new Vector2(1f, 0.5f);
                case TextAlignmentOptions.Left: return new Vector2(0f, 0.5f);
                default: return new Vector2(0f, 1f);
            }
        }

        /// <summary>Glass card centred on its transform. Returns the material so callers can tint lines (e.g. alien red).</summary>
        public static Material Panel(Transform parent, string name, Vector2 size, float headerHeight, Color? line = null)
        {
            var shader = Shader.Find("Armory/HudPanel");
            var material = shader != null ? new Material(shader) : new Material(Mats.Glow(new Color(0.02f, 0.03f, 0.06f), 0.8f));
            material.SetVector("_Size", size);
            material.SetFloat("_HeaderHeight", headerHeight);
            if (line.HasValue)
            {
                var c = line.Value;
                material.SetColor("_Line", new Color(c.r, c.g, c.b, 0.55f));
                material.SetColor("_Accent", c * 1.2f);
                material.SetColor("_HeaderFill", new Color(c.r * 0.18f, c.g * 0.18f, c.b * 0.25f, 0.9f));
            }
            var quad = Mats.Shape(PrimitiveType.Quad, parent, new Vector3(0f, 0f, 0.002f), new Vector3(size.x, size.y, 1f), material, name: name);
            return material;
        }

        public static Material Bar(Transform parent, string name, Vector3 localPosition, Vector2 size, int segments, Color on)
        {
            var shader = Shader.Find("Armory/HudBar");
            var material = shader != null ? new Material(shader) : new Material(Mats.Glow(on));
            material.SetFloat("_Segments", segments);
            material.SetColor("_On", on);
            Mats.Shape(PrimitiveType.Quad, parent, localPosition + new Vector3(size.x * 0.5f, -size.y * 0.5f, 0.001f), new Vector3(size.x, size.y, 1f), material, name: name);
            return material;
        }

        /// <summary>Thin rule line (for banners and separators), left-anchored.</summary>
        public static void Rule(Transform parent, Vector3 localPosition, float length, Color color, float thickness = 0.0012f)
        {
            Mats.Shape(PrimitiveType.Quad, parent, localPosition + new Vector3(length * 0.5f, 0f, 0.001f), new Vector3(length, thickness, 1f), Mats.Glow(color, color.a), name: "Rule");
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
