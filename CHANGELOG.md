# Changelog

Newest entry first. Dates use ISO format.

## Unreleased

### Changed

- The right-hand column of the create/edit class windows of "Progression: Education" now scrolls when its requirements and study-time rows outgrow the window, so rows no longer spill into the button row. The classroom textbook readout is appended as the column's last row, directly under the class hours row, and stays reachable however tall the column grows.
- With subject matching on, the classroom readout's book count and quality now cover only the counted textbooks printed with the taught skill, so unrelated books on the shelf no longer inflate the number the class sees. When nothing on the shelf covers the taught skill, only the "no textbook covers it" line shows; subjects the class does not learn are never listed.

### Fixed

- The mod settings window could no longer be closed from the main menu: closing it wrote settings, which invalidated the room caches via `Find.Maps`, which is null before a game is loaded, so the close threw and the window stayed open. The cache invalidation now no-ops when no game is loaded.

## 1.0.0 — 2026-11-08

First release. Supports RimWorld 1.6 and 1.7. Requires Biotech and Harmony.

### Added

- Textbooks shelved in a classroom raise the lesson XP rate and the per-skill daily XP budget of students taught there. Quality decides the size of the bonus; only the best few books per room count, with diminishing returns.
- Subject matching: a textbook helps only the skills printed on it, unless matching is turned off in the settings.
- Settings window with toggles, sliders, a quality reference table and an opt-in debug logger.
- The textbook benefit readout appears in the Create-a-class and Edit-class windows of the workshop mod "Progression: Education" (ferny.ProgressionEducation), filtered to the subject the class teaches. Proficiency classes show no readout, because their progress does not use skill XP.
- Class attendance lessons of that mod (PE_AttendClass, PE_AttendMeleeClass, PE_AttendShootingClass) receive the textbook bonuses when it is installed.
- A dev-mode gizmo on school desks that spawns a textbook of a chosen quality.
- RimWorld 1.6 support: the project builds one dll per game version (net48 for 1.6, net10.0 for 1.7) and LoadFolders.xml maps each version to its folder.

### Changed

- Renamed the mod from "Progression: Education" to "Actually Textbooks", because the old name was too close to the published mod ferny.ProgressionEducation. The packageId and Harmony id are now gapho.actuallytextbooks; the namespace, assembly and translation keys (ActTx_*) follow.
- Added ferny.ProgressionEducation to loadAfter in About.xml, so this mod sorts after it when both mods are active.

### Fixed

- The class window readout resolves the classroom learning board through its thing comp, so the room lookup works.

### Removed

- The textbook readout on the school desk inspect pane. The class windows carry that information now. The desk keeps only its dev-mode spawn gizmo.

### Note for testers

- The settings file key changed with the packageId during development. Old mod settings do not carry over. The mod stores no save-game data, so existing saves load unchanged.
