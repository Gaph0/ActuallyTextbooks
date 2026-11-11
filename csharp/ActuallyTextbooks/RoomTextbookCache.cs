using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// Per-map cache of classroom textbook values, keyed by <c>Room.ID</c>.
    /// <para>
    /// Entries are built on demand and rebuilt at most every
    /// <see cref="RebuildInterval"/> ticks, so a lesson tick costs one dictionary
    /// lookup and the book scan runs a handful of times per second per classroom at
    /// worst. There is no ticking: nothing is computed unless a lesson (or the inspect
    /// pane) asks for it.
    /// </para>
    /// <para>
    /// The cached values are never written: this class overrides no <c>ExposeData</c>.
    /// A map does scribe its component list with <c>LookMode.Deep</c>, which stores the
    /// runtime type name, so saves carry one field-less <c>RoomTextbookCache</c> node —
    /// the standard caveat for any MapComponent mod. <see cref="FinalizeInit"/> starts
    /// from an empty dictionary, and with the mod removed RimWorld resolves that node to
    /// nothing and logs a missing-type warning rather than failing to load the save.
    /// </para>
    /// </summary>
    public sealed class RoomTextbookCache : MapComponent
    {
        /// <summary>How long a rebuild stays fresh. A lesson lasts thousands of ticks.</summary>
        public const int RebuildInterval = 250;

        private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();

        public RoomTextbookCache(Map map) : base(map)
        {
        }

        /// <summary>The cache belonging to a room's map, or null.</summary>
        public static RoomTextbookCache CacheFor(Map map)
        {
            return map == null ? null : map.GetComponent<RoomTextbookCache>();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            entries.Clear();
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            entries.Clear();
            // Drop the debug throttle table too, so entries for pawns of a discarded
            // map are not held for the rest of the session.
            LessonDebugLogger.Clear();
        }

        /// <summary>
        /// Values for a room, refreshed when the entry is missing or stale.
        /// <para>
        /// Room ids are reused after walls change, so the stored <see cref="Room"/>
        /// reference is re-checked on every lookup; an entry belonging to a demolished
        /// layout can therefore never serve the room that inherited its id.
        /// </para>
        /// </summary>
        public RoomTextbooks TryGet(Room room)
        {
            if (room == null || room.Map != map)
            {
                return null;
            }
            int key = room.ID;
            if (!entries.TryGetValue(key, out Entry entry) || entry.Room != room)
            {
                entry = new Entry { Room = room };
                entries[key] = entry;
            }
            if (entry.IsStale)
            {
                entry.Rebuild();
            }
            return entry.Textbooks;
        }

        /// <summary>
        /// Drop everything so the next query reflects new settings or moved books
        /// instead of waiting out the staleness window.
        /// </summary>
        public void Invalidate()
        {
            entries.Clear();
        }

        /// <summary>
        /// Invalidate every loaded map (called when settings change).
        /// <para>
        /// <c>Find.Maps</c> is null while no game is loaded (it is
        /// <c>Current.Game?.Maps</c>), e.g. when the mod settings dialog closes at
        /// the main menu. Settings are written from the dialog's PreClose, and an
        /// exception there prevents the window from closing at all, so bail out
        /// quietly when there is nothing to invalidate.
        /// </para>
        /// </summary>
        public static void InvalidateAll()
        {
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return;
            }
            for (int i = 0; i < maps.Count; i++)
            {
                CacheFor(maps[i])?.Invalidate();
            }
        }

        private sealed class Entry
        {
            public Room Room;

            public int RebuiltAtTick = -1;

            public readonly RoomTextbooks Textbooks = new RoomTextbooks();

            public bool IsStale
            {
                get
                {
                    if (Room == null || Room.Map == null)
                    {
                        return true;
                    }
                    return Find.TickManager.TicksGame - RebuiltAtTick >= RebuildInterval;
                }
            }

            public void Rebuild()
            {
                RebuiltAtTick = Find.TickManager.TicksGame;
                TextbookRoomScanner.Fill(Room, Textbooks);
            }
        }
    }
}
