# Actually Textbooks — Mod Plan

**Target:** RimWorld 1.7 (net10.0 "Waylith" runtime) · **Requires:** Biotech (lessons, school desks, learning desires) · books/bookcases are Core 1.7 content
**Concept:** Textbooks stocked in the classroom raise students' lesson XP rate and their per‑skill daily XP budget. **No level caps are added, raised or altered** — neither on teachers nor on books (the teacher-cap idea was removed; vanilla has no such cap to raise).

---

## 1. Verified 1.7 vanilla mechanics (decompiled, Assembly-CSharp 1.7.9805)

Namespaces moved in 1.7: `SkillRecord`, `Pawn_SkillTracker`, `SchoolUtility`, `LearningUtility` are now `RimWorld` (not `Verse`).

### 1.1 How lessons work
- Children satisfy a **Learning desire need** (`Need_Learning`, `pawn.needs.learning`). Desire defs (Biotech `LearningDesires.xml`): `Lessontaking` (**0.25 xp/tick**, selectionWeight 2.5), `Workwatching` (0.16), `Reading`, `Floordrawing`, `NatureRunning`, `Skydreaming`, `Radiotalking`.
- `LearningGiver_Lessontaking.CanDo`: needs **any** childcare‑enabled adult reachable (`SchoolUtility.FindTeacher` — no skill check) + a reservable `ThingDefOf.SchoolDesk` (student spot = `InteractionCells[0]`, teacher spot = `InteractionCells[1]`).
- Student job `JobDriver_Lessontaking`: sits, ticks `LearningUtility.LearningTickCheckEnd` (fills learning need, ends job at 0.999). Fails after 5000 ticks without a matching teacher. Lessons are **juveniles-only** (`FailOnChildLearningConditions`).
- Teacher job `JobDriver_Lessongiving`, per tick while both ready:
  `student.skills.Learn(taughtSkill, LearningDesireDefOf.Lessontaking.xpPerTick * LearningUtility.LearningRateFactor(student) * delta)`; teacher gets 0.1 Social xp/tick.
- `ChooseSkill(student)` (private): teacher picks randomly among **their own top 4 skills** (`TopTeachableSkillCount = 4`) that have a `lessonInteraction` and aren't disabled for the student. If null → job ends `Incompletable`. `taughtSkill` is exposed in save data.
- `LearningUtility.LearningRateFactor(pawn)` = `StatDefOf.LearningRateFactor` (pawn stat) × `SchoolDeskLearningRate(desk)`; desk rate = **1 + 0.2 × connected blackboards** (`CompAffectedByFacilities` links, `ThingDefOf.Blackboard`, max 3). This is the facility pattern to mirror.
- **There is NO numeric teacher‑level cap in vanilla** (verified). The teacher's level never enters the XP formula; the only implicit limits are `CanTeachNow` eligibility and the top‑4 subject pick. Lessons have no level ceiling at all — the only ceiling is the per‑skill daily XP budget (§1.2, §1.5). ⇒ This mod is **buff-only**; it introduces no caps.

### 1.2 Skill XP pipeline (`RimWorld.SkillRecord`)
- `Learn(float xp, bool direct = false, bool ignoreLearnRate = false)` — central choke point for ALL xp. If `!ignoreLearnRate`: `xp *= LearnRateFactor(direct)`; `xpSinceMidnight += xp` (when `!direct`); levels up from `XpForLevelUpCurve` (1000 xp at lvl 0 → 10 000 at lvl 9 → 30 000 at lvl 19).
- `LearnRateFactor(bool direct)` = passion (None 0.35 / Minor 1 / Major 1.5) × `StatDefOf.GlobalLearningFactor` × (×`AnimalsLearningFactor` for Animals) × **0.2 if `LearningSaturatedToday`**.
- **Daily cap:** `MaxFullRateXpPerDay = 4000` (const), `LearningSaturatedToday => xpSinceMidnight > 4000f`, `SaturatedLearningFactor = 0.2f`. Per‑skill, per‑day; reset at hour 0 local (`Pawn_SkillTracker.SkillsTickInterval`, public field `xpSinceMidnight`). Level ≥ 10 decays daily (`Interval()`), unchanged by us.
- `Pawn_SkillTracker.Learn(SkillDef, xp, direct, ignoreLearnRate)` forwards to the record.

### 1.3 Books (Core 1.7 — the vanilla framework we extend)
- `Verse.Book : ThingWithComps` — auto-generated title/description (`GenerateBook`, seeded `Rand.Block`, regenerates on `PostQualitySet`), non-stackable, `BookComp => GetComp<CompBook>()`, reading float-menu (`PawnReadNow` → `JobDefOf.Reading`).
- `Verse.BookUtility` — `MakeBook`, `TryGetRandomBookToRead` (map books + `Building_Bookcase.HeldBooks`), `GetSkillExpForQuality` (reading xp/tick–factor 0.05 Awful → 0.2 Legendary), `GetReadingBonus(thing)` = room stat `RoomStatDefOf.ReadingBonus`.
- `CompBook : CompReadable` with `BookOutcomeDoer`s; `BookOutcomeDoerGainSkillExp`:
  - On generation: 1–2 random subject skills (2 skills → ×1.25), xp value from `GetSkillExpForQuality`.
  - `OnReadingTick(reader, factor)`: `reader.skills.GetSkill(s).Learn(xp * factor)` while `CanProgressSkill`.
  - **`QualityMaxLevel` curve — quality caps the skill level a book can train to:** Awful 2, Poor 5, Normal 8, Good 10, Excellent 12, Masterwork 14, Legendary 16. `Values` (subject→xp map) is a public `IReadOnlyDictionary` → subject matching is possible without reflection.
