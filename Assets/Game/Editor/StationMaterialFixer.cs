using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Armory.Editor
{
    /// <summary>
    /// The scene's station is the .blend import, whose Blender node materials arrive in Unity untextured (all white).
    /// The .fbx export of the same scene carries the texture links. This extracts the FBX materials to .mat files,
    /// adds the normal maps, gives the hologram props a glowing transparent look, and remaps both the .blend and the
    /// .fbx importers onto them. The scene itself is untouched.
    /// </summary>
    public static class StationMaterialFixer
    {
        private const string Folder = "Assets/3D/Space Station";
        private const string FbxPath = Folder + "/Space Station Scene.fbx";
        private const string BlendPath = Folder + "/Space Station Scene.blend";
        private const string MaterialFolder = Folder + "/Materials";

        private static readonly string[] Holograms = { "hologram", "holo_green", "hologram_radar-blue", "windows" };

        [MenuItem("Armory/Fix Space Station Materials")]
        public static void Fix()
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder(Folder, "Materials");

            var created = new Dictionary<string, Material>();
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Material>())
            {
                string key = Key(source.name);
                string path = $"{MaterialFolder}/{key}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(source);
                    AssetDatabase.CreateAsset(material, path);
                }
                else material.CopyPropertiesFromMaterial(source);

                material.name = key;
                AddNormalMap(material);
                if (Holograms.Contains(key)) MakeHologram(material);
                else
                {
                    // Imported colours are tinted grey (0.8); textured surfaces read better at full albedo.
                    if (material.mainTexture != null) material.SetColor("_BaseColor", Color.white);
                    material.SetFloat("_Smoothness", Mathf.Min(material.GetFloat("_Smoothness"), 0.45f));
                }
                EditorUtility.SetDirty(material);
                created[key] = material;
            }
            AssetDatabase.SaveAssets();

            int blendRemaps = Remap(BlendPath, created);
            int fbxRemaps = Remap(FbxPath, created);
            Debug.Log($"Station materials: {created.Count} extracted to {MaterialFolder}; remapped {blendRemaps} on .blend, {fbxRemaps} on .fbx.");
        }

        /// <summary>"Station-Inside__Room_1_Done_UVMap_color_3_jpg" and "Station-Inside" both → "station-inside".</summary>
        private static string Key(string materialName)
        {
            int split = materialName.IndexOf("__");
            string head = split > 0 ? materialName.Substring(0, split) : materialName;
            return head.Replace(' ', '_').Replace('.', '_').ToLowerInvariant();
        }

        private static int Remap(string modelPath, Dictionary<string, Material> materials)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return 0;
            int count = 0;
            foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().ToArray())
            {
                if (!materials.TryGetValue(Key(embedded.name), out var target)) continue;
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), target);
                count++;
            }
            importer.SaveAndReimport();
            return count;
        }

        private static void AddNormalMap(Material material)
        {
            var albedo = material.mainTexture;
            if (albedo == null) return;
            string albedoPath = AssetDatabase.GetAssetPath(albedo);
            string stem = Path.GetFileNameWithoutExtension(albedoPath);
            int cut = stem.IndexOf("_UVMap");
            if (cut < 0) return;
            string prefix = stem.Substring(0, cut);
            // The source pack has a typo ("Railings_texures"), so match loosely on the first word.
            string first = prefix.Split('_')[0];
            string normalPath = Directory.GetFiles(Folder, "*_nmap.jpg")
                .Select(p => p.Replace('\\', '/'))
                .FirstOrDefault(p => Path.GetFileName(p).StartsWith(prefix) || Path.GetFileName(p).StartsWith(first));
            if (normalPath == null) return;

            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            if (textureImporter.textureType != TextureImporterType.NormalMap)
            {
                textureImporter.textureType = TextureImporterType.NormalMap;
                textureImporter.SaveAndReimport();
            }
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }

        private static void MakeHologram(Material material)
        {
            Color color = material.GetColor("_BaseColor");
            float alpha = material.name == "windows" ? 0.25f : 0.35f;
            material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, alpha));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            if (material.name != "windows")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.5f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
        }
    }
}
