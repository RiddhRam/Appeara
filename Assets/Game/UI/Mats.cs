using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Armory
{
    /// <summary>Runtime URP materials for placeholder art. Cached by colour so rapid fire doesn't leak materials.</summary>
    public static class Mats
    {
        private static readonly Dictionary<(Color, int), Material> cache = new Dictionary<(Color, int), Material>();

        public static Material Lit(Color color, float emission = 0f)
        {
            var key = (color, 1 + (int)(emission * 100));
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            material.SetFloat("_Smoothness", 0.6f);
            material.SetFloat("_Metallic", 0.4f);
            if (emission > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return cache[key] = material;
        }

        public static Material Glow(Color color, float alpha = 1f)
        {
            color.a = alpha;
            var key = (color, alpha < 1f ? -2 : -1);
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };
            material.SetColor("_BaseColor", color);
            if (alpha < 1f) MakeTransparent(material);
            return cache[key] = material;
        }

        private static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
            material.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        /// <summary>Primitive with no collider unless asked; placeholder art everywhere goes through here.</summary>
        public static GameObject Shape(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Material material, bool collider = false, string name = null)
        {
            var shape = GameObject.CreatePrimitive(type);
            if (name != null) shape.name = name;
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = localPosition;
            shape.transform.localScale = scale;
            shape.GetComponent<Renderer>().sharedMaterial = material;
            shape.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (!collider) Object.Destroy(shape.GetComponent<Collider>());
            return shape;
        }

        public static LineRenderer Line(Transform parent, Color color, float width)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Glow(color);
            line.widthMultiplier = width;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.numCapVertices = 2;
            return line;
        }
    }
}
