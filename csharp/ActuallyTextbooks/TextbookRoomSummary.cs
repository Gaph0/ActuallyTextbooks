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
        /// only <paramref name="subject"/> is reported, since every other book on
        /// the shelf is noise next to the class it serves; a room with no
        /// covering book then reports just that fact. With matching off every
        /// book helps every lesson, so the room total is reported instead.
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

            StringBuilder text = new StringBuilder();
            text.Append("ActTx_Inspect_Title".Translate(books.CountedBooks.Named("COUNT")));
            if (books.BestQuality != QualityCategory.Awful)
            {
                text.Append(" (" + books.BestQuality.ToString() + ")");
            }

            if (settings.RequireMatchingSubject)
            {
                if (books.PerSubject == null
                    || !books.PerSubject.TryGetValue(subject, out RoomTextbooks.LessonBonus lessonBonus))
                {
                    return "ActTx_Inspect_NoSubjectBooks".Translate(subject.LabelCap);
                }
                text.AppendLine();
                text.AppendLine("  " + subject.LabelCap
                    + ": +" + lessonBonus.Rate.ToStringPercent("0")
                    + ", " + "ActTx_Inspect_Budget".Translate(
                        (SkillRecord.MaxFullRateXpPerDay + lessonBonus.Budget).ToString()));
                return text.ToString().TrimEndNewlines();
            }

            text.Append(": +" + books.Rate.ToStringPercent("0") + ", ");
            text.Append("ActTx_Inspect_Budget".Translate(
                (SkillRecord.MaxFullRateXpPerDay + books.Budget).ToString()));
            return text.ToString();
        }
    }
}
