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
            hologram.material = shader != null ? new Material(shader) : new Material(Mats.Glow(new Color(0.3f, 0.9f, 1f), 0.5f));
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

        public void Show(Texture2D texture, string title)
        {
            gameObject.SetActive(true);
            material.SetTexture(MainTex, texture);
            material.SetFloat(HasTex, 1f);
            material.SetFloat(Reveal, 0f);
            revealStart = Time.time;
            caption.text = title + "\n<size=60%><color=#6A8AA0>schematic // ai concept render</color></size>";
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
