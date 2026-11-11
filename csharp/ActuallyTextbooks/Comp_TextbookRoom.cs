using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// Dev-mode spawner for testing textbook qualities without waiting on
    /// traders.
    /// <para>
    /// Added to the school desk by XML patch and hooked through
    /// <see cref="ThingWithComps.GetGizmos"/>, which forwards every comp's
    /// <see cref="CompGetGizmosExtra"/>. The classroom readout lives in the class
    /// windows of the Progression: Education mod, filtered to the taught subject,
    /// so the inspect pane carries no textbook text of its own.
    /// </para>
    /// </summary>
    public class Comp_TextbookRoom : ThingComp
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DebugSettings.godMode || !parent.Spawned)
            {
                yield break;
            }
            Command_Action spawn = new Command_Action
            {
                defaultLabel = "ActTx_Dev_SpawnTextbook".Translate(),
                defaultDesc = "ActTx_Dev_SpawnTextbookDesc".Translate(),
                icon = ThingDefOf.TextBook.uiIcon
            };
            spawn.action = delegate
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                for (int i = 0; i < 7; i++)
                {
                    QualityCategory quality = (QualityCategory)i;
                    options.Add(new FloatMenuOption(quality.GetLabel().CapitalizeFirst(), () => SpawnTextbook(quality)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            };
            yield return spawn;
        }

        /// <summary>
        /// Spawns a textbook of an exact quality beside the building.
        /// <see cref="Book.PostQualitySet"/> regenerates title and subjects, so the
        /// copy comes out exactly like a found or traded book of that quality.
        /// </summary>
        private void SpawnTextbook(QualityCategory quality)
        {
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }
            if (!CellFinder.TryRandomClosewalkCellNear(parent.Position, map, 4, out IntVec3 cell))
            {
                cell = parent.Position;
            }
            Book book = (Book)ThingMaker.MakeThing(ThingDefOf.TextBook);
            CompQuality compQuality = book.GetComp<CompQuality>();
            if (compQuality != null)
            {
                compQuality.SetQuality(quality, ArtGenerationContext.Colony);
            }
            GenSpawn.Spawn(book, cell, map);
            Room room = cell.GetRoom(map);
            // Not inside a room the books are effectively outdoors, where a lesson
            // cannot happen; warn so the tester does not think the mod is broken.
            if (room == null || !room.ProperRoom)
            {
                Messages.Message("ActTx_Dev_NotInRoom".Translate(), MessageTypeDefOf.CautionInput, false);
            }
            RoomTextbookCache.CacheFor(map)?.Invalidate();
        }
    }

    public class CompProperties_TextbookRoom : CompProperties
    {
        public CompProperties_TextbookRoom()
        {
            compClass = typeof(Comp_TextbookRoom);
        }
    }
}
