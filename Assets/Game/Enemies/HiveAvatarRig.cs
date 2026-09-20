using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Binds the authored <c>HiveAvatarVisual</c> prefab to the encounter: the four organ sockets in
    /// <see cref="Armory.Core.HiveOrgan"/> order and the chitin scales that carry the plating adaptation.
    /// The prefab wraps the same rigged FBX the runtime used to instantiate on its own, so the bones are always
    /// present too and the sockets have to win. Models without the prefab fall back to the bone names, which
    /// keeps an older <c>HiveAvatarAssets</c> asset (Model set, Visual empty) working.
    /// </summary>
    public sealed class HiveAvatarRig
    {
        public static readonly string[] SocketNames = { "LeftClaw_Socket", "RightClaw_Socket", "SporeSac_Socket", "Crest_Socket" };
        /// <summary>Bones the authored sockets hang from; used directly when the art prefab is absent.</summary>
        public static readonly string[] BoneNames = { "Bone.005_L.004", "Bone.005_R.004", "Bone.005", "Bone.028" };
        public const string PlateNamePrefix = "Chitin Scale";
        private const string CoreVisualSuffix = "_CoreVisual";

        private static readonly Renderer[] NoPlates = new Renderer[0];

        /// <summary>Organ anchors in organ order. An entry is null when the model does not carry that organ.</summary>
        public Transform[] Anchors { get; }
        /// <summary>The authored chitin scales, empty for a model without the art prefab.</summary>
        public Renderer[] Plates { get; }
        /// <summary>True when the authored sockets were found, so the art prefab owns the layout and the plating.</summary>
        public bool Authored { get; }

        public bool IsComplete
        {
            get
            {
                foreach (var anchor in Anchors) if (anchor == null) return false;
                return true;
            }
        }

        private HiveAvatarRig(Transform[] anchors, Renderer[] plates, bool authored)
        {
            Anchors = anchors;
            Plates = plates;
            Authored = authored;
        }

        public static HiveAvatarRig Bind(Transform model)
        {
            var anchors = new Transform[SocketNames.Length];
            if (model == null) return new HiveAvatarRig(anchors, NoPlates, false);

            var transforms = model.GetComponentsInChildren<Transform>(true);
            bool authored = false;
            for (int i = 0; i < SocketNames.Length; i++)
                foreach (var candidate in transforms)
                    if (candidate.name == SocketNames[i]) { anchors[i] = candidate; authored = true; break; }

            // All or nothing: a half-socketed model would otherwise mix authored and bone anchors, which reads as
            // a working boss while one organ sits in the wrong place. Leave the gap so the caller can see it.
            if (!authored)
                for (int i = 0; i < BoneNames.Length; i++)
                    foreach (var candidate in transforms)
                        if (candidate.name == BoneNames[i]) { anchors[i] = candidate; break; }

            return new HiveAvatarRig(anchors, authored ? CollectPlates(model) : NoPlates, authored);
        }

        private static Renderer[] CollectPlates(Transform model)
        {
            var found = new System.Collections.Generic.List<Renderer>();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.StartsWith(PlateNamePrefix)) found.Add(renderer);
            return found.ToArray();
        }

        /// <summary>
        /// Turns off the little glow the prefab parents to each socket. The shootable organ is pushed clear of the
        /// torso by <see cref="Armory.Core.HiveTargets"/>, so the core left at the bone is a glowing target the
        /// player cannot hit.
        /// </summary>
        public void HideAnchorCores()
        {
            foreach (var anchor in Anchors)
            {
                if (anchor == null) continue;
                foreach (var renderer in anchor.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.EndsWith(CoreVisualSuffix)) renderer.enabled = false;
            }
        }
    }
}
