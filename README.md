# QuickLook 3MF Viewer

A [QuickLook](https://github.com/QL-Win/QuickLook) plugin that previews `.3mf` files when you press the space bar in Windows Explorer.

QuickLook already lists `.3mf` in its built-in `HelixViewer` plugin, but it cannot show files written by current slicers. Assimp does not follow objects stored in separate model files (`p:path`, from the 3MF Production extension). On top of that, the loading code falls through to the HelixToolkit importer, which does not support 3MF. This plugin has a higher priority (10 vs -5), so it handles `.3mf` files instead.

## Features

- Reads the geometry itself (ZIP + streaming XML): objects, components, `p:path`, `build/item` transforms and units.
- Colours:
  - **Bambu Studio / OrcaSlicer**: filament colours (`project_settings.config`), the filament of each object or part (`model_settings.config`) and per-triangle multi-material painting (`paint_color`).
  - **PrusaSlicer**: extruder and filament colours (`Slic3r_PE.config`), per-extruder volumes (`Slic3r_PE_model.config`) and MMU painting (`slic3rpe:mmu_segmentation`).
  - **Standard 3MF**: `basematerials` and `colorgroup`.
- Leaves out modifiers, negative volumes, and support blockers and enforcers.
- Shows the image saved by the slicer (one per plate in Bambu/Orca) while the 3D model loads, and lets you switch between **3D** and **Image**.
- Controls: drag to rotate, right-drag (or Shift + drag) to pan, mouse wheel to zoom, double-click to recenter.
- UI text in Spanish or English, following the Windows language.

## Build and install

Requirements: the .NET SDK (6 or later) and QuickLook installed in `%LOCALAPPDATA%\Programs\QuickLook` (if it is somewhere else, pass `-QuickLookDir`).

```powershell
.\build.ps1            # builds and creates dist\QuickLook.Plugin.ThreeMFViewer.qlplugin
.\build.ps1 -Install   # also copies it to the plugin folder and restarts QuickLook
```

To install the `.qlplugin` by hand, select it in Explorer, press space, then click **Install**. Restart QuickLook afterwards.

## Testing without QuickLook

`tools/RenderTest` loads files with the same code as the plugin, prints statistics (objects, triangles, colours, timings) and saves what the viewer would show as a PNG:

```powershell
dotnet run --project tools\RenderTest -c Release -- <output folder> file1.3mf file2.3mf
```

## Project layout

| File | Purpose |
| --- | --- |
| `Plugin.cs` | QuickLook entry point (`IViewer`). |
| `ViewerPanel.xaml(.cs)` | 3D view, per-plate images, info bar and mouse controls. |
| `ThreeMF/ThreeMFReader.cs` | Reads the package: relationships, model files, components, images. |
| `ThreeMF/MeshBuilder.cs` | Builds each object's mesh and works out the colour of every triangle. |
| `ThreeMF/SlicerConfig.cs` | Bambu/Orca and PrusaSlicer project data. |
| `ThreeMF/PaintDecoder.cs` | Decodes per-triangle multi-material painting. |
| `Rendering/SceneBuilder.cs` | Turns the scene into a frozen WPF model (built on a background thread). |
| `Rendering/OrbitCamera.cs` | Orbit camera with Z up, like a slicer's print bed. |

## License

[MIT](LICENSE) © 2026 Jonathan Corona (soequelle).

This project is not affiliated with QuickLook, Bambu Lab, Prusa Research or OrcaSlicer; their names are only used to describe which files it supports.
