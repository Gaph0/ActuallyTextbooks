using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActuallyTextbooks
{
    /// <summary>
    /// Optional bridge to the workshop mod "Progression: Education"
    /// (ferny.ProgressionEducation), which replaces free-form lessons with
    /// scheduled classes held in classrooms. Two touch points, both inert while
    /// that mod is absent:
    /// <list type="bullet">
    /// <item>its class attendance jobs grant skill XP through
    /// <c>SkillRecord.Learn</c>, the same choke point vanilla lessons use, so
    /// those lessons receive the textbook bonus too;</item>
    /// <item>its create/edit class dialogs list the classroom textbook readout
    /// at the bottom of the right-hand column.</item>
    /// </list>
    /// Every member of that mod is reached by name at run time, so this mod still
    /// loads and works without it.
    /// </summary>
    public static class ProgressionEducationCompat
    {
        private const string DialogCreateClass = "ProgressionEducation.Dialog_CreateClass";
        private const string DialogEditClass = "ProgressionEducation.Dialog_EditClass";

        private static readonly string[] ClassJobDefNames =
        {
            "PE_AttendClass",
            "PE_AttendMeleeClass",
            "PE_AttendShootingClass"
        };

        private static bool? installed;
        private static HashSet<JobDef> classJobDefs;
        private static readonly Dictionary<System.Type, FieldInfo> studyGroupFields = new Dictionary<System.Type, FieldInfo>();
        private static FieldInfo classroomField;
        private static FieldInfo subjectLogicField;
        private static PropertyInfo learningBoardProperty;
        private static readonly Dictionary<System.Type, PropertyInfo> skillFocusProperties = new Dictionary<System.Type, PropertyInfo>();

        // Where the class dialog's right-hand column flow ended this frame, so
        // the readout box can be appended as the column's last row.
        private static float flowBottomY;
        private static bool flowBottomCaptured;

        // Scroll state for the right-hand column we wrap, plus the readout text
        // measured in the prefix so the prefix and postfix agree on its size.
        private static Vector2 columnScroll;
        private static float columnContentHeight;
        private static string pendingReadoutText;
        private static float pendingReadoutHeight;

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
            HarmonyMethod endScroll = new HarmonyMethod(typeof(ProgressionEducationCompat), nameof(EndClassColumnScroll));
            HarmonyMethod flowCapture = new HarmonyMethod(typeof(ProgressionEducationCompat), nameof(CaptureClassRowBottom));
            foreach (string typeName in new[] { DialogCreateClass, DialogEditClass })
            {
                System.Type dialogType = AccessTools.TypeByName(typeName);
                // DrawCustomFields draws the whole right-hand column; wrapping it
                // in a scroll view keeps every row (and our readout, appended at
                // the end of the flow) reachable no matter how tall the
                // requirements and study-time blocks grow.
                MethodInfo fields = AccessTools.Method(dialogType, "DrawCustomFields");
                if (fields != null)
                {
                    harmony.Patch(fields, prefix: beginScroll, postfix: endScroll);
                }
                // DrawRequirements ends the variable-height part of the right
                // column; the three fixed rows after it (classroom, class speed,
                // class hours) are where the flow bottoms out.
                MethodInfo requirements = AccessTools.Method(dialogType, "DrawRequirements");
                if (requirements != null)
                {
                    harmony.Patch(requirements, postfix: flowCapture);
                }
            }
            Log.Message("[Actually Textbooks] Class window bridge for Progression: Education installed.");
        }

        /// <summary>
        /// Records where the dialog's right-hand column flow ends. The classroom
        /// (+30), class speed (+30) and class hours (25) rows still follow the
        /// requirements block, so the last row's bottom edge is curY + 85.
        /// </summary>
        private static void CaptureClassRowBottom(ref float curY)
        {
            flowBottomY = curY + 85f;
            flowBottomCaptured = true;
        }

        /// <summary>
        /// Opens a scroll view over the dialog's right-hand column so the column
        /// can grow past the window instead of spilling into the button row. The
        /// content height comes from last frame's measurement, which settles
        /// within a frame since the column only changes when the class does.
        /// </summary>
        private static void BeginClassColumnScroll(Rect viewRect, object __instance)
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
        }

        /// <summary>
        /// Appends the classroom textbook readout as the last row of the column
        /// flow, directly under the class hours row, then closes the scroll view.
        /// Being part of the flow it can never overlap the dialog's own rows, and
        /// being inside the scroll view it stays reachable however tall the
        /// column grows.
        /// </summary>
        private static void EndClassColumnScroll(Rect viewRect)
        {
            Text.Font = GameFont.Small;
            float bottom = viewRect.y;
            if (flowBottomCaptured)
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
            Widgets.EndScrollView();
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
            classroomField ??= AccessTools.Field(studyGroup.GetType(), "classroom");
            object classroom = classroomField?.GetValue(studyGroup);
            if (classroom == null)
            {
                return null;
            }
            // LearningBoard is a CompLearningBoard (a ThingComp), not a Thing; the
            // room anchor is the comp's parent building.
            learningBoardProperty ??= AccessTools.Property(classroom.GetType(), "LearningBoard");
            ThingComp boardComp = learningBoardProperty?.GetValue(classroom, null) as ThingComp;
            Thing board = boardComp?.parent;
            if (board == null)
            {
                return null;
            }
            // Only skill classes grant XP through SkillRecord.Learn, the hook the
            // textbook bonus rides on. Proficiency tracks (speech, weapons, vehicles)
            // progress through their own tiers, so textbooks cannot help them and
            // the readout stays hidden.
            SkillDef subject = SubjectFocusOf(studyGroup);
            if (subject == null)
            {
                return null;
            }
            return TextbookRoomSummary.For(board.GetRoom(), board.Map, subject);
        }

        /// <summary>The skill a class teaches, or null for non-skill subjects.</summary>
        private static SkillDef SubjectFocusOf(object studyGroup)
        {
            subjectLogicField ??= AccessTools.Field(studyGroup.GetType(), "subjectLogic");
            object logic = subjectLogicField?.GetValue(studyGroup);
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
    }
}
