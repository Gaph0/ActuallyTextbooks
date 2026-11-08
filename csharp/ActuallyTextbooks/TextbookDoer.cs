using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// Doer substituted for <see cref="BookOutcomeDoerGainSkillExp"/> on the vanilla
    /// <c>TextBook</c> def. Inheriting from the vanilla doer means the book keeps its
    /// native behaviour verbatim — random subject generation, reading XP, the
    /// quality-based reading level cap, benefits text, hyperlinks — while exposing the
    /// printed subjects and quality to the classroom code.
    /// </summary>
    public class TextbookDoer : BookOutcomeDoerGainSkillExp
    {
        /// <summary>
        /// Skills printed on this copy (1–2, chosen by the vanilla generator). With
        /// subject matching on these are the only lessons the textbook can boost.
        /// The dictionary is saved with the book, so the subjects survive save/load
        /// and never change for a given copy.
        /// </summary>
        public IEnumerable<SkillDef> Subjects => Values.Keys;

        /// <summary>Quality of this copy; Awful if the book lacks a quality comp.</summary>
        public QualityCategory QualitySafe => TryGetQuality(out QualityCategory quality) ? quality : QualityCategory.Awful;

        /// <summary>
        /// Reads the copy's quality without relying on the base accessor, which
        /// dereferences a parent comp that can be missing on odd/modded book defs.
        /// </summary>
        public bool TryGetQuality(out QualityCategory quality)
        {
            quality = QualityCategory.Awful;
            ThingWithComps book = Parent as ThingWithComps;
            if (book == null)
            {
                return false;
            }
            CompQuality compQuality = book.GetComp<CompQuality>();
            if (compQuality == null)
            {
                return false;
            }
            quality = compQuality.Quality;
            return true;
        }

        /// <summary>Rate bonus this single copy is worth before library aggregation.</summary>
        public float SingleRateBonus => EducationSettings.RateBonusFor(QualitySafe);

        /// <summary>Daily budget this single copy is worth before library aggregation.</summary>
        public int SingleDailyBudgetBonus => EducationSettings.DailyBudgetBonusFor(QualitySafe);

        /// <summary>
        /// Book-card line describing what the copy is worth to a classroom, so the
        /// numbers travel with the item and no extra window is needed. This is the
        /// per-copy potential: what a desk actually gets depends on the whole library
        /// (see <see cref="Comp_SchoolDeskTextbooks"/>).
        /// </summary>
        public override string GetBenefitsString(Pawn reader = null)
        {
            string baseText = base.GetBenefitsString(reader);
            string classroomLine = ClassroomBenefitsString();
            if (classroomLine == null)
            {
                return baseText;
            }
            if (baseText == null)
            {
                return classroomLine;
            }
            return baseText + classroomLine;
        }

        private string ClassroomBenefitsString()
        {
            EducationSettings settings = EducationMod.Settings;
            if (settings == null || !settings.Enabled)
            {
                return null;
            }
            float rate = SingleRateBonus;
            int budget = SingleDailyBudgetBonus;
            if (rate <= 0f && budget <= 0)
            {
                return "ActTx_Textbook_NoValue".Translate();
            }
            return "ActTx_Textbook_ClassroomBenefit".Translate(
                rate.ToStringPercent("0").Named("RATE"),
                budget.Named("BUDGET"));
        }
    }

    /// <summary>
    /// Declares the textbook doer on the vanilla <c>TextBook</c> def, replacing
    /// <see cref="BookOutcomeProperties_GainSkillExp"/>. Doers are instantiated by
    /// <c>CompReadable.InitializeDoers</c> through
    /// <c>Activator.CreateInstance(props.DoerClass)</c>, so overriding
    /// <see cref="DoerClass"/> is the only wiring required.
    /// </summary>
    public class TextbookProperties : BookOutcomeProperties_GainSkillExp
    {
        public override Type DoerClass => typeof(TextbookDoer);
    }
}
