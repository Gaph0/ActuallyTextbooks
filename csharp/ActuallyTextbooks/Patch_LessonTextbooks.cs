using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ActuallyTextbooks
{
    /// <summary>
    /// The mod's single gameplay patch.
    /// <para>
    /// Hooking <see cref="SkillRecord.LearnRateFactor"/> rather than the
    /// <c>LearningRateFactor</c> pawn stat is a deliberate choice: during a lesson both
    /// the skill XP and the learning-need fill rate multiply
    /// <see cref="LearningUtility.LearningRateFactor"/>, so raising that stat shortens
    /// the session and cancels out of XP per day (which is exactly what vanilla's
    /// blackboard bonus does). <c>LearnRateFactor</c> is the multiplier applied inside
    /// <see cref="SkillRecord.Learn"/> to the XP itself, and it is also the one place
    /// where vanilla's ×0.2 saturation penalty is applied — so a single postfix raises
    /// both the lesson rate and the daily budget.
    /// </para>
    /// <para>
    /// Only public API is touched (<see cref="SkillRecord.Pawn"/>,
    /// <see cref="SkillRecord.def"/>, <see cref="SkillRecord.xpSinceMidnight"/>,
    /// <see cref="SkillRecord.MaxFullRateXpPerDay"/>,
    /// <see cref="SkillRecord.SaturatedLearningFactor"/>): no reflection, no
    /// transpiler. The target is a sizeable method, so JIT inlining cannot silently
    /// defeat the patch.
    /// </para>
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.LearnRateFactor))]
    public static class Patch_LessonTextbooks
    {
        private static void Postfix(SkillRecord __instance, bool direct, ref float __result)
        {
            // direct=true callers (hediffs, events, dev tools) intentionally bypass
            // learn rate factors, so textbook bonuses must not leak into them either.
            // fastLearning must also be excluded: that branch returns 200f *without*
            // applying the saturation factor, so dividing by 0.2 there would be 1000×.
            if (direct || DebugSettings.fastLearning)
            {
                return;
            }
            EducationSettings settings = EducationMod.Settings;
            if (settings == null || !settings.Enabled || !ModsConfig.BiotechActive)
            {
                return;
            }

            Pawn pawn = __instance.Pawn;
            if (pawn == null)
            {
                return;
            }
            // Cheapest gate first: this postfix runs for every XP gain of every pawn on
            // the map (work, reading, recreation, skill decay included). Scheduled
            // classes of the Progression: Education mod grant skill XP through the same
            // SkillRecord.Learn choke point, so their attendance jobs count too; their
            // job target A is a desk by construction, hence the relaxed def check.
            bool vanillaLesson = pawn.CurJobDef == JobDefOf.Lessontaking;
            if (!vanillaLesson && !ProgressionEducationCompat.IsClassAttendanceJob(pawn.CurJobDef))
            {
                return;
            }
            Job job = pawn.CurJob;
            Thing desk = job == null ? null : job.GetTarget(TargetIndex.A).Thing;
            if (desk == null || (vanillaLesson && desk.def != ThingDefOf.SchoolDesk))
            {
                return;
            }
            if (!TextbookBonus.ForLesson(desk.GetRoom(), __instance.def, settings, out float rateBonus, out int budgetBonus))
            {
                return;
            }

            // Raise the daily budget. Vanilla already multiplied __result by
            // SaturatedLearningFactor (0.2) when xpSinceMidnight passed 4000, so
            // undoing exactly that factor *is* what extends the cap. The result stays
            // bit-identical to vanilla whenever no bonus applies.
            if (budgetBonus > 0)
            {
                float spent = __instance.xpSinceMidnight;
                bool wasSaturated = spent > SkillRecord.MaxFullRateXpPerDay;
                bool stillSaturated = spent > SkillRecord.MaxFullRateXpPerDay + budgetBonus;
                if (wasSaturated && !stillSaturated)
                {
                    __result /= SkillRecord.SaturatedLearningFactor;
                }
            }

            if (rateBonus > 0f)
            {
                __result *= 1f + rateBonus;
            }

            if (settings.DebugLogging)
            {
                LessonDebugLogger.ReportRate(pawn, __instance, rateBonus, budgetBonus, __result);
            }
        }
    }
}
