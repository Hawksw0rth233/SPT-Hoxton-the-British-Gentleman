using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Customization;
using EFT.Visual;
using HarmonyLib;
using UnityEngine;

namespace Hawks.Hoxton
{
    internal static class ClothingModule
    {
        private static readonly Dictionary<string, string> AssetById = new Dictionary<string, string>
        {
            ["686a04000000000000000201"] = "upper",
            ["686a04000000000000000202"] = "hands",
            ["686a04000000000000000203"] = "lower",
        };
        private static readonly Dictionary<string, SkinnedMeshRenderer> ModelById =
            new Dictionary<string, SkinnedMeshRenderer>();
        private static readonly ConditionalWeakTable<ResourceKey, SkinnedMeshRenderer> TaggedKeys =
            new ConditionalWeakTable<ResourceKey, SkinnedMeshRenderer>();
        private static BepInEx.Logging.ManualLogSource log;
        private static readonly Dictionary<string, ClothingIcon> CustomIcons = new Dictionary<string, ClothingIcon>();

        internal static void Initialize(Harmony harmony, string directory,
            BepInEx.Logging.ManualLogSource logger)
        {
            log = logger;
            var path = Path.Combine(directory, "hawks.chains.clothing.bundle");
            var bundle = AssetBundle.LoadFromFile(path);
            if (!bundle) throw new InvalidDataException("Hoxton clothing asset bundle failed to load.");
            foreach (var entry in AssetById)
            {
                var prefab = bundle.LoadAsset<GameObject>($"assets/hawkschains/{entry.Value}.prefab");
                var renderer = prefab ? prefab.GetComponent<SkinnedMeshRenderer>() : null;
                if (!renderer || !renderer.sharedMesh)
                    throw new InvalidDataException($"Hoxton {entry.Value} mesh failed to load.");
                ModelById.Add(entry.Key, renderer);
            }

            harmony.Patch(AccessTools.Method(typeof(CustomizationSolver), nameof(CustomizationSolver.GetBundle)),
                postfix: new HarmonyMethod(typeof(ClothingModule), nameof(TagResource)));
            harmony.Patch(AccessTools.Method(typeof(PlayerBody), nameof(PlayerBody.SetSkin)),
                postfix: new HarmonyMethod(typeof(ClothingModule), nameof(ApplyClothing)));
            harmony.Patch(AccessTools.Method(typeof(ClothingIconCreator), nameof(ClothingIconCreator.GetIcon)),
                prefix: new HarmonyMethod(typeof(ClothingModule), nameof(GetClothingIcon)));
            harmony.Patch(AccessTools.Method(typeof(ClothingIconCreator), nameof(ClothingIconCreator.RenderModel)),
                prefix: new HarmonyMethod(typeof(ClothingModule), nameof(RenderClothingIcon)));
        }

        private static bool GetClothingIcon(ClothingIconCreator __instance, CustomizationClothing clothing,
            IntVec2 textureSize, ref ClothingIcon __result)
        {
            string id = clothing.Id.ToString();
            if (!ModelById.ContainsKey(id)) return true;

            if (!CustomIcons.TryGetValue(id, out var icon))
            {
                icon = new ClothingIcon(IconsHash.GetClothingHash(clothing) ^ 0x48304332);
                CustomIcons.Add(id, icon);
                CreateIcon(__instance, clothing, textureSize, id, icon);
            }
            __result = icon;
            return false;
        }

        private static async void CreateIcon(ClothingIconCreator creator, CustomizationClothing clothing,
            IntVec2 textureSize, string id, ClothingIcon icon)
        {
            try
            {
                await creator.FillIconWithNewSpriteAsync(icon, clothing, textureSize,
                    saveToFile: false, requireZeroMip: false);
                if (!icon.Sprite)
                {
                    CustomIcons.Remove(id);
                    log.LogWarning($"Hoxton icon renderer returned no sprite for {id}.");
                }
            }
            catch (Exception exception)
            {
                CustomIcons.Remove(id);
                log.LogError($"Hoxton icon {id}: {exception}");
            }
        }

        private static bool RenderClothingIcon(CustomizationClothing clothing,
            IconCreatorBase<CustomizationClothing, ClothingIcon>.SpriteFactory spriteFactory,
            ref Task<IconCreatorBase<CustomizationClothing, ClothingIcon>.RenderModelResult> __result)
        {
            if (!ModelById.TryGetValue(clothing.Id.ToString(), out var artwork)) return true;
            __result = RenderImportedClothing(artwork, spriteFactory);
            return false;
        }

        private static async Task<IconCreatorBase<CustomizationClothing, ClothingIcon>.RenderModelResult>
            RenderImportedClothing(SkinnedMeshRenderer artwork,
                IconCreatorBase<CustomizationClothing, ClothingIcon>.SpriteFactory spriteFactory)
        {
            var model = new GameObject("Hoxton clothing icon") { layer = LayersMaskController.WeaponPreview };
            try
            {
                model.AddComponent<MeshFilter>().sharedMesh = artwork.sharedMesh;
                model.AddComponent<MeshRenderer>().sharedMaterials = artwork.sharedMaterials;
                var sprite = await spriteFactory(model, null);
                return new IconCreatorBase<CustomizationClothing, ClothingIcon>.RenderModelResult
                {
                    sprite = sprite,
                    zeroMipWasLoaded = true
                };
            }
            finally
            {
                UnityEngine.Object.Destroy(model);
            }
        }

        private static void TagResource(MongoID templateId, ref ResourceKey __result)
        {
            if (__result == null || !ModelById.TryGetValue(templateId.ToString(), out var skin))
                return;
            var tagged = new ResourceKey { path = __result.path, rcid = __result.rcid };
            TaggedKeys.Add(tagged, skin);
            __result = tagged;
        }

        private static void ApplyClothing(PlayerBody __instance, KeyValuePair<EBodyModelPart, ResourceKey> part)
        {
            if (part.Value == null || !TaggedKeys.TryGetValue(part.Value, out var replacement) ||
                !__instance.BodySkins.TryGetValue(part.Key, out var original))
                return;
            var mesh = replacement.sharedMesh;
            var materials = replacement.sharedMaterials;
            foreach (var lod in original._lods)
            {
                var renderer = lod.SkinnedMeshRenderer;
                if (!renderer || renderer.bones.Length != mesh.bindposes.Length)
                {
                    log.LogWarning($"Hoxton clothing could not apply {part.Key} to an incompatible native renderer.");
                    continue;
                }
                renderer.sharedMesh = mesh;
                renderer.sharedMaterials = materials;
                var bounds = renderer.localBounds;
                bounds.Encapsulate(mesh.bounds.min);
                bounds.Encapsulate(mesh.bounds.max);
                renderer.localBounds = bounds;
            }
        }
    }
}
