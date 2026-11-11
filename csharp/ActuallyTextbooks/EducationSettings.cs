using RimWorld;
using UnityEngine;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// All tunables. This is the mod's only persistent state — the textbook cache is
    /// derived from the map and rebuilt on demand.
    /// </summary>
    public sealed class EducationSettings : ModSettings
    {
        /// <summary>Master switch. Off ⇒ the single Harmony postfix returns immediately.</summary>
        public bool Enabled = true;

        /// <summary>
        /// Default on: a textbook only boosts lessons in the skills printed on it.
        /// Vanilla textbooks carry 1–2 random subjects out of ~12, so this makes the
        /// classroom a library-collection goal — keep acquiring books until the
        /// subjects your teacher actually teaches are on the shelf. Off means every
        /// textbook boosts every skill, reading quality as a generic "well supplied
        /// classroom" signal.
        /// </summary>
        public bool RequireMatchingSubject = true;

        /// <summary>
        /// Count textbooks lying unforbidden on the classroom floor, not only those
        /// filed in a bookcase. Bookcase books always count.
        /// </summary>
        public bool CountLooseTextbooks = true;

        /// <summary>How many of the room's textbooks contribute, best quality first.</summary>
        public int MaxBooksCounted = 3;

        /// <summary>Ceiling on the summed lesson XP rate bonus (fraction; 0.6 = +60%).</summary>
        public float MaxRateBonus = 0.6f;

        /// <summary>
        /// Upper bound shared by the <see cref="MaxRateBonus"/> slider and its clamp, so
        /// the range the UI offers and the value the scanner applies cannot disagree.
        /// </summary>
        public const float MaxRateBonusCeiling = 3f;

        /// <summary>Upper bound shared by the <see cref="MaxBooksCounted"/> slider and its clamp.</summary>
        public const int MaxBooksCeiling = 10;

        /// <summary>Multiplicative weight of each successive counted book (diminishing returns).</summary>
        public float BookDiminish = 0.5f;

        /// <summary>Log lesson XP, saturation state and applied bonuses per student.</summary>
        public bool DebugLogging = false;

        /// <summary>
        /// Quality → lesson XP rate bonus, indexed by <see cref="QualityCategory"/>.
        /// Deliberately modest: vanilla lessons already land near the 4000 XP/day cap,
        /// so the daily budget below is the stronger lever (see PLAN.md §1.5).
        /// </summary>
        public static readonly float[] RateBonusByQuality = { 0f, 0.08f, 0.15f, 0.22f, 0.30f, 0.40f, 0.50f };

        /// <summary>
        /// Quality → extra XP per day a student may earn in that skill, added to
        /// vanilla's <see cref="SkillRecord.MaxFullRateXpPerDay"/> (4000).
        /// </summary>
        public static readonly int[] DailyBudgetBonusByQuality = { 0, 400, 800, 1200, 1800, 2400, 3200 };

        public static float RateBonusFor(QualityCategory quality) => RateBonusByQuality[IndexOf(quality)];

        public static int DailyBudgetBonusFor(QualityCategory quality) => DailyBudgetBonusByQuality[IndexOf(quality)];

        /// <summary>Weight of the nth counted book (0-based): 1, 0.5, 0.25, …</summary>
        public float WeightForIndex(int index)
        {
            if (index <= 0)
            {
                return 1f;
            }
            float diminish = Mathf.Clamp(BookDiminish, 0f, 1f);
            float weight = 1f;
            for (int i = 0; i < index; i++)
            {
                weight *= diminish;
            }
            return weight;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref RequireMatchingSubject, "requireMatchingSubject", true);
            Scribe_Values.Look(ref CountLooseTextbooks, "countLooseTextbooks", true);
            Scribe_Values.Look(ref MaxBooksCounted, "maxBooksCounted", 3);
            Scribe_Values.Look(ref MaxRateBonus, "maxRateBonus", 0.6f);
            Scribe_Values.Look(ref BookDiminish, "bookDiminish", 0.5f);
            Scribe_Values.Look(ref DebugLogging, "debugLogging", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // A hand-edited or older settings file must not carry a value outside
                // the range the sliders offer: the scanner applies these directly.
                MaxRateBonus = Mathf.Clamp(MaxRateBonus, 0f, MaxRateBonusCeiling);
                MaxBooksCounted = Mathf.Clamp(MaxBooksCounted, 1, MaxBooksCeiling);
                BookDiminish = Mathf.Clamp(BookDiminish, 0f, 1f);
            }
            base.ExposeData();
        }

        private static int IndexOf(QualityCategory quality) => Mathf.Clamp((int)quality, 0, RateBonusByQuality.Length - 1);

        public void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("ActTx_Settings_Enabled".Translate(), ref Enabled,
                "ActTx_Settings_EnabledTooltip".Translate());
            listing.GapLine();
            listing.CheckboxLabeled("ActTx_Settings_Matching".Translate(), ref RequireMatchingSubject,
                "ActTx_Settings_MatchingTooltip".Translate());
            listing.CheckboxLabeled("ActTx_Settings_LooseBooks".Translate(), ref CountLooseTextbooks,
                "ActTx_Settings_LooseBooksTooltip".Translate());
            listing.GapLine();

            int maxBooks = (int)listing.SliderLabeled("ActTx_Settings_MaxBooks".Translate(MaxBooksCounted),
                MaxBooksCounted, 1f, MaxBooksCeiling);
            MaxBooksCounted = Mathf.Clamp(maxBooks, 1, MaxBooksCeiling);
            BookDiminish = listing.SliderLabeled("ActTx_Settings_Diminish".Translate(BookDiminish.ToStringPercent("0")),
                BookDiminish, 0f, 1f);
            MaxRateBonus = Mathf.Clamp(listing.SliderLabeled(
                    "ActTx_Settings_MaxRate".Translate(MaxRateBonus.ToStringPercent("0")), MaxRateBonus, 0f, MaxRateBonusCeiling),
                0f, MaxRateBonusCeiling);
            listing.GapLine();

            listing.CheckboxLabeled("ActTx_Settings_DebugLog".Translate(), ref DebugLogging,
                "ActTx_Settings_DebugLogTooltip".Translate());
            if (listing.ButtonText("ActTx_Settings_ShowTable".Translate()))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(BuildEffectiveTable()));
            }
            if (listing.ButtonText("ActTx_Settings_Reset".Translate()))
            {
                Enabled = true;
                RequireMatchingSubject = true;
                CountLooseTextbooks = true;
                MaxBooksCounted = 3;
                MaxRateBonus = 0.6f;
                BookDiminish = 0.5f;
                DebugLogging = false;
            }
            listing.Gap();
            listing.Label("ActTx_Settings_CacheNote".Translate(RoomTextbookCache.RebuildInterval));

            listing.End();
        }

        /// <summary>Renders the per-quality numbers plus what a full library is worth.</summary>
        private string BuildEffectiveTable()
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.AppendLine("ActTx_Settings_TableTitle".Translate());
            for (int i = 0; i < RateBonusByQuality.Length; i++)
            {
                QualityCategory quality = (QualityCategory)i;
                text.AppendLine("  " + quality.GetLabel().CapitalizeFirst().PadRight(12)
                    + RateBonusByQuality[i].ToStringPercent("0").PadRight(8)
                    + "+" + DailyBudgetBonusByQuality[i]);
            }
            text.AppendLine();
            float rate = 0f;
            int budget = 0;
            int books = Mathf.Clamp(MaxBooksCounted, 1, MaxBooksCeiling);
            // Every counted book contributes the top tier's value at its own weight, so
            // there is no second loop bound: capping this at the number of quality tiers
            // would under-sum the total whenever MaxBooksCounted exceeds 7.
            for (int i = 0; i < books; i++)
            {
                rate += RateBonusByQuality[RateBonusByQuality.Length - 1] * WeightForIndex(i);
                budget += Mathf.RoundToInt(DailyBudgetBonusByQuality[DailyBudgetBonusByQuality.Length - 1] * WeightForIndex(i));
            }
            text.AppendLine("ActTx_Settings_TableBest".Translate(
                Mathf.Min(rate, MaxRateBonus).ToStringPercent("0"),
                (SkillRecord.MaxFullRateXpPerDay + budget), books));
            return text.ToString();
        }
    }
}
