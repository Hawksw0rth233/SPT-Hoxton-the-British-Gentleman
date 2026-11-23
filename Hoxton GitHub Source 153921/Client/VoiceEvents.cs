using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace Hawks.Hoxton
{
    internal sealed class VoiceEvents : MonoBehaviour
    {
        private static VoiceEvents instance;
        private static readonly AccessTools.FieldRef<Player, IPlayer> LastAggressor =
            AccessTools.FieldRefAccess<Player, IPlayer>("LastAggressor");
        private readonly Dictionary<string, float> cooldowns = new Dictionary<string, float>();
        private readonly HashSet<string> seenEnemies = new HashSet<string>();
        private readonly RaycastHit[] contactHits = new RaycastHit[32];
        private HashSet<string> ownedKeycards = new HashSet<string>();
        private Player previousPlayer;
        private string candidate;
        private int contactFrames;
        private bool heavy;
        private float nextPoll;
        private ConfigEntry<KeyboardShortcut> upstairs;
        private ConfigEntry<KeyboardShortcut> downstairs;
        private ConfigEntry<KeyboardShortcut> goDownstairs;

        internal void Install(ConfigFile config, Harmony harmony)
        {
            instance = this;
            upstairs = config.Bind("Voice", "Upstairs", KeyboardShortcut.Empty);
            downstairs = config.Bind("Voice", "Downstairs", KeyboardShortcut.Empty);
            goDownstairs = config.Bind("Voice", "Go downstairs", KeyboardShortcut.Empty);

            Patch(harmony, typeof(Player), nameof(Player.OnHealthEffectAdded), postfix: nameof(HealthEffect));
            Patch(harmony, typeof(Player), nameof(Player.OnDead), postfix: nameof(PlayerDied));
            Patch(harmony, typeof(Player), nameof(Player.OnPhraseTold), postfix: nameof(PhrasePlayed));
            Patch(harmony, typeof(Player), nameof(Player.ExecuteInteraction), new[] { typeof(WorldInteractiveObject), typeof(InteractionResult) },
                nameof(BeforeDoor), nameof(AfterDoor));
            Patch(harmony, typeof(FlyingBulletSoundPlayer), nameof(FlyingBulletSoundPlayer.StartVignetteEffect), postfix: nameof(Suppression));
            Patch(harmony, typeof(GameWorld), nameof(GameWorld.ThrowItem),
                new[] { typeof(Item), typeof(IPlayer), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool), typeof(float) },
                postfix: nameof(ItemDropped));
        }

        private static void Patch(Harmony harmony, Type type, string method, Type[] parameters = null,
            string prefix = null, string postfix = null)
        {
            var original = AccessTools.Method(type, method, parameters) ?? throw new MissingMethodException(type.FullName, method);
            harmony.Patch(original,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(VoiceEvents), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(VoiceEvents), postfix));
        }

        private static Player Local =>
            Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;

        private static bool UsesHoxton(Player player) =>
            player != null && player.IsYourPlayer && player.HealthController != null &&
            player.HealthController.IsAlive && player.Speaker?.PlayerVoice == VoiceModule.VoiceName;

        private static bool SameTeam(Player player, Player other) =>
            other != null && other != player && !string.IsNullOrEmpty(player.GroupId) && other.GroupId == player.GroupId;

        private void Update()
        {
            var local = Local;
            if (!UsesHoxton(local)) return;
            if (local != previousPlayer)
            {
                previousPlayer = local;
                ownedKeycards = Keycards(local);
                seenEnemies.Clear();
                cooldowns.Clear();
                heavy = IsHeavy(local);
                candidate = null;
                contactFrames = 0;
            }
            if (upstairs.Value.IsDown()) Play(local, "upstairs", EPhraseTrigger.Look, "normal", 2f);
            if (downstairs.Value.IsDown()) Play(local, "downstairs", EPhraseTrigger.Look, "normal", 2f);
            if (goDownstairs.Value.IsDown()) Play(local, "go_downstairs", EPhraseTrigger.GoForward, "normal", 2f);

            if (Time.time < nextPoll) return;
            nextPoll = Time.time + .30f;
            var keys = Keycards(local);
            if (keys.Except(ownedKeycards).Any())
                Play(local, "keycard", EPhraseTrigger.LootKey, "normal", 5f);
            ownedKeycards = keys;

            bool nowHeavy = IsHeavy(local);
            if (nowHeavy && !heavy)
                StartCoroutine(AfterSpeech(local, "heavy_help", EPhraseTrigger.NeedHelp, "urgent", 22f, 0));
            heavy = nowHeavy;

            PollForContact(local);
        }

        private static bool IsHeavy(Player player) =>
            player.HealthStatus == ETagStatus.BadlyInjured || player.HealthStatus == ETagStatus.Dying;

        private static HashSet<string> Keycards(Player player) =>
            new HashSet<string>(player.Equipment.GetAllItems().OfType<Keycard>().Select(item => item.Id.ToString()));

        private void PollForContact(Player player)
        {
            if (!player.FirstPersonPointOfView || player.HandsController == null || !(Camera.main is Camera camera))
            {
                candidate = null;
                return;
            }
            var origin = camera.transform.position;
            var forward = camera.transform.forward;
            int count = Physics.RaycastNonAlloc(origin, forward, contactHits, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            Player enemy = null;
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = contactHits[i];
                if (!hit.collider || hit.distance >= nearest) continue;
                var body = hit.collider.GetComponentInParent<BodyPartCollider>();
                var target = body?.Player as Player;
                if (target == player || (target == null && hit.collider.isTrigger)) continue;
                nearest = hit.distance;
                enemy = target;
            }
            if (enemy == null || SameTeam(player, enemy) || !enemy.HealthController.IsAlive)
            {
                candidate = null;
                return;
            }
            if (candidate != enemy.ProfileId)
            {
                candidate = enemy.ProfileId;
                contactFrames = 1;
                return;
            }
            if (++contactFrames < 2 || seenEnemies.Contains(candidate)) return;

            if (Play(player, "OnFirstContact", EPhraseTrigger.OnFirstContact, "urgent", 12f))
                seenEnemies.Add(candidate);
        }

        private bool Play(Player player, string group, EPhraseTrigger signal, string urgency, float cooldown)
        {
            if (!UsesHoxton(player)) return false;
            if (cooldowns.TryGetValue(group, out float until) && until > Time.time) return false;
            var tags = player.HealthStatus | (player.Awareness > Time.time ? ETagStatus.Combat : ETagStatus.Unaware);
            if (VoiceModule.Play(player.Speaker, group, signal, tags, urgency) == null) return false;
            cooldowns[group] = Time.time + cooldown;
            return true;
        }

        private IEnumerator AfterSpeech(Player player, string group, EPhraseTrigger signal, string urgency, float cooldown, float delay)
        {
            if (delay > 0) yield return new WaitForSeconds(delay);
            float deadline = Time.time + 2f;
            while (UsesHoxton(player) && player.Speaker.Busy && Time.time < deadline)
                yield return null;
            Play(player, group, signal, urgency, cooldown);
        }

        private static void HealthEffect(Player __instance, IHealthEffect effect)
        {
            if (effect is IFlash && UsesHoxton(__instance))
                instance.Play(__instance, "flash", EPhraseTrigger.OnBeingHurt, "urgent", 4f);
        }

        private static void Suppression(Player ____player)
        {
            if (UsesHoxton(____player))
                instance.Play(____player, "suppressed", EPhraseTrigger.Hit, "urgent", 14f);
        }

        private static void PlayerDied(Player __instance)
        {
            var local = Local;
            if (!UsesHoxton(local) || __instance == local) return;
            if (SameTeam(local, __instance))
            {
                instance.StartCoroutine(instance.AfterSpeech(local, "OnFriendlyDown", EPhraseTrigger.OnFriendlyDown, "urgent", 12f, 0));
            }
            else if (LastAggressor(__instance)?.ProfileId == local.ProfileId && __instance.Profile.Info.Settings.Role == WildSpawnType.marksman)
            {
                instance.StartCoroutine(instance.AfterSpeech(local, "sniper_down", EPhraseTrigger.EnemyDown, "normal", 5f, 0));
            }
        }

        private static void PhrasePlayed(Player __instance, EPhraseTrigger @event, TaggedClip clip)
        {
            var local = Local;
            if (!UsesHoxton(local) || !SameTeam(local, __instance) ||
                Vector3.SqrMagnitude(local.Position - __instance.Position) > 30f * 30f) return;
            if (@event == EPhraseTrigger.NeedHelp)
                instance.StartCoroutine(instance.AfterSpeech(local, "help_reply", EPhraseTrigger.Going, "urgent", 10f, clip.Length));
            else if (@event == EPhraseTrigger.FollowMe)
                instance.StartCoroutine(instance.AfterSpeech(local, "follow_reply", EPhraseTrigger.Going, "normal", 10f, clip.Length));
        }

        private static void ItemDropped(Item item, IPlayer player, LootItem __result)
        {
            if (__result != null && item is MedKit && player is Player local && UsesHoxton(local))
                instance.Play(local, "med_dropped", EPhraseTrigger.LootGeneric, "normal", 5f);
        }

        private static void BeforeDoor(Player __instance, WorldInteractiveObject door, out bool __state)
        {
            __state = UsesHoxton(__instance) && door is Door && door.DoorState != EDoorState.Open;
        }

        private static void AfterDoor(Player __instance, WorldInteractiveObject door, bool __state)
        {
            if (__state) instance.StartCoroutine(instance.WaitForDoor(__instance, door));
        }

        private IEnumerator WaitForDoor(Player player, WorldInteractiveObject door)
        {
            float deadline = Time.time + 2.5f;
            while (door && UsesHoxton(player) && Time.time < deadline)
            {
                if (door.DoorState == EDoorState.Open)
                {
                    Play(player, "door_opened", EPhraseTrigger.OpenDoor, "normal", 4f);
                    yield break;
                }
                yield return null;
            }
        }
    }
}