- `ThingDefOf.TextBook` **already exists in vanilla** ("A book which focuses on teaching skills", MarketValue 160, `BookOutcomeProperties_GainSkillExp` + 1% quest doer). Sold by orbital/misc traders (1–2 stock).
- `Building_Bookcase` (Core furniture, 2×1, "up to ten books", stored books never deteriorate): `HeldBooks` (`ThingOwner<Book>`, `IReadOnlyList<Book>`), `MaximumBooks = building.maxItemsInCell * size.Area`, storage settings (it IS a haul destination for books — relevant to Option A), `ReadingBonus` room stat ("passively enhance academic work done nearby").
- **No vanilla way to write/craft books** (no writing recipe/bench anywhere in Data/) — books only come from traders/quests/loot. → The mod must add an acquisition path (progression!).

### 1.4 Classroom
- `RoomRoleWorker_Classroom`: +8 score per `SchoolDesk` or `Blackboard` in `ContainedAndAdjacentThings`; 0 if any human bed; Biotech-gated. (Anchor classroom detection on the **desk's room**, not the room role label, for robustness.)
- `StatDefOf.LearningRateFactor` (Biotech `Stats_Pawns_General.xml`, `defaultBaseValue` 1, no `parts` node, `showDevelopmentalStageFilter>Child`) and `StatDefOf.ReadingSpeed` are real pawn stats. **⚠ Do not put our rate bonus on `LearningRateFactor` — see §1.5.**

### 1.5 Lesson XP budget — verified arithmetic (and the trap that shapes the design)
`Need_Learning`: `BaseFallPerTick = 3e-6` (`NeedInterval` subtracts `0.00045` per 150-tick interval) ⇒ **0.18 of need drained per 60 000-tick day**. `LearningGiver.CanDo` gates start at `CurLevel < 0.9` (`StartJobMaxLearning`); the session ends at `>= 0.999` (`LearningSatisfied`) ⇒ each lesson refills ≈ 0.099 of need.

Per lesson tick, **both** channels multiply the same `LearningUtility.LearningRateFactor(student)` (= `LearningRateFactor` stat × desk/blackboard factor):
- need fill: `1.2e-5 × LRF` (`LearningTickCheckEnd`)
- skill xp: `0.25 × LRF`, then × `SkillRecord.LearnRateFactor(...)` inside `Learn`

**⚠ The trap — `LRF` cancels out of daily XP.** Session length = needFilled / (1.2e-5 × LRF), so
`XP/session = 0.25 × LRF × length × LearnRateFactor = (0.25 / 1.2e-5) × needFilled × LearnRateFactor ≈ 20 833 × 0.099 × LRFactor ≈ 2 060 × LRFactor` — **independent of `LRF`**. Raising `LRF` only makes lessons *shorter* (it frees the child sooner); it does **not** increase XP/day. This equally explains why vanilla's blackboard bonus (+20% each) boosts nothing but session speed.

Budget: 0.18 drained/day ÷ 0.099 per session ⇒ **≈1.8 lesson sessions/day** ⇒ **≈4 100 raw XP/day/skill**, i.e. vanilla already lands right on the 4 000 line (with passion-Minor, `GlobalLearningFactor` 1.0), so the tail of the second session runs at the ×0.2 saturated rate; effective vanilla ≈ **4 024 XP/day/skill**.

Consequences:
1. The rate bonus must hook **`SkillRecord.LearnRateFactor`** (multiplies xp inside `Learn`, not cancelled) — never the `LearningRateFactor` stat.
2. The **daily-budget raise is the primary lever**: a rate bonus alone mostly converts into a longer saturated tail, so the two bonuses are complementary by construction (rate raises XP; the raised budget lets it count).
3. Budgets are **per skill** (`xpSinceMidnight` per `SkillRecord`) and `ChooseSkill` spans the teacher's top 4 ⇒ up to 4 independent budgets/day per child.
4. Figures assume an available childcare teacher and a free desk pair; fewer sessions scale all numbers down proportionally. Treat as upper bounds. Level ≥ 10 daily decay (`Interval()`: −0.1 … −12 per 200 ticks) is untouched and dominates at high levels.

---

## 2. Two design options explored

### Option A — Per‑student textbook (exclusive)
Each student owns a specific copy; only they benefit.

**Variants:**
- **A1 "Carried book":** bonus applies when a textbook sits in `pawn.inventory.innerContainer` (scan cached per ~250 ticks). No assignment UI — the player just hauls a book onto the child. *Catches:* bookcase is a book haul destination → colonists will auto‑file the child's book away; must be handled with a `Forbidden`-style toggle or a small `CompAssignedTextbook` + float-menu "Assign to…" that auto-hauls. Books have mass and occupy inventory.
- **A2 "Formal assignment":** pawn↔book registry (WorldComponent with `Scribe_References`), assignment gizmos/UI (pawn pane or float menu on the book), re-haul jobs when the book moves, cleanup on book loss/destruction/sale, save-migration edge cases.

**Pros:** per-child specialization (subject-matched books per kid — the `Values.Keys` API supports it); works wherever the child goes (caravan reading); stronger RPG/item-economy feel (the Legendary copy becomes a named colony treasure); natural durability/wear hooks.
**Cons:** highest complexity & save-state surface (registry, dangling refs, hauling fights, forbid management); micromanagement burden scales with child count; conflicts with vanilla auto-hauling into bookcases; UI work (assignment tab/gizmos, per-pawn tooltips); bonus invisible unless extensive UI is built; child death/loss bookkeeping.
**Patch delta vs B:** none (the single `SkillRecord.LearnRateFactor` hook serves either design identically — A1 only changes *where the bonus comes from*, i.e. `pawn.inventory` instead of the room cache) — the cost is entirely in management/UI/save state.

### Option B — Collective classroom library (recommended core)
Textbooks are stored in a **bookcase in the classroom** (same room as the school desk). Bonus is a property of the *classroom*: every student at desks in that room benefits.

**Mechanic:** per-room aggregation of the `Book`s that carry our textbook doer; top-N books with diminishing weights drive both rate and daily-budget bonuses. Cached in a `MapComponent` (rebuild every 250 ticks + on load — bookcase already exposes `HeldBooks`; no per-book hooks needed).

**Pros:** near-zero per-pawn state → **save-safe by construction** (nothing new persisted except settings; the cache is rebuilt post-load); zero micromanagement (books already *want* to be hauled into bookcases by vanilla AI); zero marginal cost per extra student (books are never consumed or reserved); mirrors the mental model players already have from blackboards ("better room → better lessons"); one Legendary book uplifting the whole class is a clear, readable fantasy; trivial performance (room cache, one dictionary lookup on lesson ticks only); graceful degradation (no books → feature inert, vanilla behavior untouched).
**Cons:** no per-child customization; the best book benefits all students equally (less item-economy drama); bonus tied to a room (fine — lessons are room-bound by definition since they require a school desk); a single Legendary copy is a shared colony asset, so losing it hurts everyone at once.

### Verdict
**Ship Option B as v1.0.** It fulfills the stated concept ("textbooks available *in the classroom*") directly, at roughly ⅓ the implementation surface of A2 and far lower save-compatibility risk. Keep the effect pipeline abstracted (`TextbookBonusProvider`) so **A1 can be added later as a free "Personal Workbook" update** — a small separate item (or any textbook carried in inventory) granting a modest *stackable* bonus (+10–15%) on top of the classroom bonus, with only an inventory-scan and no forced assignment system (auto-haul conflict solved by making the workbook def non-book-storable or auto-forbid-managed). If per-student depth is ever demanded by players, add A2's assignment UI then.

---

## 3. System design (Option B)

### 3.1 Content
- **Reuse vanilla `TextBook`** via XML patch: **replace** its `BookOutcomeProperties_GainSkillExp` doer with `BookOutcomeProperties_Textbook`, our subclass (keep the 1% quest doer). Subclassing rather than adding a sibling doer means vanilla reading xp/subject logic is inherited unchanged (no double-dip by construction) and we get `Values` (subject map) + `Quality` on the same instance for free. Trader-bought textbooks become functional instantly; no parallel item confusion; the vanilla recreation-reading lane (quality-capped 2→16) keeps working — *intentional synergy: books teach via lessons AND via self-study.*
- **Acquisition:**
  - ~~New research / "write textbook" recipe / authored-subject doer~~ — **M4 dropped.** v1.0 adds **no new acquisition path**: textbooks come from vanilla traders (orbital/misc already stock 1–2), quests and loot. That keeps the mod to zero new defs and one doer swap, at the cost of coverage being luck-driven while subject matching is on (§7).
  - Deferred to v1.1 if coverage proves too sparse: `ActTx_TextbookPrinting` research → "write textbook" recipe (`ThingMaker.MakeThing(ThingDefOf.TextBook)` then `CompQuality.SetQuality(q, ArtGenerationContext.Colony)` ⇒ `PostQualitySet → GenerateBook` writes the title and subjects for free), plus `BookOutcomeDoer_AuthoredSubject : TextbookDoer` overriding `GetAvailableSkills(author)` so a Master cook writes cookbooks.

### 3.2 Bonuses from classroom textbooks
**Which books count** ("available in the classroom"): `Verse.Book` things carrying our textbook doer, located in the **lesson room** = `desk.GetRoom()` for the school desk the student sits at.
- Books held by any `Building_Bookcase` in that room (default, recommended — bookcase-held books never deteriorate and are exactly what colonists auto-haul there).
- Loose, unforbidden `Book`s spawned in that room (setting `countLooseTextbooks`, default **on**, so a hand-dropped book works immediately with no bookcase required).
- Not counted: books in other rooms, forbidden books, books in inventories/caravel/haul, destroyed or burning books.

Per quality tier (0=Awful … 6=Legendary), proposed defaults (all exposed as settings curves):

| Quality | Lesson XP rate bonus | Daily budget bonus (per skill, added to vanilla 4 000) |
|---|---|---|
| Awful | +0% | +0 |
| Poor | +8% | +400 |
| Normal | +15% | +800 |
| Good | +22% | +1 200 |
| Excellent | +30% | +1 800 |
| Masterwork | +40% | +2 400 |
| Legendary | +50% | +3 200 |

**Aggregation (room-scoped, shared by every student):** sort candidates by quality desc → take the top `maxBooksCounted` (default **3**) → weights `1.0 / 0.5 / 0.25`:
- `rateBonus = Σ rate(qᵢ) × wᵢ`, clamped to `maxRateBonus` (default **+60%**)
- `dailyBonus = Σ budget(qᵢ) × wᵢ` (unclamped; a Legendary-heavy shelf ≈ +4 300 → 8 300/day/skill)
Both are properties of the **room**, identical for all students; nothing is consumed, reserved or hauled.

**Subject matching** (setting `requireMatchingSubject`, default **ON**): a book contributes only for skills in its `Values.Keys` (readable inside the hook via `SkillRecord.def`). On ⇒ a textbook is a *curriculum*, and an off-subject book does nothing for that lesson (§10.3); off ⇒ it is a general *curriculum-quality* signal and any textbook boosts any lesson.

Matching is on by default even though the M4 authored-writing recipe was dropped: it is the mechanic that makes "stock the classroom" a real decision rather than a one-time purchase, because each textbook covers only 1–2 of ~12 skills and the player must collect coverage for whatever the teacher actually teaches. The cost is documented in §7 — early on, most trader textbooks will be off-subject — and the off switch remains for players who want the simpler buff. The inspect readout (§3.4) lists covered subjects precisely so the player can see what a shelf does and does not help.

**Scope:** applies only while `pawn.CurJobDef == JobDefOf.Lessontaking` at a `ThingDefOf.SchoolDesk`. Workwatching, recreation reading, work xp, level decay, `Aptitude` and vanilla's book reading cap (Awful 2 → Legendary 16) are all untouched.

### 3.3 Implementation hook: ONE postfix on `SkillRecord.LearnRateFactor`
Per §1.5 the rate bonus must not ride on `StatDefOf.LearningRateFactor` (it cancels out of daily XP — that would only shorten lessons). `SkillRecord.LearnRateFactor(bool direct)` is the single multiplier applied inside `Learn` to every xp gain *and* the place where the ×0.2 saturation penalty is applied, so **one postfix implements both features**:

```csharp
// [HarmonyPatch(typeof(RimWorld.SkillRecord), "LearnRateFactor")]
static void Postfix(SkillRecord __instance, bool direct, ref float __result)
{
    if (direct || DebugSettings.fastLearning || !Settings.enabled || !ModsConfig.BiotechActive) return;
    Pawn pawn = __instance.Pawn;                       // public property
    if (pawn.CurJobDef != JobDefOf.Lessontaking) return;  // cheapest gate first — runs on every xp gain on the map
    if (!TextbookBonus.TryGet(pawn, __instance.def, out float rate, out float budgetBonus)) return;

    float spent      = __instance.xpSinceMidnight;                        // public field
    bool wasCapped   = spent > SkillRecord.MaxFullRateXpPerDay;           // public const = 4000
    bool isCappedNow = spent > SkillRecord.MaxFullRateXpPerDay + budgetBonus;
    if (wasCapped && !isCappedNow)
        __result /= SkillRecord.SaturatedLearningFactor;                  // public const = 0.2f → undo vanilla's penalty
    __result *= 1f + rate;
}
```

Why this exact shape:
- **Public API only** (`Pawn`, `xpSinceMidnight`, `MaxFullRateXpPerDay`, `SaturatedLearningFactor`) → no `AccessTools`, no transpiler, no fragile private-field coupling.
- Patching `LearnRateFactor` (a sizeable method) instead of the tiny `get_LearningSaturatedToday` property avoids JIT-inlining defeating the patch; and vanilla has **no saturation UI at all** (verified: no `saturat*` key in any Keyed/Strings file; the property's only consumer is `LearnRateFactor`), so undo-vs-recompute creates no display inconsistency.
- Guarding `DebugSettings.fastLearning` is mandatory: that path returns `200f` **before** applying any saturation factor, so dividing by 0.2 there would yield 1000×.
- Undoing the vanilla factor rather than rewriting it keeps results bit-identical to vanilla when no bonus applies.
- Both effects compound correctly: the rate bonus burns the raised budget faster, which is precisely why the budget rise is what makes high-quality books valuable.
- **Total Harmony surface: 1 gameplay patch** (+1 optional inspect-string patch if the XML-comp route in §3.4 doesn't suit). No patch on `JobDriver_Lessongiving`, `ChooseSkill`, `SkillRecord.Learn`, `LearningUtility`, or any StatDef.

### 3.4 UX surfaces (no custom tabs, no new windows)
- **The book's own description** — override `GetBenefitsString(reader)` in our doer to append `As a classroom textbook: +X% lesson XP, +Y daily XP budget for every student`. Shows in the existing book info card/textbook tooltip: zero new UI, and the numbers travel with the item.
- **Classroom readout** — tiny `ThingComp_TextbookRoom` (`CompInspectStringExtra`) added by XML patch to `SchoolDesk` and `Bookcase`: `Classroom textbooks: 2 (best Excellent) · Lesson XP +30% · Daily budget 5 800/skill`. Informational only ⇒ **zero Harmony for UI**. Fallback: `Building.GetInspectString` postfix.
- **Dev tools** — "Spawn textbook of quality…" gizmo (vanilla bookcase already ships a `DEV: Fill`) + the settings-window xp/debug logger used to validate §1.5 numbers.
- **Settings window** — master toggle, rate/budget curves, `maxBooksCounted`, `maxRateBonus`, `countLooseTextbooks`, `requireMatchingSubject`, debug xp logging.

---

## 4. Implementation plan

### 4.1 Project scaffold
- `ActuallyTextbooks/` mod folder: `About/` (supportedVersions 1.7, load-after Ludeon.Biotech, Ludeon.RimWorld; packageId `gapho.actuallytextbooks`), `1.7/Assemblies/`, `1.7/Defs/`, `1.7/Patches/`.
- C# project: single TFM **net10.0** (1.7-only; the `RimWorld.SkillRecord` namespace move + LearningDesire system make 1.6 back-porting a separate product). Reuse ConditionalDrugSchedule build pattern: game refs `Private=false`, gitignored `LocalPaths.props`, `AppendTargetFrameworkToOutputPath` **true** (known race otherwise), AfterBuild copy dll into `1.7/Assemblies`.
- Dependency: **Harmony** (`brrainz.harmony`, modId load-after).

### 4.2 C# pieces (namespace `ActuallyTextbooks`)
1. `EducationMod : Mod` + `EducationSettings : ModSettings` — toggles, quality curves, `maxBooksCounted`, `maxRateBonus`, debug logger.
2. `BookOutcomeProperties_Textbook : BookOutcomeProperties_GainSkillExp` + `BookOutcomeDoer_Textbook : BookOutcomeDoerGainSkillExp` — inherits vanilla reading xp / subject generation untouched; adds quality→(rate, budget) curves and the `GetBenefitsString` appendix. Exposes `float LessonRateBonusOf(QualityCategory q)` / `int DailyBudgetBonusOf(q)` for the cache.
3. `TextbookBonus.TryGet(Pawn student, SkillDef skill, out float rate, out float budgetBonus)` — the single static query used by the patch: desk check (`CurJob.GetTarget(TargetIndex.A).Thing`) → `GetRoom()` → cache lookup → optional subject filter.
4. `TextbookCache : MapComponent` — `Dictionary<int roomId, RoomBonus{rate, budget, bestQuality, bookCount}>`, floats only, rebuilt every 250 ticks and on load; enumerates `map.listerThings.GetThingsOfType<Building_Bookcase>()` (the same call vanilla `TryGetRandomBookToRead` uses) plus `ThingsInGroup(ThingRequestGroup.Book)` when loose books count. **Nothing persisted.**
5. `Patches.LessonTextbooks` — the single `SkillRecord.LearnRateFactor` postfix (§3.3).
6. `ThingComp_TextbookRoom` + `CompProperties_TextbookRoom` — inspect-string UI (§3.4).
7. Post-M4 optional: `BookOutcomeDoer_AuthoredSubject : BookOutcomeDoer_Textbook` overriding `GetAvailableSkills(author)` with the author's top skills.
8. `TextbookRoomSummary` — shared builder for the classroom readout (desk inspect pane + class dialogs). `Compat_ProgressionEducation` — optional reflection-only bridge to ferny.ProgressionEducation: its `PE_AttendClass*` jobs pass the LearnRateFactor gate (its `SkillClassLogic.ApplyLearningTick` calls `SkillRecord.Learn`), and a postfix on `Dialog_CreateClass`/`Dialog_EditClass.DoWindowContents` draws the readout (dialog → `studyGroup` → `classroom` → `LearningBoard` thing → room).

Total: ~6 small classes, 1 gameplay patch, 0 transpilers; reflection only inside the optional compat bridge.

### 4.3 XML
- `Patches/Patch_TextBook.xml`: **replace** `<li Class="BookOutcomeProperties_GainSkillExp" />` inside vanilla `TextBook`'s `CompProperties_Book.doers` with `<li Class="ActuallyTextbooks.BookOutcomeProperties_Textbook" />` (keep the `BookOutcomeProperties_GiveQuest` sibling).
- `Patches/Patch_ClassroomComps.xml`: add `CompProperties_TextbookRoom` to `SchoolDesk` and `Bookcase`.
- `Defs/ResearchProjectDefs/` + `Defs/RecipeDefs/` — "Write textbook" (M4); optional `Defs/ThingDefs/` for a Writing Desk and an `ActTx_Textbook_Authored` variant; optional trader `StockGenerator` additions.
- `About/About.xml`, `LoadFolders.xml` (1.7 only), `1.7/Assemblies/ActuallyTextbooks.dll`.
- Keyed strings (`ActTx_*`) + any DefInjected description rewrites.

### 4.4 Save & MP safety
- Persistent state: **none** (settings aside; the cache is rebuilt post-load) ⇒ uninstall-with-save is clean and pre-mod saves load untouched.
- Deterministic: the hook is pure arithmetic on already-public fields, no new `Rand` call ⇒ multiplayer-safe.
- All code paths early-out when the mod is disabled, Biotech is inactive, or the room has no qualifying textbooks.

---

## 5. Milestones
| # | Deliverable | Est. |
|---|---|---|
| M0 | ✅ Scaffold: repo, csproj (net10), About/, Harmony bootstrap, empty mod loads in 1.7 | done |
| M1 | ✅ Doer subclass + `TextBook` XML patch + room cache + settings | done |
| M2 | ✅ The single `LearnRateFactor` postfix (rate bonus + daily-budget raise), aggregation weights/clamps | done |
| M3 | ✅ UX: `GetBenefitsString` appendix, `Comp_TextbookRoom` inspect readout, dev gizmo, debug logger | done |
| M4 | ❌ **Dropped** — no "write textbook" acquisition path. Books come from traders/quests/loot only; see the acquisition risk in §7. | — |
| M5 | In-game validation of the §1.5 baseline and §10 examples via the debug logger; balance pass | 0.5–1d |
| M6 | Test matrix, changelog/preview images, Workshop upload | 0.5d |

**Build status:** compiles clean against RimWorld 1.7 (0 errors, 0 warnings, net10.0); a single `ActuallyTextbooks.dll` publishes to `1.7/Assemblies/`; both XML patches were dry-run against the real vanilla defs and match exactly one node each.

Remaining work is validation in a running game (M5–M6), which cannot be done from here: the §1.5 budget model and the §10 example numbers are arithmetic from decompiled constants and still need confirming against the debug logger in playtest.

---

## 6. Balance & interaction notes
- All defaults anchor on the verified §1.5 budget: ~1.8 lessons/day, ~2 060 raw XP/lesson, **~4 024 effective XP/day/skill vanilla**. Worked through the four scenarios in §10: a 3×Normal shelf ⇒ ~5 200/day (+29%), a Legendary+Excellent+Normal shelf ⇒ ~6 590/day (+64%). Both feel meaningful without breaking the curve — level-up costs still run 1 000 (lvl 0→1) → 10 000 (lvl 9→10) → 30 000 (lvl 19→20), so lvl 9→10 shortens from ~2.5 to ~1.5 days at best.
- **Rate-without-budget is nearly wasted** (vanilla already lands on the 4 000 line), and budget-without-rate goes unused. That coupling is the intended progression loop and should be stated in the About/description text.
- Per-skill budgets × up to 4 taught subjects means a child's *total* daily XP can reach ~4×8 300 in the extreme; verify with the dev logger before shipping defaults (passion-Major children get ×1.5 on top).
- Double-dip control: recreation reading of the same textbook still grants vanilla reading xp with vanilla's quality level cap (Awful 2 → Legendary 16) — a **complementary self-study lane**, and the reason subjects still matter when lesson-side matching is off.
- Books are never consumed/reserved by lessons ⇒ the marginal cost of a 6th student is zero; throughput is limited by teachers and desk pairs, not by books (see §10.2).
- Do not touch: `GlobalLearningFactor`, the `LearningRateFactor` stat, need fill/fall rates, `LearningDesireDef.xpPerTick`, reading xp curves, level decay (`Interval()`), `Aptitude`, `ChooseSkill`.

## 7. Risks / edge cases
- **Biotech off:** no SchoolDesk/Lessontaking ⇒ feature silently inert (guard `ModsConfig.BiotechActive`); the book-description parts keep working.
- **Subject coverage with matching on and no writing recipe (M4 dropped):** trader books print 1–2 random skills of ~12, so a child's taught subject may be uncovered for a long time. Mitigations in place: the desk readout lists covered subjects explicitly, `CountLooseTextbooks` makes any purchased book work at once, a second classroom/teacher rotation raises the chance that some book matches, and `RequireMatchingSubject=false` restores the flat room-wide buff. If playtest shows coverage is too thin, the v1.1 writing recipe (§3.1) is the fix.
- **Hot path:** `LearnRateFactor` runs for every xp gain of every pawn. Guard order `direct → fastLearning → enabled/Biotech → CurJobDef` costs ~2 field/reference compares for all non-lesson pawns; the cache lookup only happens on lesson ticks.
- **Do not "fix" this with a `LearningRateFactor` StatPart** — it is mathematically the wrong lever (§1.5). Leave a code comment at the patch site so a future maintainer doesn't regress it.
- **`DebugSettings.fastLearning`** returns `200f` before any saturation logic; without the guard our `/0.2` yields 1000×. Covered by a test.
- **Patch conflicts:** education overhauls that also patch `LearnRateFactor`, `SkillRecord.Learn` or replace `Lessontaking`. Our postfix is multiplicative and additive to a const, so it composes safely under most orderings; still, load-after Harmony/Biotech, log the patch inventory at startup, and detect known overhaul mods by def name and warn once.
- **Vanilla churn:** LearningDesires/books are 1.6–1.7 additions. With one gameplay patch, an update review is: re-verify `MaxFullRateXpPerDay`, `SaturatedLearningFactor`, `LearningDesireDefOf.Lessontaking.xpPerTick`, `Need_Learning.BaseFallPerTick` in ILSpy, then re-run the §10 arithmetic.
- **Acquisition risk (M4 dropped ⇒ live):** textbooks can now only come from traders, quests and loot. With subject matching on, a colony must therefore acquire enough books to cover its teacher's top skills by luck of the market. Traders stock 1–2 `TextBook`s and subjects are random over ~12 skills, so early coverage will be thin. Accepted trade-off for the collect-a-library loop; mitigations: the `countLooseTextbooks` rule means any bought book works immediately, matching can be switched off, and the dev gizmo exists for testing. A later "write textbook" recipe (research + Intellectual requirement, `BookUtility.MakeBook(def, ctx, qualityGenerator)`) is the natural v1.1 addition if coverage proves too sparse in playtest.
- **Room edge cases:** open-air desk (no room) ⇒ no bonus; several desks in one room ⇒ shared bonus; wall removed / bookcase deconstructed / books burned ⇒ bonus drops at the next ≤250-tick refresh (harmless staleness).
- **Staleness vs. correctness:** because the bonus is read at the moment `LearnRateFactor` is called, a book destroyed mid-session simply stops helping mid-session. No desync, no save state.

## 8. Test checklist
- [ ] **Baseline validation (no mod):** dev logger records ~2 060 XP/lesson and ~4 024 XP/day/skill for a passion-Minor child ⇒ confirms the §1.5 model the defaults are tuned on.
- [ ] **Rate works, and it isn't the stat:** 3× Normal shelf ⇒ XP/lesson ≈ 1.26× baseline **and lesson duration in ticks is unchanged** (the signature of hooking `LearnRateFactor`, not `LearningRateFactor`).
- [ ] **Anti-regression probe:** temporarily adding a `LearningRateFactor` StatPart must leave XP/day unchanged (proof the trap is real — then delete it).
- [ ] **Budget raise:** push a skill past 4 000/4 800/8 300 with no books / Normal shelf / Legendary shelf ⇒ full rate until the raised line, then ×0.2.
- [ ] **Aggregation:** 1, 2, 3, 5 books of mixed quality ⇒ matches weights 1.0/0.5/0.25 and the +60% clamp (`maxBooksCounted` respected; 4th book adds nothing).
- [ ] **Fewer books than students:** 1 textbook + 6 children ⇒ every child shows the identical bonus; mod creates no reservations, haul jobs or forbid flags.
- [ ] **Irrelevant subjects:** matching off ⇒ full bonus from a Shooting textbook during a Cooking lesson; matching on ⇒ that book contributes nothing (and other qualifying books still do).
- [ ] **Loose books:** setting on ⇒ book on the classroom floor counts; off ⇒ only bookcase books count; forbidden book ⇒ never counts.
- [ ] `DebugSettings.fastLearning` ⇒ no 1000× blow-up.
- [ ] **Save/state:** load mid-lesson, load a pre-mod save, uninstall with the mod in the save ⇒ no errors, nothing persisted, vanilla numbers returned exactly.
- [ ] **Biotech off** ⇒ no errors, feature inert.
- [ ] **Perf:** no measurable tick cost at 20 adults + 6 children + 5 classrooms (HarmonyProfiler or stopwatch around the postfix).
- [ ] **Multiplayer** smoke test ⇒ deterministic (no `Rand`, no wall-clock).

## 9. Open questions
1. Is the coupling (a rate bonus only pays off once the budget is raised) the feel we want, or should rate scale gently and budget aggressively?
2. Should workwatching (0.16/tick, no lesson context) share the textbook bonus? Default no.
3. Write at the research bench vs. a new Writing Desk furniture (flavor vs. build-menu bloat)? *(M4 dropped — deferred with the whole acquisition path.)*
4. Ship subject-matching as an off-by-default v1.0 setting, or hold it until M4's authored-writing recipe exists to make it playable? *(Resolved: ships **on** by default, off switch retained, coverage risk accepted and documented in §7.)*
5. Personal Workbook (Option A1) as a free follow-up — gauge demand from feedback.
6. **Consequence of dropping the teacher cap (accepted):** book quality now moves only *speed* and *daily budget*; the sole level ceiling left anywhere in the system is vanilla's read-your-own-book cap (Awful 2 → Legendary 16), which the mod deliberately leaves intact. If a visible ceiling axis is ever wanted, an **opt-in, off-by-default** setting could mirror vanilla semantics (lessons can't push a skill past the shelf's best book's `QualityMaxLevel`). Explicitly out of scope for this plan.

---

## 10. Worked examples
Assumptions used throughout (from §1.5, and all upper bounds): passion-Minor child, `GlobalLearningFactor` 1.0, a childcare teacher always available, **2 lesson sessions/day** (need drains 0.18/day, each session refills ≈0.099), **2 060 raw XP per session**. Blackboard count is irrelevant to XP/day (it only changes session length). Vanilla budget = 4 000/skill/day.

Because **subject matching is on by default** (§3.2), each shelf has two readings. **Covered** = the child is currently being taught a skill printed on one of the counted books (that is where the numbers below apply). **Uncovered** = no counted book covers that skill ⇒ **no bonus at all**, lesson runs at vanilla 4 024 XP/day. The "matching off" column is the upper bound every subject would get if the setting is switched off.

| Classroom shelf | rate bonus (covered) | budget/skill/day | XP/day (2 sessions) | vs vanilla | lvl 9→10 (10 000 XP) |
|---|---|---|---|---|---|
| *(vanilla, no books)* | — | 4 000 | **≈4 024** (tail saturated ×0.2) | — | ≈2.5 days |
| 1× Normal | +15% (1–2 subjects only) | 4 800 | 4 738 | +17.7% | ≈2.1 days |
| 3× Normal | +26.25% (up to 6 subjects) | 5 400 | 5 202 | +29.3% | ≈1.9 days |
| Leg + Exc + Normal (matching **on**) | +50% Legendary subject, +30% Excellent subject, +15% Normal subject — **per subject, no summing** | 7 200 / 5 800 / 4 800 | up to 6 592 | up to +63.8% | ≈1.5 days |
| Leg + Exc + Normal (matching **off**) | +60% (clamped from 68.75%) | 8 300 | 6 592 | +63.8% | ≈1.5 days |

The matching-on rows are *per covered subject*, which is the whole design intent: a Legendary cookbook makes the cooking student shine, and the shooting student is unaffected until a shooting textbook turns up. Matching off trades that specificity away for a flat room-wide buff.

### 10.1 Only Normal-quality textbooks
Three Normal textbooks sit in the classroom bookcase. Each is worth +15% rate / +800 budget; weights 1.0/0.5/0.25 give **+26.25% rate, +1 400 budget ⇒ 5 400 XP/day** for a *covered* subject.
With matching on (default) those three books cover up to six distinct skills (each prints 1–2), and the +26.25% applies only where their subjects overlap the skill being taught; a subject printed on one book alone gets just +15%/+800. Raw XP per session rises 2 060 → 2 601, two sessions total 5 202 — *under* the raised 5 400 line, so the child **never touches the ×0.2 penalty** and every point of the rate bonus converts to progress: ≈1.9 days per level at lvl 9→10 instead of ≈2.5. With matching off, every subject gets that +26.25%/5 400 unconditionally.
Lesson *duration* is unchanged (unlike adding blackboards); only XP per session moves. A **single** Normal textbook gives +15% / 4 800 → 4 738 XP/day for its 1–2 subjects — a cheap trader buy already beats vanilla on those skills, which is the intended early progression.
Awful and Poor books matter mostly as budget: an Awful textbook contributes **+0%/+0** — shelf decoration, and that contrast is what makes quality worth pursuing.

### 10.2 Fewer textbooks than students (e.g. 1 textbook, 8 students)
Nothing is rationed. The bonus is a property of the **room**, so all 8 children get the identical +15%/+800; the book is never reserved, carried, consumed or worn out by the mod (vanilla bookcase-held books also never deteriorate).
This is the sharpest advantage over Option A, which would need **8 assigned copies** (plus haul jobs, assignment UI and re-assignment on child death) to reach the same result. Here the efficient target is `maxBooksCounted` = **3 good books for the classroom, regardless of student count** — and the 4th book adds nothing, which caps hoarding.
What *doesn't* scale is throughput, and books were never the lever for it: each lesson needs one free desk pair plus one free childcare adult (`SchoolUtility.FindTeacher` + reservations, one child per teacher at a time). With 8 students and 1 teacher, children queue across the day; each child's own daily budget is still per-skill and unchanged, they just get their sessions later. Practical guidance for players: **1 teacher + 1 desk per ~3–4 children beats more books.**

### 10.3 Textbooks teaching irrelevant skills (matching OFF = default)
A Normal textbook generated with Shooting + Animals sits in the classroom while the child is being taught Cooking. Vanilla textbooks pick 1–2 subjects at random from all ~12 skills (`OnBookGenerated`; 25% chance of two), so most bought books *will* be "off-subject" for any given child.
With `requireMatchingSubject = false` (default), the book is read as **curriculum quality, not curriculum content** — the child gets the full +15%/+800 for Cooking anyway. Rationale: a classroom stocked with books is a better classroom; the alternative makes trader textbooks feel broken and forces players to chase 12 subjects × quality, which fights the low-micromanagement goal of Option B.
With matching **on**, that Shooting textbook contributes **nothing** to the Cooking lesson (filtered by `SkillRecord.def` against `BookOutcomeDoerGainSkillExp.Values.Keys`); only on-subject books count, so a single off-subject book gives no lesson bonus at all — and this is exactly why matching should not ship before M4: only a *written* textbook can be authored to a chosen subject (`BookOutcomeDoer_AuthoredSubject` uses the author's own top skills). Matching-on then becomes the "hard mode" for late-game colonies that can staff specialist writers.
Either way the off-subject book is not useless: the **vanilla reading lane is untouched**, so when the child's Reading desire fires, `BookUtility.TryGetRandomBookToRead` already prefers books that `ProvidesOutcome(child)` (i.e. on-subject, below the quality level cap) and grants 0.05–0.2 XP/tick by quality.

### 10.4 One Legendary textbook plus several lower-quality ones
Shelf: Legendary, Excellent, Normal, Poor, Awful. Quality-descending order takes the **top 3 only** ⇒ Legendary(×1.0), Excellent(×0.5), Normal(×0.25); Poor and Awful are ignored entirely.

**Matching on (default).** Each counted book is worth its own quality's numbers, but only in its own printed subjects, so nothing sums and the clamp is never reached:
- the Legendary's 1–2 subjects: **+50% rate, 7 200/day**
- the Excellent's subjects: **+30%, 5 800/day** · the Normal's: **+15%, 4 800/day**
- any subject none of them prints: **no bonus** — this is the gap the player is meant to keep filling
For a covered subject: 2 060 × 1.5 ≈ 3 090 raw XP per session, two sessions = **≈6 180 XP/day** inside the 7 200 budget ⇒ no saturation, +54% over vanilla, lvl 9→10 in ~1.6 days, and headroom for a 2½th session.

**Matching off.** Everything sums per §3.2:
- rate = 50 + 15 + 3.75 = 68.75% → **clamped to +60%** (the clamp only bites here)
- budget = 3 200 + 900 + 200 = **+4 300 ⇒ 8 300 XP/day/skill**
- 2 060 × 1.6 ≈ 3 296 per session, **≈6 592 XP/day** ≈ +64%, lvl 9→10 in ~1.5 days, comfortable inside 8 300.

Read the dynamics of this shelf:
- **The Legendary carries its own subjects entirely** (matching on) or dominates the sum (matching off): pull it and the best remaining coverage is +30%, or the room drops to 30×1.0 + 15×0.5 = 37.5% with budget 1 800 + 400 = 2 200. One item is worth more than the other four combined — the item-economy tension per-student ownership would have provided, with zero per-pawn state.
- **Diminishing returns keep hoarding pointless** past 3 books and make upgrading the *best* book the meaningful purchase: a Legendary replacing an Excellent beats adding a 4th Normal.
- **Budget scales harder than rate** at the top (rate is clamped, budget isn't), so late-game quality investment keeps paying — the progression axis the mod needs now that no level cap exists.
- Because budgets are per skill, the shelf gives up to ~6 590/day to *each* covered subject the teacher rotates through — the real colony-wide multiplier, and the main thing to watch in the balance pass (§6).
