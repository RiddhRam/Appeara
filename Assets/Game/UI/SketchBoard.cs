using System;
using TMPro;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Holographic drawing board. Point the right controller at it and hold the trigger to draw; what you sketch is
    /// sent to the model with your next spoken request, so "make this, but electric" works. Sits beside the player
    /// during the armory phase.
    /// </summary>
    public sealed class SketchBoard : MonoBehaviour
    {
        public const int Resolution = 320;
        private const float Width = 0.62f, Height = 0.62f;

        public static SketchBoard Instance { get; private set; }

        public ArmoryRig Rig;
        public bool HasInk { get; private set; }

        private Texture2D canvas;
        private Color32[] pixels;
        private Material surface;
        private TextMeshPro hint;
        private Transform board;
        private Vector2? lastPixel;
        private bool triggerHeld;

        private void Awake() => Instance = this;

        private void Start()
        {
            canvas = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false) { name = "Sketch", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            pixels = new Color32[Resolution * Resolution];
            Build();
            Clear();
        }

        private void Build()
        {
            board = new GameObject("Board").transform;
            board.SetParent(transform, false);

            var frame = UiKit.Panel(board, "Frame", new Vector2(Width + 0.06f, Height + 0.12f), 0.05f);
            frame.SetColor("_Fill", new Color(0.02f, 0.04f, 0.07f, 0.9f));

            UiKit.Text(board, "Title", new Vector3(-Width / 2f, Height / 2f + 0.042f, -0.003f), 0.022f, UiKit.Label,
                UiKit.CyanDim, tracking: 16f, uppercase: true).text = "Sketch // fabricator input";

            surface = Mats.Glow(Color.white);
            surface.mainTexture = canvas;
            var quad = Mats.Shape(PrimitiveType.Quad, board, new Vector3(0f, -0.02f, -0.004f), new Vector3(Width, Height, 1f), surface, collider: true, name: "Canvas");
            quad.GetComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.02f);

            hint = UiKit.Text(board, "Hint", new Vector3(0f, -Height / 2f - 0.05f, -0.003f), 0.02f, UiKit.Mono, UiKit.Muted,
                TextAlignmentOptions.Top, width: Width, uppercase: true);
            hint.text = "hold trigger to draw · B clears";
        }

        /// <summary>Places the board within reach on the player's left, angled toward them.</summary>
        public void Show(bool on)
        {
            board.gameObject.SetActive(on);
            if (!on) return;
            var head = Rig.Head.transform;
            Vector3 forward = head.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward) * -1f;
            transform.position = head.position + forward * 0.75f + right * 0.55f - Vector3.up * 0.25f;
            transform.rotation = Quaternion.LookRotation(transform.position - head.position);
        }

        public bool Visible => board != null && board.gameObject.activeSelf;

        private void Update()
        {
            if (Rig == null || !Visible || MissionDeck.Open) { lastPixel = null; return; }

            if (Rig.DropPressed) Clear();

            bool drawing = false;
            if (Rig.FireHeld && Physics.Raycast(Rig.Aim.position, Rig.Aim.forward, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.gameObject.name == "Canvas")
            {
                var uv = hit.textureCoord;
                Paint(new Vector2(uv.x * Resolution, uv.y * Resolution));
                drawing = true;
            }
            if (!drawing) lastPixel = null;
            triggerHeld = Rig.FireHeld;
        }

        /// <summary>Draws a straight stroke in canvas pixels. Used by tests and scripted demos.</summary>
        public void Stroke(Vector2 from, Vector2 to)
        {
            lastPixel = from;
            Paint(to);
            lastPixel = null;
        }

        private void Paint(Vector2 point)
        {
            var from = lastPixel ?? point;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, point)));
            for (int i = 0; i <= steps; i++) Dab(Vector2.Lerp(from, point, i / (float)steps));
            lastPixel = point;
            canvas.SetPixels32(pixels);
            canvas.Apply(false);
            HasInk = true;
        }

        private void Dab(Vector2 centre)
        {
            const int radius = 4;
            var ink = new Color32(90, 230, 255, 255);
            int cx = Mathf.RoundToInt(centre.x), cy = Mathf.RoundToInt(centre.y);
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radius * radius) continue;
                    int px = cx + x, py = cy + y;
                    if (px < 0 || px >= Resolution || py < 0 || py >= Resolution) continue;
                    pixels[py * Resolution + px] = ink;
                }
        }

        public void Clear()
        {
            var background = new Color32(6, 12, 22, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;
            // Faint grid so the board reads as a drafting surface.
            var line = new Color32(14, 34, 54, 255);
            for (int i = 0; i < Resolution; i += Resolution / 8)
                for (int j = 0; j < Resolution; j++)
                {
                    pixels[j * Resolution + i] = line;
                    pixels[i * Resolution + j] = line;
                }
            canvas.SetPixels32(pixels);
            canvas.Apply(false);
            HasInk = false;
            lastPixel = null;
        }

        /// <summary>PNG of the drawing for the model, or null when nothing has been drawn.</summary>
        public string EncodeBase64()
        {
            if (!HasInk) return null;
            return Convert.ToBase64String(canvas.EncodeToPNG());
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas);
        }
    }
}
