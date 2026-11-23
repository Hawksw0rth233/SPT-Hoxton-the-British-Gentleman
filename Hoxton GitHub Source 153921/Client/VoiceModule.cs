using System.Linq;
using BepInEx;
using EFT;
using HarmonyLib;

namespace Hawks.Hoxton
{
    internal static class VoiceModule
    {
        internal const string VoiceName = "Hoxton";
        private const string VoiceBundle = "assets/content/audio/phrases/hoxton_voice.bundle";
        private const EPhraseTrigger SelectionPreview = (EPhraseTrigger)500;

        internal static void Initialize(BaseUnityPlugin plugin, Harmony harmony)
        {
            InGameBundles._phrasesPaths[VoiceName] = VoiceBundle;

            // Both the stock menu and WTT request OnMutter. Point that request
            // at the specific audition line without changing the in-raid bank.
            var takeVoice = AccessTools.Method(typeof(PlayerVoiceLoader), nameof(PlayerVoiceLoader.TakeVoice));
            harmony.Patch(takeVoice, prefix: new HarmonyMethod(typeof(VoiceModule), nameof(SelectPreviewBank)));

            var events = plugin.gameObject.AddComponent<VoiceEvents>();
            events.Install(plugin.Config, harmony);
        }

        private static void SelectPreviewBank(string voiceName, ref EPhraseTrigger trigger)
        {
            if (voiceName == VoiceName && trigger == EPhraseTrigger.OnMutter)
                trigger = SelectionPreview;
        }

        internal static TagBank Play(BaseSpeaker speaker, string group, EPhraseTrigger signal,
            ETagStatus tags, string urgency)
        {
            var bank = speaker.PhrasesBanks.Values.FirstOrDefault(candidate => candidate.name == group);
            if (bank == null) return null;
            if (urgency == "urgent") tags = (tags & ~(ETagStatus.Unaware | ETagStatus.Aware)) | ETagStatus.Combat;
            float before = speaker.ReleaseTime;
            speaker.PlayExternal(bank, signal, tags, null);
            return speaker.ReleaseTime != before ? bank : null;
        }
    }
}
