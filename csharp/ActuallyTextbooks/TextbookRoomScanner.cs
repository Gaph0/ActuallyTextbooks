using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// Collects the textbooks in one classroom and folds them into a
    /// <see cref="RoomTextbooks"/> value.
    /// <para>
    /// This is cold-path code — at most one call per room every
    /// <see cref="RoomTextbookCache.RebuildInterval"/> ticks — so it may use LINQ-free
    /// loops and allocate freely. The lesson hot path only ever reads the cached
    /// result, so nothing here needs to be allocation-free.
    /// </para>
    /// </summary>
    public static class TextbookRoomScanner
    {
        private static readonly List<Candidate> Candidates = new List<Candidate>();

        private struct Candidate
        {
            public int Quality;

            /// <summary>Subjects printed on the book; null when matching is off.</summary>
            public List<SkillDef> Subjects;

            /// <summary>
            /// Tie-break for ordering. <see cref="List{T}.Sort"/> is an unstable sort, so
            /// without a total order two books of equal quality could come out in
            /// different orders on different machines and produce different bonuses —
            /// a desync in multiplayer and a replay divergence.
            /// </summary>
            public int ThingIdNumber;
        }

        /// <summary>
        /// Books filed in bookcases standing in the room always count: they never
        /// deteriorate there, and hauling books into bookcases is exactly what vanilla
        /// colonists already do, which is the zero-friction behaviour the collective
        /// design depends on. Loose books on the floor count while unforbidden (setting)
        /// so a freshly bought textbook works before anyone builds a bookcase.
        /// </summary>
        public static void Fill(Room room, RoomTextbooks result)
        {
            result.Reset();
            EducationSettings settings = EducationMod.Settings;
            if (settings == null || !settings.Enabled || room == null || room.Map == null)
            {
                return;
            }

            Map map = room.Map;
            bool matching = settings.RequireMatchingSubject;
            Candidates.Clear();

            foreach (Building_Bookcase bookcase in map.listerThings.GetThingsOfType<Building_Bookcase>())
            {
                if (bookcase == null || bookcase.GetRoom() != room)
                {
                    continue;
                }
                IReadOnlyList<Book> held = bookcase.HeldBooks;
                for (int i = 0; i < held.Count; i++)
                {
                    TryAddCandidate(held[i], matching);
                }
            }

            if (settings.CountLooseTextbooks)
            {
                // A book inside a bookcase is not spawned (it lives in the bookcase's
                // ThingOwner) and a book being hauled sits in a pawn's carry tracker,
                // so neither can be counted twice by this pass.
                List<Thing> booksInGroup = map.listerThings.ThingsInGroup(ThingRequestGroup.Book);
                for (int i = 0; i < booksInGroup.Count; i++)
                {
                    Thing thing = booksInGroup[i];
                    if (thing == null || !thing.Spawned || thing.Map != map || thing.Position.GetRoom(map) != room)
                    {
                        continue;
                    }
                    // Forbidden things are outside the colony's usable stock by
                    // convention: forbidding a book is the player parking it.
                    if (thing.IsForbidden(Faction.OfPlayer))
                    {
                        continue;
                    }
                    TryAddCandidate(thing as Book, matching);
                }
            }

            Aggregate(result, settings, matching);
            Candidates.Clear();
        }

        private static void TryAddCandidate(Book book, bool matching)
        {
            if (book == null || book.Destroyed || book.IsBurning())
            {
                return;
            }
            CompBook comp = book.BookComp;
            if (comp == null)
            {
                return;
            }
            TextbookDoer doer = comp.GetDoer<TextbookDoer>();
            if (doer == null || !doer.TryGetQuality(out QualityCategory quality))
            {
                return;
            }
            List<SkillDef> subjects = null;
            if (matching)
            {
                subjects = new List<SkillDef>(4);
                foreach (SkillDef subject in doer.Subjects)
                {
                    if (subject != null && !subjects.Contains(subject))
                    {
                        subjects.Add(subject);
                    }
                }
            }
            Candidates.Add(new Candidate
            {
                Quality = (int)quality,
                Subjects = subjects,
                ThingIdNumber = book.thingIDNumber
            });
        }

        /// <summary>
        /// Best quality first, weighted 1 / 0.5 / 0.25 … across the top
        /// <c>MaxBooksCounted</c> books. Sorting never consults <c>Rand</c>, so the
        /// outcome is identical on every machine — required for multiplayer and
        /// replay.
        /// </summary>
        private static void Aggregate(RoomTextbooks result, EducationSettings settings, bool matching)
        {
            if (Candidates.Count == 0)
            {
                return;
            }
            // Total order: best quality first, then oldest book first. Never consults
            // Rand, so every machine reaches the same aggregation.
            Candidates.Sort(static (a, b) =>
            {
                int byQuality = b.Quality.CompareTo(a.Quality);
                return byQuality != 0 ? byQuality : a.ThingIdNumber.CompareTo(b.ThingIdNumber);
            });

            int maxBooks = Mathf.Max(1, settings.MaxBooksCounted);
            float rateTotal = 0f;
            int budgetTotal = 0;

            for (int i = 0; i < Candidates.Count && result.CountedBooks < maxBooks; i++)
            {
                Candidate candidate = Candidates[i];
                QualityCategory quality = (QualityCategory)candidate.Quality;
                float weight = settings.WeightForIndex(result.CountedBooks);
                result.CountedBooks++;
                if (quality > result.BestQuality)
                {
                    result.BestQuality = quality;
                }

                float rate = EducationSettings.RateBonusFor(quality) * weight;
                int budget = Mathf.RoundToInt(EducationSettings.DailyBudgetBonusFor(quality) * weight);
                if (rate <= 0f && budget <= 0)
                {
                    continue; // Awful copies count for the UI but are worth nothing.
                }

                if (!matching)
                {
                    rateTotal += rate;
                    budgetTotal += budget;
                    continue;
                }

                if (candidate.Subjects == null || candidate.Subjects.Count == 0)
                {
                    continue;
                }
                if (result.PerSubject == null)
                {
                    result.PerSubject = new Dictionary<SkillDef, RoomTextbooks.LessonBonus>();
                }
                for (int s = 0; s < candidate.Subjects.Count; s++)
                {
                    SkillDef subject = candidate.Subjects[s];
                    result.PerSubject.TryGetValue(subject, out RoomTextbooks.LessonBonus existing);
                    result.PerSubject[subject] = new RoomTextbooks.LessonBonus(existing.Rate + rate, existing.Budget + budget);
                }
            }

            if (!matching)
            {
                result.Rate = Mathf.Min(rateTotal, settings.MaxRateBonus);
                result.Budget = budgetTotal;
                return;
            }

            // Per-subject clamp, applied after summing: a Legendary + Excellent +
            // Normal triple on the same subject must not pass the configured ceiling.
            if (result.PerSubject != null)
            {
                foreach (KeyValuePair<SkillDef, RoomTextbooks.LessonBonus> pair in result.PerSubject)
                {
                    if (pair.Value.Rate > settings.MaxRateBonus)
                    {
                        // Mutating a value while enumerating is not allowed, so the
                        // clamp writes through this collected key list instead.
                        ClampQueue.Add(pair.Key);
                    }
                }
                for (int i = 0; i < ClampQueue.Count; i++)
                {
                    SkillDef subject = ClampQueue[i];
                    RoomTextbooks.LessonBonus clamped = result.PerSubject[subject];
                    result.PerSubject[subject] = new RoomTextbooks.LessonBonus(settings.MaxRateBonus, clamped.Budget);
                }
                ClampQueue.Clear();
            }
        }

        private static readonly List<SkillDef> ClampQueue = new List<SkillDef>();
    }
}
