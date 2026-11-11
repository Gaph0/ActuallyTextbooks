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
    /// scheduled classes held in classrooms. That mod is a listed dependency, but
    /// every member of it is still reached by name at run time, so all three touch
    /// points degrade to inert (with one warning in the log) when it is missing or
    /// changes its internals:
    /// <list type="bullet">
    /// <item>its class attendance jobs grant skill XP through
    /// <c>SkillRecord.Learn</c>, the same choke point vanilla lessons use, so
    /// those lessons receive the textbook bonus too;</item>
    /// <item>its create/edit class dialogs list the classroom textbook readout
    /// at the bottom of the right-hand column;</item>
    /// <item>its proficiency classes train faster: a postfix on the class's
    /// per-tick progress multiplies it by the classroom's textbook bonus for
    /// the tier's mapped subject (see <see cref="TrainingTierSubjects"/>).</item>
    /// </list>
    /// </summary>
    public static class ProgressionEducationCompat
    {
        private const string DialogCreateClass = "ProgressionEducation.Dialog_CreateClass";
        private const string DialogEditClass = "ProgressionEducation.Dialog_EditClass";
        private const string ProficiencyLogicClass = "ProgressionEducation.ProficiencyClassLogic";

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
        /// whose textbooks speed that tier's training: intellectual books help
        /// animal riding, shooting books help firearms, social books help speech
        /// (basic and fluent). Keyed by def name, so a PE rename simply drops the
        /// mapping and that tier trains at its own speed. Unmapped tiers (low-tech,
        /// high-tech, driving, piloting) take no bonus by design — these are the
        /// pairings this mod chose, not a claim the other book subjects fit nowhere.
        /// Values are skill def names, resolved at use, because the def databases
        /// are not populated while static fields initialise.
        /// </summary>
        private static readonly Dictionary<string, string> TrainingTierSubjects = new Dictionary<string, string>
        {
            { "PE_AnimalRidingTier", "Intellectual" },
            { "PE_FirearmTier", "Shooting" },
            { "PE_BasicSpeechTier", "Social" },
            { "PE_FluentSpeechTier", "Social" }
        };

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
                    continue;
                }
                try
                {
                    if (fields != null)
                    {
                        harmony.Patch(fields, prefix: beginScroll, finalizer: endScrollFinalizer);
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
                    LogWarningOnce("skipped class-window bridge for " + typeName + ": " + e);
                }
            }
            if (warned.Count == 0)
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
            if (!Installed)
            {
                return;
            }
            System.Type logicType = AccessTools.TypeByName(ProficiencyLogicClass);
            MethodInfo progressGetter = logicType == null
                ? null
                : AccessTools.PropertyGetter(logicType, "ProgressPerTick");
            if (progressGetter == null)
            {
                // A PE rename or merge of the training-rate property costs only the
                // boost: tiers train at PE's own speed and every class still works,
                // so this degrades silently rather than warning on every load.
                return;
            }
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
                LogWarningOnce("skipped proficiency training bridge: " + e);
            }
            if (warned.Count == 0)
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
            if (__result <= 0f)
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
                // This runs every tick of every proficiency class in progress:
                // report once and never throw into the teacher's job driver.
                LogWarningOnce("proficiency training boost failed: " + e);
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
        /// Never throws: the matching finalizer balances the scroll view only when
        /// <see cref="columnScrollOpen"/> says this prefix opened one.
        /// </para>
        /// </summary>
        private static void BeginClassColumnScroll(Rect viewRect, object __instance)
        {
            // Both dialogs share this static state, so clear it every frame: a value
            // left behind by an aborted dialog would misplace the next one's readout.
            flowBottomCaptured = false;
            columnScrollOpen = false;
            try
            {
                Text.Font = GameFont.Small;
                pendingReadoutText = ReadoutFor(__instance);
                pendingReadoutHeight = pendingReadoutText == null
                    ? 0f
                    : Text.CalcHeight(pendingReadoutText, viewRect.width - 8f) + 8f;
                float contentHeight = Mathf.Max(viewRect.height, columnContentHeight);
                float barWidth = contentHeight > viewRect.height + 1f ? 16f : 0f;
                // The column's rows are drawn at viewRect's own origin, so the
                // content rect shares it; the scroll view then only clips and offsets.
                Rect content = new Rect(viewRect.x, viewRect.y, viewRect.width - barWidth, contentHeight);
                Widgets.BeginScrollView(viewRect, ref columnScroll, content);
                columnScrollOpen = true;
            }
            catch (System.Exception e)
            {
                // ReadoutFor walks PE's private object graph by reflection; a PE
                // change can throw here, but that must cost the readout only, not the
                // dialog or log once per frame.
                LogWarningOnce("class-window readout failed: " + e);
            }
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
                Widgets.EndScrollView();
            }
        }

        /// <summary>Reports a bridge failure once per distinct message.</summary>
        private static void LogWarningOnce(string detail)
        {
            if (warned.Add(detail))
            {
                Log.Warning("[Actually Textbooks] Progression: Education bridge degraded — " + detail);
            }
        }

        /// <summary>
        /// Walks dialog → studyGroup → classroom → learning board → room without
        /// compile-time references, then reuses the desk inspect readout.
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
            // on, so both report. Subject classes use the taught skill; a mapped
            // proficiency tier reports under its own skill with the tier's label
            // ("firearm training rate +30%") instead of the XP budget line, which
            // means nothing for tier progress. Other subjects (daycare) take no
            // bonus at all, so they keep showing no readout.
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
        /// with the tier's own label for the readout; null for every subject that
        /// is not a mapped tier (skill and daycare classes included — their logic
        /// has no target tier field at all).
        /// </summary>
        private static SkillDef TrainingSubjectOf(object logic, out string trainingLabel)
        {
            trainingLabel = null;
            Def tier = TargetTierOf(logic);
            if (tier == null || !TrainingTierSubjects.TryGetValue(tier.defName, out string subjectName))
            {
                return null;
            }
            SkillDef subject = DefDatabase<SkillDef>.GetNamedSilentFail(subjectName);
            if (subject == null)
            {
                return null;
            }
            trainingLabel = tier.label.CapitalizeFirst();
            return subject;
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
