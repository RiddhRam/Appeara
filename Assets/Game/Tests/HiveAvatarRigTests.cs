using System.Collections.Generic;
using Armory.Core;
using NUnit.Framework;
using UnityEngine;

namespace Armory.Tests
{
    /// <summary>
    /// Binding the authored HiveAvatarVisual prefab. The prefab wraps the same rigged FBX the runtime used to
    /// instantiate on its own, so both the authored sockets and the raw bones are present and the rig has to
    /// prefer the sockets; older assets without the prefab still have to work off the bone names.
    /// </summary>
    public class HiveAvatarRigTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private Transform Root(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go.transform;
        }

        private Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private Renderer Visual(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Renderer>();
        }

        /// <summary>The real shape: root -> FBX armature -> bones, with sockets hung off the bones.</summary>
        private Transform AuthoredPrefab()
        {
            var root = Root("HiveAvatarVisual");
            var armature = Child(root, "Armature");
            for (int i = 0; i < HiveAvatarRig.BoneNames.Length; i++)
            {
                var bone = Child(armature, HiveAvatarRig.BoneNames[i]);
                var socket = Child(bone, HiveAvatarRig.SocketNames[i]);
                Visual(socket, HiveAvatarRig.SocketNames[i].Replace("_Socket", "_CoreVisual"));
            }
            for (int i = 1; i <= 8; i++) Visual(armature, HiveAvatarRig.PlateNamePrefix + " " + i);
            return root;
        }

        [Test]
        public void AuthoredSocketsBindInOrganOrder()
        {
            var rig = HiveAvatarRig.Bind(AuthoredPrefab());

            Assert.IsTrue(rig.Authored);
            Assert.IsTrue(rig.IsComplete);
            Assert.AreEqual("LeftClaw_Socket", rig.Anchors[(int)HiveOrgan.LeftClaw].name);
            Assert.AreEqual("RightClaw_Socket", rig.Anchors[(int)HiveOrgan.RightClaw].name);
            Assert.AreEqual("SporeSac_Socket", rig.Anchors[(int)HiveOrgan.SporeSac].name);
            Assert.AreEqual("Crest_Socket", rig.Anchors[(int)HiveOrgan.Crest].name);
        }

        [Test]
        public void AuthoredSocketsWinOverTheBonesTheyHangFrom()
        {
            var rig = HiveAvatarRig.Bind(AuthoredPrefab());

            foreach (var anchor in rig.Anchors) Assert.IsTrue(anchor.name.EndsWith("_Socket"), anchor.name + " is not a socket");
        }

        [Test]
        public void FallsBackToTheModelBonesWhenThereAreNoSockets()
        {
            var root = Root("Alien Animal_Fbx_7.4");
            var armature = Child(root, "Armature");
            foreach (string bone in HiveAvatarRig.BoneNames) Child(armature, bone);

            var rig = HiveAvatarRig.Bind(root);

            Assert.IsFalse(rig.Authored);
            Assert.IsTrue(rig.IsComplete);
            Assert.AreEqual(HiveAvatarRig.BoneNames[(int)HiveOrgan.SporeSac], rig.Anchors[(int)HiveOrgan.SporeSac].name);
            Assert.IsEmpty(rig.Plates);
        }

        [Test]
        public void OnlyTheChitinScalesBecomePlatingRenderers()
        {
            var root = AuthoredPrefab();
            Visual(root, "Alien Animal Body");

            var rig = HiveAvatarRig.Bind(root);

            Assert.AreEqual(8, rig.Plates.Length);
            foreach (var plate in rig.Plates) Assert.IsTrue(plate.name.StartsWith(HiveAvatarRig.PlateNamePrefix), plate.name + " is not a scale");
        }

        [Test]
        public void AMissingSocketLeavesTheRigIncompleteRatherThanMisaligned()
        {
            var root = AuthoredPrefab();
            var crest = root.Find("Armature/" + HiveAvatarRig.BoneNames[(int)HiveOrgan.Crest]);
            Object.DestroyImmediate(crest.Find(HiveAvatarRig.SocketNames[(int)HiveOrgan.Crest]).gameObject);

            var rig = HiveAvatarRig.Bind(root);

            Assert.IsFalse(rig.IsComplete);
            Assert.IsNull(rig.Anchors[(int)HiveOrgan.Crest]);
            Assert.IsNotNull(rig.Anchors[(int)HiveOrgan.LeftClaw]);
        }

        [Test]
        public void HidingAnchorCoresRemovesTheGlowThePlayerCannotShoot()
        {
            var root = AuthoredPrefab();
            var rig = HiveAvatarRig.Bind(root);

            rig.HideAnchorCores();

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.EndsWith("_CoreVisual")) Assert.IsFalse(renderer.enabled, renderer.name + " still renders");
                else Assert.IsTrue(renderer.enabled, renderer.name + " should be untouched");
        }
    }
}
