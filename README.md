# yahd2mm

you ever just write an entire mod manager because youre not on windows and the only linux one doesnt have features youd like

no?

okay well i did (and it took surprisingly little time)

should be compatible with Arsenal mods, manifest-less mods and mods that extract to a single file

uses ImGui.NET and Veldrid.SDL2 for rendering (the `Shaders` and `veldrid` directories are from Veldrid.ImGui and Veldrid.ImageSharp)

## notes

will always prompt for nexus api key regardless of if you plan to use nxm integration

uses symlinks on linux, hardlinks files on windows (will use symlinks if data directory is not on C: drive, prompts for admin if needed)

should work with most archive types, if one doesn't work extract it manually and put the resulting folder into the local files folder (make a folder for the mod if it's single-file)

setting this as your nxm handler will let you use this to download mods off nexus (might be buggy, requests might freeze sometimes)

you do not need to have this open in the background for this, it'll open the manager automatically when it receives one without a running instance

multi-part download mods now work, although i cannot offer as much assistance as with regular mods
  - optional files are not guaranteed to always have the same name, as are main files

in exchange for being able to do ^, you'll have to manage the mods a bit more manually and uninstall old versions of a mod if the file name changes

## tabs

### Mod List

a list of all installed mods.

can enable all, disable all, purge or re-deploy

enabling/disabling a mod will automatically deploy it or purge it

can search for mods (only based on name)

when enabling/disabling a mod here, priority list is ignored (lazy)

sorted alphabetically when not searching

mods can be given aliases (used for sorting)

should function properly with various manifest.json styles

hovering over a mod with an icon will show that icon as large as it can while preserving aspect ratio and keeping it onscreen

same goes for hovering over the icon itself when expanded

previous two apply to options and sub options too

if two or more files from the same mod are downloaded, they'll be under a header with the mod name instead of two or more headers with the mod file names

downloading mod file A and B from mod C will cause there to be a header C with two headers A and B in it, as opposed to two loose headers A and B

### Priorities

the priority list for all mods

mods that are active have (X) at the beginning, mods that are inactive have ( )

drag and drop to change priority

click re-deploy to quickly re-deploy with priority list active

### Downloads

tab for the nxm handler

any mods being downloaded are here

can cancel/pause a download

downloads should persist correctly when you relaunch the manager (or if it crashes)

### Completed Downloads

all completed downloads from the Downloads tab are here

click Clear to clear list

### Modpacks

modpacks are currently local-only (primarily because i don't know if itd be possible to export to something other managers work with)

modpacks are not saved with a snapshot of everything active and the options, it's just a list of mod names and the option paths active on them

### Local Files

this tab is for files that do not let you download with a mod manager

put any compressed archives in here and you can install them directly

also works with already-extracted mod folders, will move them instead (because c# doesnt let you do Directory.Copy for some godforsaken reason)

## todo

properly deploy with priorities when enabling/disabling in mod list (might not be feasible without a more advanced filesystem abstraction)

installer and updater

general usability improvements (priority list preview, drag and drop on empty space to move mod to last position, etc.)

maybe try to add support for reshade mods?
  - could be interesting to try

## things missing

checking for nexus mod updates
  - isn't feasible with how most mods are setup, versions can be entirely different from what a SemVer-based updater would expect
  
  - file names aren't guaranteed to be consistent, some mods may have an old main file alongside a new main file

  - optional files may just entirely change name for what's supposed to be the same file

  - in general would require a standard only something like arsenal or hd2mm can enforce