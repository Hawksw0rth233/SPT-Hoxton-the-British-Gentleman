using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx;
using Diz.Skinning;
using EFT;
using EFT.Visual;
using HarmonyLib;
using UnityEngine;

namespace Hawks.HoxtonHead
{
    internal static class HeadModule
    {
        private const string HeadId = "686a04000000000000000101";
        private static readonly ConditionalWeakTable<ResourceKey, object> HeadKeys =
            new ConditionalWeakTable<ResourceKey, object>();
        private static Mesh headMesh;
        private static Material headMaterial;

        internal static void Initialize(Harmony harmony, string directory)
        {
            string path = Path.Combine(directory, "hawks.hoxton.head.bundle");
            var bundle = AssetBundle.LoadFromFile(path);
            if (!bundle)
                throw new InvalidDataException("Hoxton head asset bundle failed to load.");
            var prefab = bundle.LoadAsset<GameObject>("assets/hawkshoxtonhead/hoxtonhead.prefab");
            var renderer = prefab.GetComponent<SkinnedMeshRenderer>();
            headMesh = renderer.sharedMesh;
            headMaterial = renderer.sharedMaterial;


            harmony.Patch(AccessTools.Method(typeof(CustomizationSolver), nameof(CustomizationSolver.GetBundle)),
                postfix: new HarmonyMethod(typeof(HeadModule), nameof(TagHeadBundle)));
            harmony.Patch(AccessTools.Method(typeof(PlayerBody), nameof(PlayerBody.SetSkin)),
                postfix: new HarmonyMethod(typeof(HeadModule), nameof(ApplyHead)));
        }

        private static void TagHeadBundle(MongoID templateId, ref ResourceKey __result)
        {
            if (templateId.ToString() != HeadId || __result == null)
                return;
            var key = new ResourceKey { path = __result.path, rcid = __result.rcid };
            HeadKeys.Add(key, new object());
            __result = key;
        }

        private static void ApplyHead(PlayerBody __instance, KeyValuePair<EBodyModelPart, ResourceKey> part)
        {
            if (part.Key != EBodyModelPart.Head || part.Value == null ||
                !HeadKeys.TryGetValue(part.Value, out _) || !__instance.BodySkins.TryGetValue(part.Key, out var skin))
                return;

            foreach (var lod in skin._lods)
            {
                var renderer = lod.SkinnedMeshRenderer;
                if (renderer.bones.Length != headMesh.bindposes.Length)
                    continue;
                if (lod is HeadSkin head)
                    head.Data = new HeadSkinData { Base = headMesh, FaceCover = headMesh };
                renderer.sharedMesh = headMesh;
                renderer.sharedMaterials = new[] { headMaterial };
                // The imported head is within the donor renderer's pre-existing culling bounds.
            }
        }
    }
}
