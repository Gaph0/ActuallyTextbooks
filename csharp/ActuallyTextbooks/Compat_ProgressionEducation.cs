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
            HarmonyMethod postfix = new HarmonyMethod(typeof(ProgressionEducationCompat), nameof(DrawClassWindowReadout));
            foreach (string typeName in new[] { DialogCreateClass, DialogEditClass })
            {
                MethodInfo target = AccessTools.Method(AccessTools.TypeByName(typeName), "DoWindowContents");
                if (target != null)
                {
                    harmony.Patch(target, postfix: postfix);
                }
            }
            Log.Message("[Actually Textbooks] Class window bridge for Progression: Education installed.");
        }

        /// <summary>
        /// Draws the classroom textbook readout into the free space at the bottom
        /// of the dialog's right-hand column, under the class hours row.
        /// </summary>
        private static void DrawClassWindowReadout(Rect inRect, object __instance)
        {
            string text = ReadoutFor(__instance);
            if (text == null)
            {
                return;
            }
            Text.Font = GameFont.Small;
            float rightX = inRect.x + inRect.width * 0.5f + 5f;
            float rightWidth = inRect.width * 0.5f - 5f;
            float bottom = inRect.y + inRect.height - 35f - 6f;
            float height = Mathf.Min(Text.CalcHeight(text, rightWidth - 12f) + 12f, 240f);
            Rect box = new Rect(rightX, bottom - height, rightWidth, height);
            Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.25f));
            Widgets.Label(box.ContractedBy(6f), text);
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
