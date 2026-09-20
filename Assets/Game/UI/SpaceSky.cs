using UnityEngine;
using UnityEngine.Rendering;

namespace Armory
{
    /// <summary>
    /// Swaps Unity's default daytime dome for the procedural Armory/SpaceSkybox and drops ambient light to
    /// something a space station would actually sit in. Done in code at runtime, like the rest of the scene build,
    /// so the scene file's lighting settings stay untouched and merge-friendly.
    /// </summary>
    public static class SpaceSky
    {
        private static readonly int PlanetDirId = Shader.PropertyToID("_PlanetDir");
        private static readonly int PlanetLightId = Shader.PropertyToID("_PlanetLight");
        private static readonly int PlanetSizeId = Shader.PropertyToID("_PlanetSize");

        /// <summary>The live skybox material, kept so repeated calls reuse one material instead of leaking them.</summary>
        public static Material Material { get; private set; }

        public static void Apply()
        {
            if (Material == null)
            {
                var shader = Shader.Find("Armory/SpaceSkybox");
                if (shader == null)
                {
                    Debug.LogWarning("SpaceSky: shader 'Armory/SpaceSkybox' not found; keeping the default sky.");
                    return;
                }
                Material = new Material(shader) { name = "Space Sky (runtime)" };
                // A planet sitting low reads best through the station windows, and lighting it from nearly the
                // opposite side leaves a visible terminator instead of a flat lit coin.
                Material.SetVector(PlanetDirId, new Vector4(0.62f, 0.10f, -0.78f, 0f));
                Material.SetVector(PlanetLightId, new Vector4(-0.45f, 0.35f, 0.82f, 0f));
                Material.SetFloat(PlanetSizeId, 0.13f);
            }

            RenderSettings.skybox = Material;
            // Trilight, not Skybox ambient: this sky is almost black, so sampling it would leave every prop and
            // alien unreadable. A cold key from above with a fainter bounce keeps silhouettes legible in VR.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.055f, 0.075f, 0.12f);
            RenderSettings.ambientEquatorColor = new Color(0.03f, 0.04f, 0.07f);
            RenderSettings.ambientGroundColor = new Color(0.012f, 0.014f, 0.022f);
            DynamicGI.UpdateEnvironment();
        }
    }
}
