# Improvement: Open Mod Archives as Extracted Folders

## Summary

Currently mesh archives (`MeshArchives`) and texture archives (`TextureArchives`) in a project file must be `.rfa` files. This improvement would also allow pointing them at already-extracted folder trees, removing the need to keep the original `.rfa` files around once a mod has been unpacked.

## Motivation

Modders often work with already-extracted content (e.g. unpacked from a BF1942/Vietnam mod) stored in a folder hierarchy rather than a compressed archive. Today this content is inaccessible without re-packing it into an `.rfa`, which is an unnecessary step.

The same need applies to the level archives (`LevelArchives`): currently addressed by the new **Open Level Folder** flow (added alongside this doc), which creates a project with `ProjectFile.Level` pointing at the folder. Mesh/texture archives need an analogous solution.

## Scope of change

### ProjectFile

`MeshArchives` and `TextureArchives` are `string[]` arrays. The entries are currently assumed to be `.rfa` paths. Making them accept folder paths is a backward-compatible extension: any entry where `Directory.Exists(entry)` is true is treated as a folder tree; otherwise it is treated as an `.rfa`.

### RFA loading pipeline

The `.rfa` loader (`RefractorForge.Formats.Rfa`) opens an archive by path. A thin adapter is needed that, given a folder root, enumerates files and serves them the same way `LevelArchive.Open` / the mesh/texture lookup code does today. The key interface points to adapt:

- **Mesh resolution**: wherever `MeshArchives` entries are opened to look up `.con`/`.staticmesh`/`.skinnedmesh` files.
- **Texture resolution**: wherever `TextureArchives` entries are opened to look up `.dds`/`.tga` files.
- **The `ArchiveListWidget` helper** in the editor UI: currently its file picker filters for `*.rfa`. A folder-browse option should be added (a second button "Add Folder…") so users can append a folder path to the list.

### UI changes

1. **Archive list widget** (`ArchiveListWidget` in Program.cs): add an "Add Folder…" button alongside the existing "Add .rfa…" button. Folder entries display with a folder icon prefix (e.g. `[dir] C:\path\to\mod`) to distinguish them visually.
2. **Open Level RFA modal** and **Open Level Folder modal**: the mesh/texture archive sections already use `ArchiveListWidget`, so they get the folder option for free once the widget is updated.
3. **Project Settings modal**: same widget, same free update.

### Backward compatibility

Existing `.rfproj` files continue to work without changes — all their archive entries are `.rfa` paths, which the updated loader handles as before. New projects can mix `.rfa` and folder entries freely.

## Estimated complexity

**Medium.** The primary work is the folder-backed archive adapter in the Formats layer. The UI change is small (one button per widget instance). No schema changes to `ProjectFile` are needed.
