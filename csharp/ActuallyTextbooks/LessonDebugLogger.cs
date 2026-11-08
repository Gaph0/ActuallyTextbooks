using System.Collections;
using RimWorld;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// Opt-in instrumentation, gated by <see cref="EducationSettings.DebugLogging"/>,
    /// used to validate the budget arithmetic in PLAN.md §1.5 against a live game
    /// rather than trusting the model.
    /// <para>
    /// Throttled to one line per pawn-skill pair per second: a lesson calls into
    /// <see cref="SkillRecord.LearnRateFactor"/> every tick, so unthrottled logging
    /// would both flood the log and distort the timing it is meant to measure.
    /// </para>
    /// </summary>
    public static class LessonDebugLogger
    {
        private const int LogIntervalTicks = 60;

        /// <summary>Keys are <c>thingIDNumber * 64 + skillDef.shortHash</c>; values are the last tick logged.</summary>
        private static readonly Hashtable LastLogged = new Hashtable();

        /// <summary>
        /// Reports the effective learn rate for one XP gain during a lesson, with the
        /// day's progress against both the vanilla cap and our raised budget.
        /// </summary>
        public static void ReportRate(Pawn pawn, SkillRecord record, float rateBonus, int budgetBonus, float effectiveRate)
        {
            if (record == null)
            {
                return;
            }
            // SkillDef index is stable for the session, so this key needs no allocation
            // beyond boxing the int, and one entry per learner-skill pair is bounded.
            int key = pawn.thingIDNumber * 1024 + record.def.index;
            int now = Find.TickManager.TicksGame;
            object previous = LastLogged[key];
            if (previous is int lastTick && now - lastTick < LogIntervalTicks)
            {
                return;
            }
            LastLogged[key] = now;

            float spent = record.xpSinceMidnight;
            int vanillaCap = SkillRecord.MaxFullRateXpPerDay;
            int raisedCap = vanillaCap + budgetBonus;
            string saturation = spent > raisedCap ? "over-cap (x0.2)" :
                (spent > vanillaCap ? "in raised budget" : "full rate");
            Log.Message($"[ActTx] {pawn.LabelShort} {record.def.defName}: rate={effectiveRate:F3} "
                + $"(textbook +{rateBonus:P0}, budget +{budgetBonus}) xp/day={spent:F0}/{raisedCap} [{saturation}] lvl={record.Level}");
        }

        /// <summary>Releases throttling state when a map is discarded.</summary>
        public static void Clear()
        {
            LastLogged.Clear();
        }
    }
}
