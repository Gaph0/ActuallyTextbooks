using RimWorld;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// Hot-path query used by the lesson patch, separated from the scanner so the
    /// matching policy lives in exactly one place. Allocation-free and LINQ-free by
    /// design: the caller has already established that this pawn is mid-lesson at a
    /// school desk, so all that remains is a dictionary lookup.
    /// </summary>
    public static class TextbookBonus
    {
        /// <summary>
        /// Bonus in force for a lesson happening in a classroom.
        /// </summary>
        /// <param name="room">The room the school desk stands in.</param>
        /// <param name="skill">Skill being taught right now; null means no match is possible.</param>
        /// <param name="rateBonus">Multiplicative lesson XP bonus (0.3 ⇒ +30%).</param>
        /// <param name="budgetBonus">Extra XP per skill per day beyond vanilla's 4000.</param>
        public static bool ForLesson(Room room, SkillDef skill, EducationSettings settings,
            out float rateBonus, out int budgetBonus)
        {
            rateBonus = 0f;
            budgetBonus = 0;
            if (room == null || settings == null)
            {
                return false;
            }

            RoomTextbooks books = RoomTextbookCache.CacheFor(room.Map)?.TryGet(room);
            if (books == null || books.IsEmpty)
            {
                return false;
            }

            if (settings.RequireMatchingSubject)
            {
                // The room's value is a coverage map here, so an off-subject textbook —
                // which is most trader textbooks — contributes nothing to this lesson.
                if (skill == null || books.PerSubject == null)
                {
                    return false;
                }
                if (!books.PerSubject.TryGetValue(skill, out RoomTextbooks.LessonBonus matched))
                {
                    return false;
                }
                rateBonus = matched.Rate;
                budgetBonus = matched.Budget;
            }
            else
            {
                rateBonus = books.Rate;
                budgetBonus = books.Budget;
            }

            return rateBonus > 0f || budgetBonus > 0;
        }
    }
}
