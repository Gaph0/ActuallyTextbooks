using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// Bridge to the workshop mod "Progression: Education"
    /// (ferny.ProgressionEducation), which replaces free-form lessons with
    /// scheduled classes held in classrooms. That mod is a listed dependency, so
    /// running without it takes a forced or stale mod list; every PE member is still
    /// reached by name at run time, so each touch point degrades to inert instead of
    /// throwing, and each kind of failure warns once:
    /// <list type="bullet">
    /// <item>its class attendance jobs grant skill XP through
    /// <c>SkillRecord.Learn</c>, the same choke point vanilla lessons use, so
    /// those lessons receive the textbook bonus too;</item>
    /// <item>its create/edit class dialogs list the classroom textbook readout
    /// at the bottom of the right-hand column;</item>
    /// <item>its proficiency classes train faster: a postfix on the class's
    /// per-tick progress multiplies it by the classroom's textbook bonus for
    /// the tier's paired subject (see <see cref="TrainingTierSubjects"/>).</item>
    /// </list>
    /// </summary>
    public static class ProgressionEducationCompat
    {
        private const string DialogCreateClass = "ProgressionEducation.Dialog_CreateClass";
        private const string DialogEditClass = "ProgressionEducation.Dialog_EditClass";
        private const string ProficiencyLogicClass = "ProgressionEducation.ProficiencyClassLogic";

        // Failure kinds, used as the dedup keys for LogWarningOnce. Keys are short
        // constants rather than message text so a caller on a per-frame or per-tick
        // path can ask AlreadyWarned before it builds an expensive message (an
        // exception's stack trace) only to have it discarded.
        private const string KeyDialogBridge = "dialog-bridge";
        private const string KeyTrainingBridge = "training-bridge";
        private const string KeyTrainingBoost = "training-boost";
        private const string KeyReadout = "readout";
        private const string KeyTierLabel = "tier-label";
        private const string KeyScrollBalance = "scroll-balance";

        /// <summary>
        /// Combined height of the three fixed rows that <c>DrawCustomFields</c> draws
        /// after <c>DrawRequirements</c>: classroom (30), class speed (30) and class
        /// hours (25). This mirrors the installed Progression: Education layout, so a
        /// PE update that resizes those rows moves the readout — the reason every
        /// touch point here degrades instead of throwing.
        /// </summary>
        private const float FixedRowsBelowRequirements = 85f;

        private static readonly string[] ClassJobDefNames =
        {
            "PE_AttendClass",
            "PE_AttendMeleeClass",
            "PE_AttendShootingClass"
        };

        /// <summary>
        /// Progression: Education proficiency tier (def name) → the vanilla skill
        /// whose textbooks speed that tier's training: intellectual books help animal
        /// riding, shooting books help firearms, social books help speech (basic and
        /// fluent). Keyed by def name, so a PE rename simply drops the pairing and that
        /// tier trains at PE's own speed.
        /// <para>
        /// Left untied on purpose: the weapon track's low-tech and high-tech tiers, the
        /// vehicle track's automobile driving, aerial piloting and orbital piloting
        /// tiers, and the speech track's non-speaker tier — which PE never offers as a
        /// training target anyway. This list is the pairings this mod chose, not a claim
        /// that books suit no other proficiency. Note that PE only defines the animal
        /// riding tier when GiddyUp is also installed.
        /// </para>
        /// Values are skill def names. Nothing is resolved here: the def databases are
        /// not populated while static fields initialise, so the first use of a tier goes
        /// through <see cref="trainingTargets"/>.
        /// </summary>
        private static readonly Dictionary<string, string> TrainingTierSubjects = new Dictionary<string, string>
        {
            { "PE_AnimalRidingTier", "Intellectual" },
            { "PE_FirearmTier", "Shooting" },
            { "PE_BasicSpeechTier", "Social" },
            { "PE_FluentSpeechTier", "Social" }
        };

        /// <summary>tier def name → its resolved pairing, including untied tiers as a
        /// null value, so the per-tick hot path does one dictionary read and nothing
        /// else. Entries are session-long: skills and labels do not change in play.</summary>
        private static readonly Dictionary<string, TrainingTarget> trainingTargets = new Dictionary<string, TrainingTarget>();

        /// <summary>PE's own <c>ProficiencyClassLogic.GetLabel</c>, resolved once.</summary>
        private static MethodInfo tierLabelMethod;
        private static bool tierLabelMethodChecked;

        /// <summary>Latched on the first failure inside the training postfix. The cause
        /// is structural (a PE change), so every later tick would fail identically; the
        /// latch keeps a broken bridge from costing an exception per tick per class.</summary>
        private static bool trainingBoostDisabled;

        /// <summary>A resolved tier pairing.</summary>
        private sealed class TrainingTarget
        {
            /// <summary>Skill whose textbooks speed this tier; never null.</summary>
            public SkillDef Skill;

            /// <summary>Tier name as PE displays it, for the readout.</summary>
            public string Label;
        }

        private static bool? installed;
        private static HashSet<JobDef> classJobDefs;
        private static readonly Dictionary<System.Type, FieldInfo> studyGroupFields = new Dictionary<System.Type, FieldInfo>();
        private static readonly Dictionary<System.Type, FieldInfo> classroomFields = new Dictionary<System.Type, FieldInfo>();
        private static readonly Dictionary<System.Type, FieldInfo> subjectLogicFields = new Dictionary<System.Type, FieldInfo>();
        private static readonly Dictionary<System.Type, PropertyInfo> learningBoardProperties = new Dictionary<System.Type, PropertyInfo>();
        private static readonly Dictionary<System.Type, PropertyInfo> skillFocusProperties = new Dictionary<System.Type, PropertyInfo>();
        private static readonly Dictionary<System.Type, FieldInfo> logicStudyGroupFields = new Dictionary<System.Type, FieldInfo>();
        private static readonly Dictionary<System.Type, FieldInfo> targetTierFields = new Dictionary<System.Type, FieldInfo>();

        /// <summary>Distinct failure messages already reported, so a per-frame draw
        /// failure logs once instead of flooding the log.</summary>
        private static readonly HashSet<string> warned = new HashSet<string>();

        // Where the class dialog's right-hand column flow ended this frame, so
        // the readout box can be appended as the column's last row.
        private static float flowBottomY;
        private static bool flowBottomCaptured;

        // Scroll state for the right-hand column we wrap, plus the readout text
        // measured in the prefix so the prefix and finalizer agree on its size.
        private static Vector2 columnScroll;
        private static float columnContentHeight;
        private static string pendingReadoutText;
        private static float pendingReadoutHeight;

        /// <summary>True between a successful BeginScrollView and its matching EndScrollView.
        /// The dialogs are modal and share this state, so the finalizer uses it to decide
        /// whether the scroll view is actually open.</summary>
        private static bool columnScrollOpen;

        /// <summary>
        /// True when the other education mod's assembly is loaded. Safe to query
        /// from a Mod constructor: the game loads every mod assembly before it
        /// instantiates any Mod class.
        /// </summary>
        public static bool Installed
        {
            get
            {
                installed ??= AccessTools.TypeByName(DialogCreateClass) != null;
                return installed.Value;
            }
        }

        /// <summary>True for the other mod's class attendance job defs.</summary>
        public static bool IsClassAttendanceJob(JobDef def)
        {
            if (def == null || !Installed)
            {
                return false;
            }
            if (classJobDefs == null)
            {
                classJobDefs = new HashSet<JobDef>();
                foreach (string name in ClassJobDefNames)
                {
                    JobDef job = DefDatabase<JobDef>.GetNamedSilentFail(name);
                    if (job != null)
                    {
                        classJobDefs.Add(job);
                    }
                }
            }
            return classJobDefs.Contains(def);
        }

        /// <summary>Patches the other mod's class dialogs, if present.</summary>
        public static void ApplyDialogPatches(HarmonyLib.Harmony harmony)
        {
            if (!Installed)
            {
                return;
            }
            // Counted on entry so the success line at the end reports this bridge
            // alone: a warning from the training bridge must not silence it, and a
            // warning from here must not silence the training bridge either.
            int warnedBefore = warned.Count;
            bool patched = false;
            HarmonyMethod beginScroll = new HarmonyMethod(typeof(ProgressionEducationCompat), nameof(BeginClassColumnScroll));
            HarmonyMethod endScrollFinalizer = new HarmonyMethod(typeof(ProgressionEducationCompat), nameof(EndClassColumnScroll));
            HarmonyMethod flowCapture = new HarmonyMethod(typeof(ProgressionEducationCompat), nameof(CaptureClassRowBottom));
            foreach (string typeName in new[] { DialogCreateClass, DialogEditClass })
            {
                System.Type dialogType = AccessTools.TypeByName(typeName);
                // DrawCustomFields draws the whole right-hand column; wrapping it
                // in a scroll view keeps every row (and our readout, appended at
                // the end of the flow) reachable no matter how tall the
                // requirements and study-time blocks grow.
                MethodInfo fields = AccessTools.Method(dialogType, "DrawCustomFields");
                // DrawRequirements ends the variable-height part of the right
                // column; the three fixed rows after it (classroom, class speed,
                // class hours) are where the flow bottoms out.
                MethodInfo requirements = AccessTools.Method(dialogType, "DrawRequirements");
                if (fields == null && requirements == null)
                {
                    LogWarningOnce(KeyDialogBridge + typeName, "class-window readout unavailable on "
                        + typeName + ": it exposes neither DrawCustomFields nor DrawRequirements.");
                    continue;
                }
                try
                {
                    if (fields != null)
                    {
                        harmony.Patch(fields, prefix: beginScroll, finalizer: endScrollFinalizer);
                        patched = true;
                    }
                    if (requirements != null)
                    {
                        harmony.Patch(requirements, postfix: flowCapture);
                    }
                }
                catch (System.Exception e)
                {
                    // Harmony binds injected arguments by parameter name, so a PE
                    // refactor that renames viewRect/curY throws here. This runs in the
                    // Mod constructor: letting it escape would fail the whole mod load,
                    // so the bridge degrades to the state it has when PE is absent.
                    LogWarningOnce(KeyDialogBridge + typeName, "skipped class-window bridge for " + typeName + ": " + e);
                }
            }
            if (patched && warned.Count == warnedBefore)
            {
                Log.Message("[Actually Textbooks] Class window bridge for Progression: Education installed.");
            }
        }

        /// <summary>
        /// Makes the other mod's proficiency classes train faster while the
        /// classroom holds a textbook for the tier's mapped subject. The hook is
        /// the property behind the class's per-tick progress (the teacher's job
        /// adds <c>ProgressPerTick * delta</c> to the semester every tick), so the
        /// boost lands on the real training rate and the class dialog's study-time
        /// estimate — which reads the same property — reflects it for free.
        /// </summary>
        public static void ApplyTrainingPatches(HarmonyLib.Harmony harmony)
        {
            // Found through the training system's own type rather than through
            // Installed, which probes a UI dialog: the two touch points can fail
            // separately, and a dialog rename must not switch off training.
            System.Type logicType = AccessTools.TypeByName(ProficiencyLogicClass);
            if (logicType == null)
            {
                // PE itself is absent — only reachable on a forced or stale mod list,
                // since PE is a listed dependency. No warning: nothing is broken here.
                return;
            }
            MethodInfo progressGetter = AccessTools.PropertyGetter(logicType, "ProgressPerTick");
            if (progressGetter == null)
            {
                // PE is present but the hook moved, which is the likeliest way an update
                // kills this feature. Tiers then train at PE's own speed and every class
                // still works, so the loss is benign — but silent loss of a headline
                // feature leaves the player and any bug report with nothing to go on, and
                // LogWarningOnce already caps this at one line.
                LogWarningOnce(KeyTrainingBridge, "proficiency training boost unavailable: "
                    + ProficiencyLogicClass + ".ProgressPerTick was not found, so textbooks"
                    + " do not speed up proficiency training.");
                return;
            }
            int warnedBefore = warned.Count;
            try
            {
                harmony.Patch(progressGetter, postfix: new HarmonyMethod(
                    typeof(ProgressionEducationCompat), nameof(BoostProficiencyTraining)));
            }
            catch (System.Exception e)
            {
                // Runs in the Mod constructor: an escaping exception would fail the
                // whole mod load, so the bridge degrades to no-boost (see
                // ApplyDialogPatches for the same reasoning).
                LogWarningOnce(KeyTrainingBridge, "skipped proficiency training bridge: " + e);
                return;
            }
            if (warned.Count == warnedBefore)
            {
                Log.Message("[Actually Textbooks] Proficiency training bridge for Progression: Education installed.");
            }
        }

        /// <summary>
        /// Multiplies a proficiency class's per-tick training by the classroom's
        /// textbook bonus for the tier's mapped subject. A class without a
        /// qualified teacher already gets 0, and 0 stays 0: books accelerate
        /// training, never conjure it. The daily XP budget has no meaning for
        /// tier progress, so only the rate is used.
        /// </summary>
        private static void BoostProficiencyTraining(object __instance, ref float __result)
        {
            if (__result <= 0f || trainingBoostDisabled)
            {
                return;
            }
            EducationSettings settings = EducationMod.Settings;
            if (settings == null || !settings.Enabled || !ModsConfig.BiotechActive)
            {
                return;
            }
            try
            {
                SkillDef subject = TrainingSubjectOf(__instance, out _);
                if (subject == null)
                {
                    return;
                }
                Thing board = LearningBoardOf(StudyGroupOfLogic(__instance));
                if (board == null)
                {
                    return;
                }
                if (TextbookBonus.ForLesson(board.GetRoom(), subject, settings, out float rateBonus, out _)
                    && rateBonus > 0f)
                {
                    __result *= 1f + rateBonus;
                }
            }
            catch (System.Exception e)
            {
                // This runs every tick of every proficiency class in progress, and a
                // throw here means PE's shape changed — so it would throw every tick
                // forever. Stop trying: the latch keeps the cost at one exception and
                // one formatted message for the whole session, and the boost was dead
                // anyway. Never let an exception reach the teacher's job driver.
                trainingBoostDisabled = true;
                LogWarningOnce(KeyTrainingBoost, "proficiency training boost disabled after a failure: " + e);
            }
        }

        /// <summary>
        /// Records where the dialog's right-hand column flow ends. The classroom
        /// (+30), class speed (+30) and class hours (25) rows still follow the
        /// requirements block, so the last row's bottom edge is curY + 85.
        /// </summary>
        private static void CaptureClassRowBottom(ref float curY)
        {
            flowBottomY = curY + FixedRowsBelowRequirements;
            flowBottomCaptured = true;
        }

        /// <summary>
        /// Opens a scroll view over the dialog's right-hand column so the column
        /// can grow past the window instead of spilling into the button row. The
        /// content height comes from last frame's measurement, which settles
        /// within a frame since the column only changes when the class does.
        /// <para>
        /// Only the readout measurement is guarded, because it is the only part that
        /// can realistically throw: it walks PE's private object graph by reflection.
        /// <see cref="Widgets.BeginScrollView"/> is left outside that guard on purpose,
        /// so the flag the matching finalizer reads can never disagree with GUI state
        /// — either the scroll view opened and the flag says so, or it never ran.
        /// </para>
        /// </summary>
        private static void BeginClassColumnScroll(Rect viewRect, object __instance)
        {
            // Both dialogs share this static state, so clear it every frame: a value
            // left behind by an aborted dialog would misplace the next one's readout.
            flowBottomCaptured = false;
            columnScrollOpen = false;
            pendingReadoutText = null;
            pendingReadoutHeight = 0f;
            try
            {
                Text.Font = GameFont.Small;
                pendingReadoutText = ReadoutFor(__instance);
                pendingReadoutHeight = pendingReadoutText == null
                    ? 0f
                    : Text.CalcHeight(pendingReadoutText, viewRect.width - 8f) + 8f;
            }
            catch (System.Exception e)
            {
                // Losing the readout must not cost the dialog, and this runs per frame
                // while a class window is open, so the message (which formats the
                // exception, stack trace included) is built for the first failure only.
                pendingReadoutText = null;
                pendingReadoutHeight = 0f;
                if (!AlreadyWarned(KeyReadout))
                {
                    LogWarningOnce(KeyReadout, "class-window readout failed: " + e);
                }
            }
            float contentHeight = Mathf.Max(viewRect.height, columnContentHeight);
            float barWidth = contentHeight > viewRect.height + 1f ? 16f : 0f;
            // The column's rows are drawn at viewRect's own origin, so the content
            // rect shares it; the scroll view then only clips and offsets.
            Rect content = new Rect(viewRect.x, viewRect.y, viewRect.width - barWidth, contentHeight);
            Widgets.BeginScrollView(viewRect, ref columnScroll, content);
            columnScrollOpen = true;
        }

        /// <summary>
        /// Appends the classroom textbook readout as the last row of the column
        /// flow, directly under the class hours row, then closes the scroll view.
        /// Being part of the flow it can never overlap the dialog's own rows, and
        /// being inside the scroll view it stays reachable however tall the
        /// column grows.
        /// <para>
        /// This is a Harmony finalizer rather than a postfix: Harmony skips postfixes
        /// when the original throws, but the scroll view opened in the prefix must
        /// always be closed or the IMGUI stack stays unbalanced for every window
        /// drawn afterwards.
        /// </para>
        /// <para>
        /// Its <see cref="System.Void"/> return type is load-bearing, so do not change
        /// it to <c>Exception</c>: Harmony rethrows a pending original exception only
        /// while every finalizer returns void, whereas a non-void finalizer's return
        /// value <i>replaces</i> that exception — returning null would swallow PE's own
        /// drawing error and hide it from the log. Verified in both shipped Harmony
        /// majors (<c>MethodCreator.AddFinalizers</c> in HarmonyX 2.4.3 for RimWorld
        /// 1.6, <c>MethodCreator.EmitFinalization</c> in HarmonyLib 3.0 for 1.7).
        /// </para>
        /// </summary>
        private static void EndClassColumnScroll(Rect viewRect, System.Exception __exception)
        {
            if (!columnScrollOpen)
            {
                return;
            }
            columnScrollOpen = false;
            try
            {
                Text.Font = GameFont.Small;
                float bottom = viewRect.y;
                // Skip the readout when the dialog's own drawing threw: the flow
                // position is then unknown and the column may be half drawn.
                if (__exception == null && flowBottomCaptured)
                {
                    bottom = flowBottomY;
                    if (pendingReadoutText != null)
                    {
                        Rect box = new Rect(viewRect.x, bottom + 4f, viewRect.width, pendingReadoutHeight);
                        Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.25f));
                        Widgets.Label(box.ContractedBy(4f), pendingReadoutText);
                        bottom = box.yMax;
                    }
                }
                flowBottomCaptured = false;
                columnContentHeight = Mathf.Max(viewRect.height, bottom + 4f - viewRect.y);
            }
            finally
            {
                // Guarded so a failure to close cannot replace an original exception
                // from inside a finally block: the window stack is then left to log the
                // real problem, and this mod has still reported its own part of it.
                try
                {
                    Widgets.EndScrollView();
                }
                catch (System.Exception e)
                {
                    if (!AlreadyWarned(KeyScrollBalance))
                    {
                        LogWarningOnce(KeyScrollBalance, "could not close the class-window scroll view: " + e);
                    }
                }
            }
        }

        /// <summary>True once this failure kind has been reported, so a caller on a
        /// per-frame or per-tick path can skip building the message.</summary>
        private static bool AlreadyWarned(string key) => warned.Contains(key);

        /// <summary>
        /// Reports one kind of bridge failure, once per session. <paramref name="key"/>
        /// identifies the kind (see the Key* constants) and <paramref name="detail"/>
        /// carries the message for that first report.
        /// </summary>
        private static void LogWarningOnce(string key, string detail)
        {
            if (warned.Add(key))
            {
                Log.Warning("[Actually Textbooks] Progression: Education bridge degraded — " + detail);
            }
        }

        /// <summary>
        /// Walks dialog → studyGroup → classroom → learning board → room without
        /// compile-time references, then reuses the shared readout builder.
        /// </summary>
        private static string ReadoutFor(object dialog)
        {
            if (dialog == null)
            {
                return null;
            }
            System.Type dialogType = dialog.GetType();
            if (!studyGroupFields.TryGetValue(dialogType, out FieldInfo studyGroupField))
            {
                studyGroupField = AccessTools.Field(dialogType, "studyGroup");
                studyGroupFields.Add(dialogType, studyGroupField);
            }
            object studyGroup = studyGroupField?.GetValue(dialog);
            if (studyGroup == null)
            {
                return null;
            }
            Thing board = LearningBoardOf(studyGroup);
            if (board == null)
            {
                return null;
            }
            // Skill classes grant XP through SkillRecord.Learn and proficiency
            // classes raise ProgressPerTick, the two hooks the textbook bonuses ride
            // on, so both report. A skill class names the taught skill and quotes its
            // lesson figures. A paired proficiency tier instead names the tier itself
            // ("Firearm training rate +30%") and quotes the rate only: it matches
            // books by the paired skill, and the daily XP budget means nothing for
            // tier progress. Untied tiers and other subjects (daycare) take no bonus
            // at all, so they keep showing no readout.
            object logic = LogicOf(studyGroup);
            SkillDef subject = SkillFocusOf(logic);
            string trainingLabel = null;
            if (subject == null)
            {
                subject = TrainingSubjectOf(logic, out trainingLabel);
                if (subject == null)
                {
                    return null;
                }
            }
            return TextbookRoomSummary.For(board.GetRoom(), board.Map, subject, trainingLabel);
        }

        /// <summary>The class's subjectLogic, or null while the class has none.</summary>
        private static object LogicOf(object studyGroup)
        {
            if (studyGroup == null)
            {
                return null;
            }
            System.Type studyGroupType = studyGroup.GetType();
            if (!subjectLogicFields.TryGetValue(studyGroupType, out FieldInfo subjectLogicField))
            {
                subjectLogicField = AccessTools.Field(studyGroupType, "subjectLogic");
                subjectLogicFields.Add(studyGroupType, subjectLogicField);
            }
            return subjectLogicField?.GetValue(studyGroup);
        }

        /// <summary>The skill a skill class teaches, or null for other subjects.</summary>
        private static SkillDef SkillFocusOf(object logic)
        {
            if (logic == null)
            {
                return null;
            }
            System.Type logicType = logic.GetType();
            if (!skillFocusProperties.TryGetValue(logicType, out PropertyInfo skillFocus))
            {
                skillFocus = AccessTools.Property(logicType, "SkillFocus");
                skillFocusProperties.Add(logicType, skillFocus);
            }
            return skillFocus?.GetValue(logic, null) as SkillDef;
        }

        /// <summary>The classroom's learning board building, or null while the
        /// class has no classroom or the board cannot be resolved.</summary>
        private static Thing LearningBoardOf(object studyGroup)
        {
            if (studyGroup == null)
            {
                return null;
            }
            System.Type studyGroupType = studyGroup.GetType();
            if (!classroomFields.TryGetValue(studyGroupType, out FieldInfo classroomField))
            {
                classroomField = AccessTools.Field(studyGroupType, "classroom");
                classroomFields.Add(studyGroupType, classroomField);
            }
            object classroom = classroomField?.GetValue(studyGroup);
            if (classroom == null)
            {
                return null;
            }
            // LearningBoard is a CompLearningBoard (a ThingComp), not a Thing; the
            // room anchor is the comp's parent building.
            System.Type classroomType = classroom.GetType();
            if (!learningBoardProperties.TryGetValue(classroomType, out PropertyInfo learningBoardProperty))
            {
                learningBoardProperty = AccessTools.Property(classroomType, "LearningBoard");
                learningBoardProperties.Add(classroomType, learningBoardProperty);
            }
            ThingComp boardComp = learningBoardProperty?.GetValue(classroom, null) as ThingComp;
            return boardComp?.parent;
        }

        /// <summary>The study group a class subject's logic belongs to.</summary>
        private static object StudyGroupOfLogic(object logic)
        {
            if (logic == null)
            {
                return null;
            }
            System.Type logicType = logic.GetType();
            if (!logicStudyGroupFields.TryGetValue(logicType, out FieldInfo studyGroupField))
            {
                // "studyGroup" is declared on PE's shared base ClassSubjectLogic,
                // so walk the ancestors explicitly instead of trusting any particular
                // helper's hierarchy behaviour.
                for (System.Type t = logicType; studyGroupField == null && t != null && t != typeof(object); t = t.BaseType)
                {
                    studyGroupField = AccessTools.DeclaredField(t, "studyGroup");
                }
                logicStudyGroupFields.Add(logicType, studyGroupField);
            }
            return studyGroupField?.GetValue(logic);
        }

        /// <summary>
        /// The vanilla skill whose textbooks speed a proficiency tier's training,
        /// with the tier's display name for the readout; null for every subject that
        /// is not a paired tier (skill and daycare classes included — their logic
        /// has no target tier field at all).
        /// <para>
        /// Hot path: per tick per active proficiency class, and per frame while a
        /// class dialog draws. Everything therefore resolves once per tier into
        /// <see cref="trainingTargets"/>, untied tiers included, so a repeat call is
        /// one field read and one dictionary read — no def lookup, no reflection
        /// beyond the cached field, no label string.
        /// </para>
        /// </summary>
        private static SkillDef TrainingSubjectOf(object logic, out string trainingLabel)
        {
            trainingLabel = null;
            Def tier = TargetTierOf(logic);
            if (tier == null)
            {
                return null;
            }
            if (!trainingTargets.TryGetValue(tier.defName, out TrainingTarget target))
            {
                target = ResolveTrainingTarget(tier);
                trainingTargets.Add(tier.defName, target);
            }
            if (target == null)
            {
                return null;
            }
            trainingLabel = target.Label;
            return target.Skill;
        }

        /// <summary>The one-time work behind <see cref="TrainingSubjectOf"/>; null
        /// caches an untied tier so it is never looked up again.</summary>
        private static TrainingTarget ResolveTrainingTarget(Def tier)
        {
            if (!TrainingTierSubjects.TryGetValue(tier.defName, out string subjectName))
            {
                return null;
            }
            SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(subjectName);
            if (skill == null)
            {
                LogWarningOnce(KeyTrainingBridge, "dropped the " + tier.defName + " pairing: no skill named "
                    + subjectName + ".");
                return null;
            }
            return new TrainingTarget
            {
                Skill = skill,
                Label = TierLabelOf(tier) ?? tier.label?.CapitalizeFirst()
            };
        }

        /// <summary>
        /// The tier's name the way PE shows it. PE's own
        /// <c>ProficiencyClassLogic.GetLabel</c> prefers the proficiency trait's degree
        /// label — "firearm proficient" for the firearm tier, where the tier def's own
        /// label reads just "firearm". The readout is drawn inside PE's dialog, next to
        /// PE's wording for the same tier, so it calls that method rather than keeping a
        /// copy of its rule. Falls back to the tier def's label when the method is gone
        /// or returns nothing.
        /// </summary>
        private static string TierLabelOf(Def tier)
        {
            try
            {
                if (!tierLabelMethodChecked)
                {
                    tierLabelMethodChecked = true;
                    System.Type logicType = AccessTools.TypeByName(ProficiencyLogicClass);
                    tierLabelMethod = logicType == null ? null : AccessTools.Method(logicType, "GetLabel");
                }
                if (tierLabelMethod == null)
                {
                    return null;
                }
                // PE's GetLabel takes its own ProficiencyTierDef; passing the tier as
                // the base Def is fine because the instance is that type.
                string label = tierLabelMethod.Invoke(null, new object[] { tier }) as string;
                return string.IsNullOrEmpty(label) ? null : label.CapitalizeFirst();
            }
            catch (System.Exception e)
            {
                LogWarningOnce(KeyTierLabel, "could not read PE's tier label, using the def label instead: "
                    + e.Message);
                return null;
            }
        }

        /// <summary>The class's target tier as a plain <see cref="Def"/> (its label
        /// and def name are what the bridge needs), or null for subject logics
        /// without a proficiency tier.</summary>
        private static Def TargetTierOf(object logic)
        {
            if (logic == null)
            {
                return null;
            }
            System.Type logicType = logic.GetType();
            if (!targetTierFields.TryGetValue(logicType, out FieldInfo targetTierField))
            {
                targetTierField = AccessTools.Field(logicType, "targetTier");
                targetTierFields.Add(logicType, targetTierField);
            }
            return targetTierField?.GetValue(logic) as Def;
        }
    }
}
