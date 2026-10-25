# MillViewMemories

A local-first digital scrapbook editor built with C#, .NET 10, and Avalonia. Arrange photographs and text on pages, save editable SVG documents, and export an album as PNG/JPEG images or a multi-page PDF.

## Build and run

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The first restore needs access to NuGet. Run these commands from the repository root:

```powershell
dotnet restore MillViewMemories/MillViewMemories.csproj
dotnet build MillViewMemories/MillViewMemories.csproj -c Release --no-restore
dotnet run --project MillViewMemories/MillViewMemories.csproj -c Release --no-build
```

The application uses native Skia libraries supplied through Avalonia. Windows is the validated development platform. macOS and Linux are intended targets but still need native desktop, font, file-picker, and packaging verification. Linux requires a graphical desktop and the platform libraries described in [Avalonia's supported-platform documentation](https://docs.avaloniaui.net/docs/overview/supported-platforms).

To publish a folder containing the application and .NET runtime, for example for Windows x64:

```powershell
dotnet publish MillViewMemories/MillViewMemories.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Run `artifacts/win-x64/MillViewMemories.exe`. Other runtime identifiers, such as `osx-arm64` or `linux-x64`, require validation on those platforms. This produces an application folder, not a signed installer; keep its contents together. OS file associations are not registered.

## Make a scrapbook

1. Choose **New project**, give the album a title, select its page size, and choose a parent folder. The application creates a new folder named after the album. Sizes include 12×12 inches, 7×7 inches, A4 portrait/landscape, and custom inches. All pages share that size, which stays fixed after creation.
2. Choose **Import JPEG / PNG**. Select an imported resource and choose **Place selected resource**. Original files and orientation-normalised PNG working copies are kept inside the project, so the source files can subsequently move without breaking the album.
3. Choose **Add text**. Click an object to select it. Drag it to move; drag the bottom-right handle to resize with proportions locked. Use the right-hand properties panel for precise position, dimensions, rotation, opacity, text, font, alignment, and colour. Measurements are points (72 per inch). Choose **Apply object changes** to commit property fields; un-applied field text is not a document edit.
4. Use **Backward / Forward** to change object stacking, **Duplicate / Delete** to manage objects, and the page controls to add, rename, duplicate, reorder, or delete pages. At least one page remains. Page deletion can be undone.
5. Use **One / two pages** to show the active page and its following page; the final page appears alone. Click a page to make it active. Objects belong to a single page and cannot span the gutter. Use **Fit page** or the zoom buttons; scroll to pan the workspace.
6. Choose **Save**. **Save as** writes an independent album into an empty destination folder. **Open** selects `project.mvm`; **Recent** lists recently opened/saved albums.
7. Choose **Export album**, select PNG, JPEG, or PDF, and choose a destination. PNG/JPEG create `page-001.png`/`.jpg`, etc., in album order. PDF produces one document with the project's physical page dimensions. Exports exclude editor handles.

Default raster export is 300 DPI; choose 36–600 DPI, subject to a 64-megapixel output limit per page. JPEG uses quality 92 against the opaque page background. PDF retains text/vector operations and embeds raster photos; there is no bleed, CMYK, PDF/X, or commercial-print colour-management workflow. Text uses explicit line breaks rather than automatic wrapping. Missing font families use the system fallback, so install matching fonts on machines where identical text layout matters. The first version targets English/plain left-to-right text rather than advanced typography.

**Preferences** configures the default project parent folder and multiple resource folders. **Browse resource folders** scans those folders and their subfolders on demand, excluding filesystem links, and imports the selected images. There is no background watcher. Only JPEG/PNG import is supported initially. Resources show thumbnails and usage counts; unused imports are retained.

Keyboard shortcuts: Ctrl (Command on macOS) + N/O/S for new/open/save, Shift+S for Save As, Z for undo, Shift+Z or Y for redo, D for duplicate, and C/V for copying/pasting objects between pages in the current album; Delete removes the selected object. The object clipboard is internal to the album and clears when switching projects. Text fields retain their own editing shortcuts. Toolbar/property buttons supplement keyboard access.

File and export operations show a working overlay with **Cancel operation**. Cancellation is checked between files or page objects; a single native image decode/encode must finish first. Cancelled saves keep the previously committed manifest. Completed imports and individual exported pages are retained if a batch is cancelled. Each exported file is replaced only after its new contents are ready; an album image export is not an all-or-nothing batch transaction.

## Saving and recovery

```text
My album/
  project.mvm                    JSON project manifest, dimensions, resource index, page order/names
  project.mvm.previous           Previous committed manifest, after a subsequent save
  .mvm.lock                      Exclusive open-project lock (an existing file alone is harmless)
  .recovery.mvm                  Separate autosave manifest, when available
  pages/<generation>/<id>.svg    Immutable SVG page generations
  resources/<id>.png             Normalised working images
  resources/originals/<id>.*     Unmodified imported originals
  previews/<id>.png              Optional, regenerated resource thumbnails
```

SVG pages are the authoritative page documents; page content is not duplicated in JSON. SVGs reference project images using relative paths. Move or back up the **whole album folder**, not just its manifest or individual pages. Page generations are written and flushed before the manifest is replaced. The previous manifest and its referenced pages remain available if a save fails. This protects against application-level interrupted saves; it is not a substitute for backups or a guarantee against filesystem/hardware failure.

Every two minutes, changed projects receive a separate recovery snapshot. Opening an album offers a newer snapshot without silently replacing the saved album. If the current manifest cannot be read, the application offers its previous save when present. Unsaved albums have recovery snapshots under the per-user application-data `MillViewMemories/drafts` folder; choose **Recover draft** to restore one. Recovery does not include changes made after the most recent snapshot. Discarding changes removes the current recovery manifest. Save a recovered document to keep it permanently.

Settings, recent paths, and drafts are stored under `Environment.SpecialFolder.LocalApplicationData/MillViewMemories` (normally `%LOCALAPPDATA%\MillViewMemories` on Windows). Project files are local, with no uploads or application telemetry. The Avalonia build tooling has its own build-time behaviour, separate from the application.

For safety, projects use a restricted version-one SVG contract (`urn:millviewmemories:page:1`). They contain a background rectangle, transformed image/text groups, IDs, and application metadata. Arbitrary SVG artwork import and external-editor round-trips are not supported: unsupported nodes/attributes/transform changes cause a clear load failure rather than being silently discarded. Newer manifest/page versions, remote references, XML DTDs, and paths outside the album are rejected. A project open in another instance cannot be edited concurrently.

Old page generations, unused resources, duplicate-import leftovers, and old draft folders are retained intentionally. There is no automatic storage cleanup yet; do not manually remove referenced files. Undo/redo retains up to 100 document edits in memory and is not restored after reopening.

## Automated tests

The test project is an executable integration runner that returns a nonzero exit code on failure. Use `dotnet run`, not `dotnet test`:

```powershell
dotnet restore MillViewMemories.Tests/MillViewMemories.Tests.csproj
dotnet run --project MillViewMemories.Tests/MillViewMemories.Tests.csproj -c Release
```

The suite covers SVG/manifest round-trips, imported resources, portable Save As, interrupted saves, previous-save/recovery isolation, exclusive locks, invalid paths/versions/XML/SVG, validation, undo/redo, page duplication, PNG/JPEG dimensions, ordered album exports, PDF page count, and output-allocation limits. An Avalonia headless test renders the actual desktop shell, exercises editing buttons and pointer dragging, and captures `desktop/desktop-smoke.png`. No display server or visible window is required for the headless test; native Skia/font dependencies are still needed. See [Avalonia's headless test documentation](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform).

Each run creates synthetic images and a uniquely named `MillViewMemories-tests-*` temporary directory, printed in the output. Fixtures are retained for diagnosis. The headless window also initialises a normal per-user draft workspace, but does not change your saved albums or preferences. Tests never use personal photographs.

## Manual release checks

- Create each preset size and a custom-size album; save, close, reopen, and verify dimensions and ordering.
- Import PNG transparency and JPEG EXIF orientation examples. Place a resource, move the source file, and reopen the album.
- Edit text, background, opacity, dimensions, and rotation; drag/resize objects; duplicate/delete/reorder objects and pages; check undo/redo and the unsaved marker.
- Verify single/two-page navigation, keyboard focus, text-field editing, window resizing, scrolling, and high-DPI display scaling.
- Exercise Save/Discard/Cancel on close and project switches; confirm Save As remains independent of its source album.
- Wait two minutes after changing a project, terminate it without saving, reopen, and verify recovery is offered. Try an unsaved draft using Recover draft.
- Export PNG/JPEG at 72 and 300 DPI and a multi-page PDF; inspect page order, physical dimensions, text, rotation, and image placement in independent viewers. Test overwrite cancellation and unwritable destinations.
- Repeat the desktop checks on each operating system before claiming release support.

Validation limits are 500 pages, 2,000 objects per page, 10,000 resources, 100 megapixels / 100 MB per image, and 0.5–100 inches per page dimension. These are defensive bounds, not tested performance guarantees. Start with modest albums; large-project benchmarking, detailed progress estimates, direct printing, crop/masks, rich text, reusable templates, installers, and resource cleanup remain future work. Build and test instructions describe the implemented version; product/design context is in [AGENTS.md](AGENTS.md).
