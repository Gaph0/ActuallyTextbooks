using System.Text;
using RimWorld;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// Builds the human-readable classroom textbook readout for one room and one
    /// taught subject, as shown in the class dialogs of the Progression:
    /// Education mod.
    /// </summary>
    public static class TextbookRoomSummary
    {
        /// <summary>
        /// The readout, or null while the mod is off. With subject matching on,
        /// only what serves <paramref name="subject"/> is reported: the count and
        /// quality in the title cover just the counted textbooks printed with the
        /// taught skill, and the bonus line is that subject's. A room with no
        /// covering book reports just that fact. With matching off every book
        /// helps every lesson, so the room total is reported instead.
        /// </summary>
        public static string For(Room room, Map map, SkillDef subject)
        {
            EducationSettings settings = EducationMod.Settings;
            if (settings == null || !settings.Enabled || !ModsConfig.BiotechActive || room == null || map == null)
            {
                return null;
            }
            RoomTextbooks books = RoomTextbookCache.CacheFor(map)?.TryGet(room);
            if (books == null || books.IsEmpty)
            {
                return "ActTx_Inspect_NoTextbooks".Translate();
            }

            // With matching on the readout speaks for this class alone: the books
            // that do not print the taught skill are not its textbooks, however
            // large the room's library is, so neither the count nor the quality
            // mentions them. A shelf with nothing for this class reports just
            // that, without a misleading count in front of it.
            if (settings.RequireMatchingSubject)
            {
                if (books.BooksPerSubject == null
                    || !books.BooksPerSubject.TryGetValue(subject, out int subjectBooks))
                {
                    return "ActTx_Inspect_NoSubjectBooks".Translate(subject.LabelCap);
                }
                StringBuilder text = new StringBuilder();
                text.Append("ActTx_Inspect_Title".Translate(subjectBooks.Named("COUNT")));
                QualityCategory best = books.BestQualityPerSubject[subject];
                if (best != QualityCategory.Awful)
                {
                    text.Append(" (" + best.ToString() + ")");
                }
                text.AppendLine();
                if (books.PerSubject != null
                    && books.PerSubject.TryGetValue(subject, out RoomTextbooks.LessonBonus lessonBonus))
                {
                    text.AppendLine(SubjectLine(subject, lessonBonus));
                }
                else
                {
                    // Counted but worthless: awful copies still show in the count.
                    text.AppendLine("ActTx_Inspect_WorthlessBooks".Translate(subject.LabelCap));
                }
                return text.ToString().TrimEndNewlines();
            }

            StringBuilder total = new StringBuilder();
            total.Append("ActTx_Inspect_Title".Translate(books.CountedBooks.Named("COUNT")));
            if (books.BestQuality != QualityCategory.Awful)
            {
                total.Append(" (" + books.BestQuality.ToString() + ")");
            }
            total.Append(": +" + books.Rate.ToStringPercent("0") + ", ");
            total.Append("ActTx_Inspect_Budget".Translate(
                (SkillRecord.MaxFullRateXpPerDay + books.Budget).ToString()));
            return total.ToString();
        }

        private static string SubjectLine(SkillDef skill, RoomTextbooks.LessonBonus bonus)
        {
            return "  " + skill.LabelCap
                + ": +" + bonus.Rate.ToStringPercent("0")
                + ", " + "ActTx_Inspect_Budget".Translate(
                    (SkillRecord.MaxFullRateXpPerDay + bonus.Budget).ToString());
        }
    }
}
