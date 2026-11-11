using UnityEngine;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// Entry point. Owns the Harmony bootstrap and exposes the settings instance to
    /// the static helpers that need it on hot paths (a <see cref="ModSettings"/>
    /// object is reached through the mod instance, which is one more indirection than
    /// a lesson tick wants to pay).
    /// </summary>
    public sealed class EducationMod : Mod
    {
        public const string HarmonyId = "gapho.actuallytextbooks";

        public EducationMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<EducationSettings>();
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(HarmonyId);
            harmony.PatchAll();
            // LoadAllActiveMods loads every mod assembly (LoadModContent) before
            // CreateModClasses runs any Mod constructor, verified in 1.7, so the
            // optional bridge resolves the other mod's types right here.
            ProgressionEducationCompat.ApplyDialogPatches(harmony);
            // The proficiency training-rate boost rides on the same bridge.
            ProgressionEducationCompat.ApplyTrainingPatches(harmony);
            Log.Message("[Actually Textbooks] Harmony patches applied (" + HarmonyId + ").");
        }

        /// <summary>Null until the mod constructor runs (e.g. mod disabled mid-session).</summary>
        public static EducationSettings Settings { get; private set; }

        public override string SettingsCategory() => "Actually Textbooks";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoSettingsWindowContents(inRect);
            base.DoSettingsWindowContents(inRect);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            // Aggregation only takes effect on the next cache rebuild, so drop
            // the cached rooms rather than waiting up to 250 ticks per room.
            RoomTextbookCache.InvalidateAll();
        }
    }
}
