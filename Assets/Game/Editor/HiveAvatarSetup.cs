using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Armory.Editor
{
    public static class HiveAvatarSetup
    {
        [MenuItem("Armory/Prepare Hive Avatar Assets")]
        public static void Prepare()
        {
            const string modelPath = "Assets/AlienAnimal/Alien Animal_Fbx_7.4.fbx";
            const string folder = "Assets/Game/Enemies/Resources";
            const string path = folder + "/HiveAvatarAssets.asset";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Game/Enemies", "Resources");
            var assets = AssetDatabase.LoadAssetAtPath<HiveAvatarAssets>(path);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<HiveAvatarAssets>();
                AssetDatabase.CreateAsset(assets, path);
            }
            const string visualPath = "Assets/Game/Art/HiveAvatar/HiveAvatarVisual.prefab";
            assets.Visual = AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
            if (assets.Visual == null) Debug.LogWarning("Hive Avatar art prefab not found at " + visualPath + "; falling back to the bare model.");
            assets.Model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().ToArray();
            assets.Idle = clips.First(c => c.name == "Armature|Idle_Aggressive");
            assets.Walk = clips.First(c => c.name == "Armature|Walk-Cycle");
            assets.Attack = clips.First(c => c.name == "Armature|Attack_Hit");
            assets.Spit = clips.First(c => c.name == "Armature|Attack_Bite");
            assets.Death = clips.First(c => c.name == "Armature|Die_1");
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            Debug.Log((assets.Visual != null ? "Hive Avatar art prefab" : "Hive Avatar model") + " and five animation clips prepared. Scene unchanged.");
        }

        [MenuItem("Armory/Jump To Hive Avatar (Play Mode)")]
        public static void JumpToBoss()
        {
            if (!EditorApplication.isPlaying || WaveDirector.Instance == null) return;
            WaveDirector.Instance.RestartWave(4, 1f);
        }
    }
}
