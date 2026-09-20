using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Armory.Editor
{
    public static class DemoArtPreview
    {
        [MenuItem("Armory/Art/Render Effect Sheet")]
        public static void Effects()
        {
            Directory.CreateDirectory("Temp/ArtPreview");
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Art/Effects", "Assets/Game/Art/Status", "Assets/Game/Art/Trails" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x).ToArray();
            const int cell = 256, columns = 5;
            var sheet = new Texture2D(cell * columns, cell * Mathf.CeilToInt(paths.Length / (float)columns), TextureFormat.RGB24, false);
            sheet.SetPixels(Enumerable.Repeat(new Color(.025f, .045f, .075f), sheet.width * sheet.height).ToArray()); sheet.Apply();
            var scene = EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(cell, cell, 24); var old = RenderTexture.active;
            Camera camera = null;
            try
            {
                var cameraObject = new GameObject("Effect camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.targetTexture = rt;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .045f, .075f);
                camera.orthographic = true;
                for (int i = 0; i < paths.Length; i++)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]), scene);
                    try
                    {
                        bool muzzle = paths[i].Contains("MuzzleFlash"); bool shock = paths[i].Contains("Shockwave"); bool beam = paths[i].Contains("LaserBeam");
                        foreach (var particle in instance.GetComponentsInChildren<ParticleSystem>())
                        { var main = particle.main; main.stopAction = ParticleSystemStopAction.None; particle.useAutoRandomSeed = false; particle.randomSeed = 17; particle.Simulate(muzzle ? .025f : .25f, false, true, true); }
                        var trail = instance.GetComponent<TrailRenderer>();
                        if (trail != null) { trail.Clear(); for (int j = 0; j < 16; j++) trail.AddPosition(new Vector3(-.6f + j * .08f, Mathf.Sin(j * .3f) * .15f, 0)); }
                        camera.orthographicSize = shock ? 2.7f : beam ? 5 : muzzle ? .1f : paths[i].Contains("Status") ? 1.1f : .8f;
                        var center = beam ? new Vector3(0, 0, 4) : paths[i].Contains("Status") ? Vector3.up * .7f : Vector3.zero;
                        camera.transform.position = center + (shock ? new Vector3(0, 5, -2) : beam ? new Vector3(5, 2, 0) : new Vector3(0, .6f, -4));
                        camera.transform.LookAt(center); camera.Render(); RenderTexture.active = rt;
                        sheet.ReadPixels(new Rect(0, 0, cell, cell), i % columns * cell, sheet.height - (i / columns + 1) * cell); sheet.Apply();
                    }
                    finally { Object.DestroyImmediate(instance); }
                }
                File.WriteAllBytes("Temp/ArtPreview/Effects.png", sheet.EncodeToPNG());
                File.WriteAllLines("Temp/ArtPreview/Effects-index.txt", paths);
            }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = old; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(sheet); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("Armory/Art/Render Preview Sheets")]
        public static void Render()
        {
            Directory.CreateDirectory("Temp/ArtPreview");
            string root = "Assets/Game/Art/HiveAvatar/";
            foreach (var name in new[] { "Stomp", "Fireball", "Laser", "Enrage", "DeathSettle" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(root + "Animations/" + name + ".anim");
                float[] times = name == "Stomp" ? new[] { 0f, 1.2f, 1.5f, 2.3f } : new[] { 0f, clip.length / 3, clip.length * 2 / 3, clip.length };
                Sheet(root + "HiveAvatarVisual.prefab", name, clip, times, true);
            }
            foreach (string name in new[] { "Grunt", "Swarm", "Armored", "Fast", "Shielded" })
                Sheet("Assets/Game/Art/Enemies/Authored/" + name + "Visual.prefab", name, null, new[] { 0f, .5f }, false);
            Debug.Log("Art preview PNGs saved under Temp/ArtPreview. Preview scenes closed; gameplay scene untouched.");
        }

        private static void Sheet(string path, string name, AnimationClip clip, float[] times, bool boss)
        {
            const int width = 640, height = 480;
            var scene = EditorSceneManager.NewPreviewScene();
            var sheet = new Texture2D(width * times.Length, height, TextureFormat.RGB24, false);
            var rt = new RenderTexture(width, height, 24);
            var old = RenderTexture.active;
            Camera camera = null;
            var bakedMeshes = new System.Collections.Generic.List<UnityEngine.Mesh>();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var animator = instance.GetComponentInChildren<Animator>();
                foreach (var a in instance.GetComponentsInChildren<Animator>()) a.enabled = false;
                foreach (var r in instance.GetComponentsInChildren<Renderer>()) if (r.name.EndsWith("_CoreVisual")) r.enabled = false;
                var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); SceneManager.MoveGameObjectToScene(plane, scene);
                plane.transform.localScale = Vector3.one * (boss ? 10 : 2);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = new Color(.12f, .16f, .2f);
                plane.GetComponent<Renderer>().sharedMaterial = material;
                var cameraObject = new GameObject("Preview camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.targetTexture = rt;
                camera.backgroundColor = new Color(.025f, .045f, .075f); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.fieldOfView = boss ? 65 : 40;
                var key = new GameObject("Key"); SceneManager.MoveGameObjectToScene(key, scene);
                var light = key.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; key.transform.rotation = Quaternion.Euler(40, -30, 0);
                var fill = new GameObject("Fill"); SceneManager.MoveGameObjectToScene(fill, scene);
                var fillLight = fill.AddComponent<Light>(); fillLight.type = LightType.Directional; fillLight.intensity = .8f; fillLight.color = new Color(.4f, .65f, 1); fill.transform.rotation = Quaternion.Euler(25, 160, 0);
                var renderers = instance.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                var baked = skins.Select(s =>
                {
                    var g = new GameObject("Sampled skin"); g.transform.SetParent(s.transform, false);
                    var filter = g.AddComponent<MeshFilter>(); var mesh = new UnityEngine.Mesh(); bakedMeshes.Add(mesh); filter.sharedMesh = mesh;
                    g.AddComponent<MeshRenderer>().sharedMaterials = s.sharedMaterials; s.enabled = false; return filter;
                }).ToArray();
                for (int i = 0; i < times.Length; i++)
                {
                    if (clip != null) clip.SampleAnimation(animator.gameObject, Mathf.Min(times[i], clip.length - .0001f));
                    else foreach (var a in instance.GetComponentsInChildren<Animator>())
                        if (a.runtimeAnimatorController != null)
                        {
                            var sample = a.runtimeAnimatorController.animationClips.FirstOrDefault();
                            if (sample != null) sample.SampleAnimation(a.gameObject, times[i]);
                        }
                    for (int s = 0; s < skins.Length; s++) skins[s].BakeMesh(baked[s].sharedMesh, true);
                    if (boss) camera.transform.position = new Vector3(13, 1.7f, 20);
                    else camera.transform.position = bounds.center + new Vector3(i == 0 ? 1 : -1, .3f, 2) * bounds.size.y * 1.6f;
                    camera.transform.LookAt(boss ? new Vector3(0, 6, 0) : bounds.center);
                    camera.Render(); RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, width, height), i * width, 0); sheet.Apply();
                }
                File.WriteAllBytes("Temp/ArtPreview/" + name + ".png", sheet.EncodeToPNG());
                Object.DestroyImmediate(material);
            }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = old; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(sheet);
                foreach (var mesh in bakedMeshes) Object.DestroyImmediate(mesh);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
