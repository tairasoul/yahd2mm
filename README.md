# yahd2mm

you ever just write an entire mod manager because youre not on windows and the only linux one doesnt have features youd like

no?

okay well i did (and it took surprisingly little time)

should be compatible with Arsenal mods, manifest-less mods and mods that extract to a single file

uses ImGui.NET and Veldrid.SDL2 for rendering (the `Shaders` and `veldrid` directories are from Veldrid.ImGui and Veldrid.ImageSharp)

## notes

will always prompt for nexus api key regardless of if you plan to use nxm integration

uses symlinks on linux, copies files on windows (havent tested symlinks on windows yet)

should work with most archive types, if one doesn't work extract it manually and put the resulting folder into the local files folder (make a folder for the mod if it's single-file)

setting this as your nxm handler will let you use this to download mods off nexus (might be buggy, requests might freeze sometimes)

you do not need to have this open in the background for this, it'll open the manager automatically when it receives one without a running instance

multi-part download mods do not work currently (and i'm unsure how to make them work), if need be download parts manually and put the archives in the local files directory

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

### Priorities

the priority list for all mods

mods that are active have (X) at the beginning, mods that are inactive have ( )

drag and drop to change priority

click re-deploy to quickly re-deploy with priority list active

### Downloads

tab for the nxm handler

any mods being downloaded are here

no cancelling/pausing (lazy :3)

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

more nexus integration (properly get mod version from nexus, check for updates, show author)

implement cancelling/pausing for downloads

implement refreshing for installing mods manually to the mods folder