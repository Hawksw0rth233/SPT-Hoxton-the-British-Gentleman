using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BepInEx;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using EFT.Visual;
using HarmonyLib;
using UnityEngine;

namespace Hawks.Hoxton
{
    [BepInPlugin("team.aminus.hoxton.client", "Hoxton the British Gentleman", "0.3.1")]
    public sealed class Plugin : BaseUnityPlugin
    {
        const string MaskId = "686a04000000000000000001";
        static GameObject prefab;
        static readonly ConditionalWeakTable<PlayerBody, MaskVisual> worn = new ConditionalWeakTable<PlayerBody, MaskVisual>();
        [ThreadStatic] static HitContext hit;
        readonly struct HitContext
        {
            internal readonly PlayerBody Body;
            internal readonly Vector3 Point;
            internal readonly Vector3 Direction;
            internal HitContext(PlayerBody body, Vector3 point, Vector3 direction)
            {
                Body = body; Point = point; Direction = direction;
            }
        }

        void Awake()
        {
            var bundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "hawks.hoxton.mask.bundle"));
            if (!bundle) throw new InvalidDataException("Hoxton asset bundle could not be read.");
            prefab = bundle.LoadAsset<GameObject>("assets/hawkshoxton/hoxtonmask.prefab");
            if (!prefab) throw new InvalidDataException("Hoxton mesh prefab was not found.");
            var harmony = new Harmony(Info.Metadata.GUID);
            foreach (var factory in typeof(ObjectsFactory).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)
                .Where(x => x.Name == "CreateItem" || x.Name == "CreateItemAsync" ||
                            x.Name == "CreateCleanLootPrefab" || x.Name == "CreateCleanLootPrefabAsync" ||
                            x.Name == "CreateLootPrefab"))
            {
                if (!factory.GetParameters().Any(p => p.ParameterType == typeof(Item))) continue;
                if (factory.ReturnType == typeof(GameObject))
                    harmony.Patch(factory, postfix: new HarmonyMethod(typeof(Plugin), nameof(Created)));
                else if (factory.ReturnType == typeof(Task<GameObject>))
                    harmony.Patch(factory, postfix: new HarmonyMethod(typeof(Plugin), nameof(CreatedAsync)));
            }
            var slotView = AccessTools.Inner(typeof(PlayerBody), "SlotView");
            harmony.Patch(AccessTools.Method(slotView, "CreateItemAsync"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(EquippedAsync)));
            var restore = new HarmonyMethod(typeof(Plugin), nameof(RestoreHit));
            harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.SetShotStatus)),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(TraceShot)), finalizer: restore);
            harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.ProceedDamageThroughArmor)),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(TraceDamage)), finalizer: restore);
            harmony.Patch(AccessTools.Method(typeof(BodyPartCollider), nameof(BodyPartCollider.IsPenetrated)),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(TracePenetration)), finalizer: restore);
            harmony.Patch(AccessTools.Method(typeof(ArmorComponent), nameof(ArmorComponent.ShotMatches)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(FilterMaskHit)));
            Logger.LogInfo("Hoxton mask initialized; class 4 coverage uses the worn mask mesh.");
            InitializeModule("head", () => Hawks.HoxtonHead.HeadModule.Initialize(
                new Harmony(Info.Metadata.GUID + ".head"), Path.GetDirectoryName(Info.Location)));
            InitializeModule("voice", () => VoiceModule.Initialize(this, new Harmony(Info.Metadata.GUID + ".voice")));
            InitializeModule("clothing", () => ClothingModule.Initialize(
                new Harmony(Info.Metadata.GUID + ".clothing"), Path.GetDirectoryName(Info.Location), Logger));
        }

        private void InitializeModule(string name, Action initialize)
        {
            try
            {
                initialize();
                Logger.LogInfo($"Hoxton {name} initialized.");
            }
            catch (Exception exception)
            {
                Logger.LogError($"Hoxton {name} initialization failed: {exception}");
            }
        }

        static bool IsHoxton(Item item) => item != null && item.TemplateId == MaskId;
        static void Created(Item item, GameObject __result) => Configure(item, __result);
        static void CreatedAsync(Item item, ref Task<GameObject> __result) => __result = AfterCreation(__result, item);
        static async Task<GameObject> AfterCreation(Task<GameObject> original, Item item)
        {
            var model = await original;
            Configure(item, model);
            return model;
        }

        static MaskVisual Configure(Item item, GameObject model)
        {
            if (!model) return null;
            var view = model.GetComponent<MaskVisual>();
            if (!IsHoxton(item))
            {
                if (view) view.Restore();
                return null;
            }
            if (!view) view = model.AddComponent<MaskVisual>();
            return view.Attach(prefab) ? view : null;
        }

        static void EquippedAsync(object __instance, Item item, ref Task __result)
        {
            if (IsHoxton(item)) __result = BindWornMask(__result, __instance, item);
        }

        static async Task BindWornMask(Task original, object slot, Item item)
        {
            await original;
            var body = Traverse.Create(slot).Field<PlayerBody>("_playerBody").Value;
            var model = Traverse.Create(slot).Property<GameObject>("Model").Value;
            var active = Traverse.Create(slot).Property<Diz.Binding.BindableState<Item>>("ContainedItem").Value;
            if (!body || active == null || !ReferenceEquals(active.Value, item))
                return;
            var view = Configure(item, model);
            if (!view) return;
            worn.Remove(body);
            worn.Add(body, view);
        }

        static void TraceShot(Player __instance, Vector3 hitpoint, Vector3 shotDirection, out HitContext __state)
        {
            __state = hit;
            hit = new HitContext(__instance.PlayerBody, hitpoint, shotDirection);
        }
        static void TraceDamage(Player __instance, ref DamageInfo damageInfo, out HitContext __state)
        {
            __state = hit;
            hit = (damageInfo.DamageType & (EDamageType.Bullet | EDamageType.GrenadeFragment | EDamageType.Sniper)) != 0
                ? new HitContext(__instance.PlayerBody, damageInfo.HitPoint, damageInfo.Direction)
                : default;
        }
        static void TracePenetration(BodyPartCollider __instance, Shot shot, Vector3 hitPoint, out HitContext __state)
        {
            __state = hit;
            if (__instance.Player is Player player)
                hit = new HitContext(player.PlayerBody, hitPoint, shot.Direction);
        }
        static void RestoreHit(HitContext __state) => hit = __state;

        static void FilterMaskHit(ArmorComponent __instance, ref bool __result)
        {
            if (!__result || !hit.Body || !IsHoxton(__instance.Item)) return;
            __result = worn.TryGetValue(hit.Body, out var model) && model &&
                model.Intercepts(hit.Point, hit.Direction);
        }
    }

    internal sealed class MaskVisual : MonoBehaviour
    {
        GameObject mask;
        Dress dress;
        Renderer[] donor;
        Renderer[] renderers;
        Vector3[] vertices;
        int[] triangles;
        Bounds bounds;

        internal bool Attach(GameObject prefab)
        {
            if (!mask)
            {
                dress = GetComponentInChildren<Dress>(true);
                if (!dress) return false;
                donor = dress.Renderers;
                if (donor == null || donor.Length == 0)
                    donor = dress.GetComponentsInChildren<Renderer>(true);
                mask = Instantiate(prefab, dress.transform, false);
                mask.transform.localPosition = new Vector3(0f, .055f, .099f);
                mask.transform.localRotation = Quaternion.identity;
                renderers = mask.GetComponentsInChildren<Renderer>(true);
                if (donor.Length > 0 && donor[0])
                    foreach (var part in renderers) part.gameObject.layer = donor[0].gameObject.layer;
                var mesh = mask.GetComponent<MeshFilter>().sharedMesh;
                vertices = mesh.vertices; triangles = mesh.triangles; bounds = mesh.bounds;
            }
            dress.Renderers = renderers;
            foreach (var original in donor) if (original) original.forceRenderingOff = true;
            mask.SetActive(true);
            return true;
        }
        internal void Restore()
        {
            if (!mask) return;
            mask.SetActive(false);
            if (dress) dress.Renderers = donor;
            foreach (var original in donor) if (original) original.forceRenderingOff = false;
        }

        internal bool Intercepts(Vector3 hitPoint, Vector3 direction)
        {
            if (!mask || !mask.activeSelf || direction.sqrMagnitude < .000001f) return false;
            return MaskHitMesh.Intercepts(vertices, triangles, bounds, mask.transform.InverseTransformPoint(hitPoint),
                mask.transform.InverseTransformDirection(direction));

        }
    }
}




