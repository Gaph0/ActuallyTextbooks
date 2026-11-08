using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// What the textbooks in one classroom are worth to its students, after
    /// aggregation.
    /// <para>
    /// Two shapes, chosen by <see cref="EducationSettings.RequireMatchingSubject"/>:
    /// with matching off, <see cref="Rate"/>/<see cref="Budget"/> hold the whole
    /// library's value and every student in the room gets it regardless of subject;
    /// with matching on (the default) only <see cref="PerSubject"/> is populated, so a
    /// textbook helps the skills printed on it and nothing else.
    /// </para>
    /// </summary>
    public sealed class RoomTextbooks
    {
        /// <summary>Lesson XP rate bonus as a fraction (0.3 ⇒ +30%). Matching off only.</summary>
        public float Rate;

        /// <summary>Extra XP per skill per day, on top of vanilla's 4000. Matching off only.</summary>
        public int Budget;

        /// <summary>Best quality among the books that counted, for UI reporting.</summary>
        public QualityCategory BestQuality = QualityCategory.Awful;

        /// <summary>How many textbooks were counted after aggregation.</summary>
        public int CountedBooks;

        /// <summary>skill → bonus; matching on only.</summary>
        public Dictionary<SkillDef, LessonBonus> PerSubject;

        public bool IsEmpty => CountedBooks <= 0;

        public int SubjectCount => PerSubject == null ? 0 : PerSubject.Count;

        public void Reset()
        {
            Rate = 0f;
            Budget = 0;
            BestQuality = QualityCategory.Awful;
            CountedBooks = 0;
            PerSubject?.Clear();
        }

        public readonly struct LessonBonus
        {
            public readonly float Rate;

            public readonly int Budget;

            public LessonBonus(float rate, int budget)
            {
                Rate = rate;
                Budget = budget;
            }
        }
    }
}
