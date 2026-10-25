# MillViewMemories — development and product brief

## Purpose and status

MillViewMemories is a C# Avalonia desktop application for digital scrapbooking: arranging photographs, decorative elements, backgrounds, and written memories into pages grouped into projects/albums.

This file guides agents working anywhere in this repository. Keep it current as product decisions are agreed. The first implementation now includes the core editor, SVG project persistence, resource import, undo/redo, recovery, and album export. The user's answers below and SVG page storage are confirmed requirements; items explicitly labelled proposed remain defaults to review. See the implementation decisions below and README for the constructed version's limitations.

## Confirmed technical baseline

- Application project: `MillViewMemories/MillViewMemories.csproj`.
- Target framework: `net10.0`, nullable reference types enabled.
- Template packages: Avalonia 12.1.4, Avalonia.Desktop, Fluent theme, Inter fonts, CommunityToolkit.Mvvm 8.4.2, and development diagnostics.
- Existing organisation: `Models/`, `ViewModels/`, `Views/`, and embedded application `Assets/`.
- `MillViewMemories.Tests/` is an executable integration runner with Avalonia headless rendering checks; run it using `dotnet run`, not `dotnet test`. There is no solution file.
- Read the project file before changing dependencies. Preserve the existing stack unless a change has a clear reason.

## Product direction and audience

Proposed experience: a local-first creative tool for people who want to preserve personal memories without needing professional design software. Prioritise a clear workspace, dependable saving, and predictable editing over a large feature catalogue.

Product questions and recorded user answers:

- Who is the primary audience, and what experience do they have with scrapbooking/design tools?

Primary audience is an individual hobby user who is likely familiar with scrapbooking but has a low level of familiarity with computational graphic design.

- Which operating systems must version one support? Windows is the current development environment; cross-platform support still needs agreement and verification.

The application should ideally support windows, osx and linux.

- Are pages primarily for home printing, commercial printing, screen viewing, or all three?

All three are possible.  For commerical printing JPEG or PNG export will suffice.  For home printing and screen viewing PDF export will be used.

- What are the preferred page sizes, measurement units, and default orientation?

Page size should be establised when starting a project and all pages in the project will be the same size.  The default size should be 12x12 inches, but there should be defaults for both 7x7 inches and A4 (both portrait and landscape) as well as a custom size option.


- Is an album simply an ordered set of pages, or are sections, spreads, and covers required?

Albums are just an ordered set of pages, however some views may combine two adjacent pages so there needs to be an option to show two pages in the editor. The pages can still be stored separately and objects never need to span the pages, this is just for visualisation.

- Are there existing scrapbook files or resource collections to import?

Not yet, but importing a collection of material will be needed in the program workflow, or automatically discovered from specifing a resource location.  There should be the ability to have multiple resource locations configured.

- What visual style, branding, accessibility needs, and languages should guide the interface?

Visual style should be a simple light mode GUI with guided workflows for complex operations.  Simplicity and step by step instructions are more important than shortcuts and efficiency.


## Proposed initial scope

The first useful version should let a user:

1. Create, open, save, and reopen a scrapbook project in a chosen folder.
2. Add, rename, duplicate, reorder, and delete pages.
3. Import photographs and decorative images into the project.
4. Place images, text, and a solid background on a page.
5. Select, move, resize, rotate, duplicate, delete, and reorder page elements.
6. Use undo/redo for document edits and receive clear unsaved-change prompts.
7. Export an album as a collection of PNG or JPEG images at a chosen resolution, or as a single multi-page PDF.

Proposed later work: reusable templates, cropping and masks, alignment guides, multi-selection, direct printing, rich text, and advanced image effects. Album PNG/JPEG and multi-page PDF export, multiple configurable resource locations, and a two-adjacent-page editor view belong to the initial requirements. The two-page view is for visualisation only: objects remain on individual pages. Cloud synchronisation, accounts, collaboration, online marketplaces, and AI features require separate requirements.

Agree the first implementation milestone before starting broad feature development. A useful vertical slice is: create one page, import one image, add text, save, reopen, and export with the same layout.

## Overall user workflow

### Start or resume

- Offer New Project and Open Project; show recent projects when available.
- New Project collects a title, storage location, and project-wide page dimensions. Offer 12x12 inches by default, 7x7 inches, A4 portrait/landscape, and custom dimensions. All pages use the same size.
- Validate the destination and explain filesystem errors with actionable messages.
- Opening a project validates its format before displaying editable content.

### Gather resources

- Import files through a picker; drag-and-drop can follow when basic import is reliable.
- Copy imported content into the project so moving or deleting the source file does not break the scrapbook.
- Generate thumbnails without blocking the UI. Report unsupported or damaged files individually.
- Show resources in a tray with previews, names, and whether they are used.
- Support multiple configured resource locations. Browsing/discovery can reference those locations, but adding an item to a page copies it into project-owned resources. External libraries are not required to reopen a saved project.

### Compose pages

- Use a page navigator, central canvas, resource tray, and properties panel.
- Show the page boundary clearly; keep zoom/pan separate from document coordinates.
- Selecting an element exposes relevant properties and visible manipulation handles.
- Keep stacking order explicit. Page deletion and resource cleanup must be undoable or confirmed when destructive.
- Provide a way to show both a single page and two adjacent pages in the editor

### Save and recover

- Save updates the current project; Save As creates an independent copy including its resources.
- Show dirty/saved state and prompt to save, discard, or cancel before closing a changed project.
- Autosave recovery is separate from normal saves and must never silently replace a good user save.
- Warn when another application instance has the project open; version one should avoid concurrent editing.

### Export

- Export the ordered album as one PNG or JPEG per page, or as a single multi-page PDF. A selected-page export may be offered in addition.
- Choose the output destination, format, and raster pixel dimensions or DPI. PDF page dimensions follow the project's physical page size; PDF rasterisation resolution is a separate setting.
- Render only document content, without editor handles or guides.
- Explain export failures and request confirmation before overwriting an existing output.

## Project, page, and resource storage

### Proposed editable project format

Start with a self-contained directory and a JSON manifest using a proposed `.mvm` extension. Confirm the extension before registering OS file associations. A portable archive can be added later if needed; do not make it the working format without assessing save/recovery costs.

```text
Holiday Album/
  project.mvm                 # JSON manifest and ordered page references
  pages/
    <generation>/<page-id>.svg # Authoritative editable SVG page document
  resources/
    <resource-id>.<extension>  # Imported original content
  previews/                   # Regenerable thumbnails; never authoritative
```

- Store a `schemaVersion` in the JSON manifest and an application format version in SVG metadata, alongside stable IDs and relevant title/timestamps (UTC).
- The JSON manifest owns ordered page IDs, page names/paths, a resource index, and the shared physical page size. Resolution is an export preference, not an intrinsic property of a vector page.
- Each page is stored as a UTF-8 SVG XML document, never as a JSON page document. SVG is the authoritative persisted representation of page content, geometry, styles, and element order; do not maintain a second JSON copy of page content.
- Resource records include ID, project-relative path, original filename, media type, dimensions when applicable, and optionally a content hash for deduplication.
- SVG images use project-local relative `href` paths (for example `../resources/<resource-id>.png` from `pages/`) so standard SVG consumers can resolve them. Application metadata maps those references to resource IDs. Never use source-machine absolute paths. Treat a resource as immutable; image transforms belong to page elements.
- Store page and element IDs independently of names. Renaming a page must not break references.
- Use UTF-8 JSON and explicit DTOs for the manifest only; use SVG/XML for pages. Use invariant numeric representations throughout, and never serialise Avalonia controls or runtime types.
- Resolve all persisted paths inside the project root. Reject path traversal and unexpected absolute paths.
- Treat previews as disposable. Missing previews must not prevent opening a project.

### Page model and coordinates

- Proposed SVG authoring contract: a restricted, documented SVG subset for application-created pages, rather than an arbitrary SVG editor. Initially support backgrounds, raster images, plain text, groups, and their transforms. Importing arbitrary SVG artwork is a separate scope decision.
- Proposed document unit: points (72 points per inch), independent of screen pixels. Express SVG root width/height with explicit physical units and use a `viewBox` whose width/height equal the project dimensions in points. This deliberately makes one document user unit equal one point; do not rely on implicit SVG pixel sizing.
- The manifest is authoritative for shared page size; SVG root dimensions and `viewBox` must agree. Report inconsistent files rather than silently changing layout. Whether existing projects can be resized is still to be agreed.
- Persist element position, size, rotation, and opacity using SVG geometry/styles/transforms with sufficient precision. SVG child order determines stacking order; do not duplicate it in the manifest.
- Define rotation origin, coordinate origin, aspect-ratio locking, and ordering semantics consistently before building manipulation tools.
- Proposed editor convention: top-left page origin, positive X right, positive Y down, and element rotation about its centre using an explicit SVG transform. SVG alone does not specify the editor's rotation pivot or resize interaction.
- Text elements persist their text, font family, size, colour, and alignment. Define font substitution behaviour; do not assume a font exists on another machine.
- Store non-destructive image placement information. Cropping, if added, must have explicitly defined source-space coordinates.
- Do not persist selection, pointer state, or undo history as document content. Decide separately whether zoom and active page are workspace preferences.
- Preserve stable SVG element IDs. Store application-only data in a dedicated XML namespace or SVG `metadata`, keeping visible content in standard SVG elements. Define the namespace and supported subset before writing the first persistence implementation.
- Maintain an editable in-memory model with lossless round-tripping for the supported subset. Define handling of unsupported elements and externally edited SVG before allowing such files to be overwritten; do not silently discard content.

### Safe saving and compatibility

- Validate document structure and references before committing a save.
- Stage writes and use atomic replacement where supported; retain a recoverable previous manifest/page set until the new save completes.
- Atomic replacement of individual files does not make a multi-file save atomic. Define a generation/staging and recovery protocol before enabling multi-page saves.
- Prevent save operations from overlapping. Avoid leaving the manifest pointing at partially written pages or resources.
- Handle cancellation, disk-full, access-denied, interrupted saves, missing resources, corrupt JSON manifests, and malformed SVG/XML pages without discarding the current in-memory document.
- Never silently overwrite a format version newer than the application supports. Explain incompatibility and preserve the source.
- Implement explicit migrations for older supported versions and keep fixture projects for compatibility tests.
- Do not delete unused resources automatically. Provide explicit cleanup once reference tracking and recovery are reliable.

### Application data versus user content

- User projects live in a user-selected location, outside the source repository by default.  A defaut location should be configured in preferences
- Store settings, recent-project paths, and unsaved draft recovery in per-user OS application-data locations resolved at runtime. Saved projects keep separate recovery manifests alongside their normal manifest; regenerable image previews live in the project's `previews/` folder.
- Keep private photographs, real scrapbook projects, and user-specific paths out of Git. Commit only small, licensed synthetic/sample fixtures.
- `MillViewMemories/Assets/` is for shipped application assets such as icons, not imported user photographs.
- Configured resource locations must distinguish application-provided, user-library, and project-owned resources; copy used dependencies into a project to keep it portable. A project directory is portable as a whole; its individual SVG pages may reference sibling resources and are not necessarily standalone files.

## Application architecture

- Follow MVVM using CommunityToolkit.Mvvm. Keep views focused on presentation and view models on UI state and commands.
- Keep document/domain models independent of Avalonia controls and filesystem access.
- Introduce focused services as needed for project persistence, resource import, thumbnails, export, dialogs, and user settings; avoid speculative abstraction layers.
- Keep pointer interaction and canvas rendering logic separate from serialisable document data.
- Choose an SVG rendering/export approach only after checking support for the agreed SVG subset, text layout, relative images, and PNG/JPEG/PDF output on the target platforms. Do not assume Avalonia can directly edit arbitrary SVG. Keep this technical selection separate from the confirmed SVG storage requirement.
- Route document mutations through undoable operations with clear transaction boundaries. One completed drag should create one undo step.
- Track unsaved state against the saved document revision, including after undo/redo.
- Use the same document geometry and text layout rules for editor previews and export to prevent mismatches.
- Perform filesystem work and expensive image processing asynchronously, honour cancellation, and update observable UI state on the UI thread.
- Dispose image/stream resources, decode appropriately sized previews, and avoid repeatedly decoding full-resolution photos while dragging.
- Add projects or dependencies only when a concrete implementation need justifies them.

## Interaction, accessibility, and visual design

- Prefer a calm workspace with visible page boundaries, readable labels, and consistent spacing.
- Support keyboard access, clear focus indicators, accessible names, and useful empty/error/loading states.
- Plan standard shortcuts for new/open/save, undo/redo, copy/paste, duplicate, delete, and zoom; adapt platform conventions where supported.
- Do not rely solely on colour to communicate selection, errors, or save state.
- Distinguish page zoom from object resizing and provide a fit-page action.
- Validate numeric inputs and constrain invalid dimensions, opacity, and transforms at both UI and model boundaries.
- Use a simple light theme and guided steps for complex operations. Agree minimum supported window size and high-DPI behaviour before polishing the layout; dark mode is not an initial requirement.

## Privacy and resource handling

- Proposed default: all project processing stays local; no telemetry or uploads unless separately requested.
- Treat imported files and project manifests as untrusted input. Bound image dimensions, allocations, and document sizes; handle malformed files gracefully.
- Avoid including personal photo content, metadata, text, or full user paths in diagnostic logs.
- Agree supported import formats and metadata/orientation handling before implementation. Suggested first formats: JPEG and PNG; add other decoders only when required.
- Do not embed executable content or fetch remote resource references from a project.
- Parse SVG/XML with DTDs and external entity resolution disabled. Reject scripts, event handlers, remote URLs, and resource references escaping the project root, including references inside styles. Apply the same resource policy to rendering/export as to loading.
- Use assets and fonts with suitable redistribution licences and record attribution when required.

## Development workflow for agents

1. Read this file, relevant source files, and any deeper `AGENTS.md` instructions before editing.
2. Check repository status and preserve unrelated user changes.
3. Identify the smallest complete user-visible outcome and any decisions that materially affect it.
4. Resolve routine implementation details independently. Ask for missing product decisions when they affect scope, format compatibility, or user-visible behaviour; label interim assumptions.
5. Implement a vertical slice across model, persistence, UI, and validation as appropriate. Avoid broad scaffolding with no usable behaviour.
6. Run checks appropriate to the change and report their actual results and limitations.
7. Update this brief and relevant documentation when agreed behaviour or storage contracts change.

Do not introduce package upgrades, hosting services, accounts, or unrelated rewrites as incidental changes. Keep changes reviewable and explain any format migration.

## Build and verification

Run from the repository root:

```powershell
dotnet restore MillViewMemories/MillViewMemories.csproj
dotnet build MillViewMemories/MillViewMemories.csproj
dotnet run --project MillViewMemories/MillViewMemories.csproj
```

These commands are the development workflow. A compatible .NET 10 SDK and package restore access are needed. See README for Release builds and the executable integration test command.

Run integration tests with `dotnet run --project MillViewMemories.Tests/MillViewMemories.Tests.csproj`. They use temporary directories and synthetic images and include an Avalonia headless desktop test. Add fixtures/migrations when a second format version is introduced; version one currently rejects unsupported versions.

For a working editor, manually verify create/import/edit/save/close/reopen/export, missing resources, unsaved-change prompts, keyboard navigation, and high-DPI rendering. Verify every claimed supported OS. Rendering tests should check layout with reasonable tolerances rather than assume identical font pixels across platforms.

## Initial milestones and acceptance criteria

1. **Agree the brief:** confirm platform, scope, page units/sizes, storage format, import formats, and export goals.
2. **Persistent document:** create a project/page, save, reopen, and preserve IDs, dimensions, and ordering; invalid files produce useful errors.
3. **First editing slice:** import a photo, add text, manipulate elements, and undo/redo; saved content survives reopening.
4. **Multi-page workflow:** navigate, duplicate, reorder, and delete pages without breaking resource references.
5. **Export and resilience:** export the ordered album as PNG/JPEG files and a multi-page PDF matching the SVG layout and physical size, verify failure handling and recovery, and complete accessibility/manual checks.

## Remaining clarifications before the affected feature is implemented

There is enough product detail to plan the first persistence/editor slice. Resolve the following before committing the affected behaviour; these do not reopen requirements already answered above.

- **Platform commitment:** are Windows, macOS, and Linux all required for the first release, or is Windows first with the others following? Minimum OS versions and packaging remain unspecified.
- **Resource discovery:** which formats and folder structures must be supported? Should scanning include subfolders, refresh automatically, and offer bulk copying, or only copy resources when used? JPEG/PNG support remains a proposed starting point, not a confirmed restriction.
- **SVG interoperability:** is SVG primarily the editable internal format, or must users freely edit pages in external SVG applications and bring them back? This determines unsupported-content preservation and font/feature compatibility requirements. The restricted subset above is a proposal pending this answer.
- **Page size changes:** are dimensions fixed after project creation? If resizing is required, choose whether content scales, stays in place, or follows a guided choice.
- **Export defaults:** agree raster DPI, JPEG quality/background, PDF text/font handling, and whether PDF must retain vectors. No commercial print colour profile, bleed, or PDF standard has been requested.
- **Adjacent pages:** agree pairing (including covers/odd page counts), navigation, and which page receives a new object. Proposed starting behaviour is two consecutive pages with no cross-page objects or editing across the gutter.
- **Recovery and limits:** agree autosave cadence/retention and representative project/image sizes for performance checks. Font substitution, default project folder, language, and minimum window size also need defaults during UI planning.

Technical design work must define the SVG namespace/subset, save transaction/recovery protocol, and renderer/export compatibility before persistence is considered complete. Record selected defaults and their rationale here.

Before calling version one complete, agree measurable limits for page count, image count/resolution, project size, startup/open times, editing responsiveness, and export size. Record known limitations and packaging/install instructions.

## Decision log

### First implementation defaults (2026-10-25)

- Windows is the verified development target; retain cross-platform APIs, but macOS/Linux native behaviour and packaging require testing before release claims.
- Manifest version 1 is JSON; page version 1 is SVG with namespace `urn:millviewmemories:page:1`. Application-authored SVG is a restricted subset; unsupported external edits are rejected without overwriting their source.
- Physical dimensions are fixed at creation, expressed in points; SVG roots use `pt` and an equal-sized point-coordinate `viewBox`. Elements use centre-pivot rotation and SVG child stacking order. Resize handles lock proportions; numeric fields allow explicit width/height.
- Normalise imported JPEG/PNG orientation into immutable PNG working copies. Preserve the unmodified originals in `resources/originals`; Save As copies both. Resource discovery scans configured folders recursively on demand, skips links, and imports only selected files.
- Save pages as `pages/<generation>/<page-id>.svg`; publish the new generation by atomically replacing the manifest only after all pages are written/flushed. References use `../../resources/...` from these SVGs. Keep previous manifests and page generations; do not automatically clean them up.
- Separate `.recovery.mvm` snapshots every two minutes; retain a previous snapshot, and offer explicit recovery. Unsaved drafts use per-user application data and a Recover draft action. Concurrent editing is prevented with an exclusive open-project lock.
- Use one SkiaSharp renderer for previews, PNG/JPEG, and PDF, interpreting the same editable model used by the SVG codec. This avoids editor/export geometry drift without executing arbitrary SVG. Tests verify the restricted subset round-trip, raster dimensions, and PDF page count.
- Default export is 300 DPI; JPEG quality 92, opaque page background; PDF preserves vector/text operations and physical sizes. No advanced print profiles or bleed. Text supports explicit newlines/alignment and system font substitution; advanced shaping and automatic wrapping are outside the first version.
- Two-page view starts at the active page and shows its successor, if any. Objects stay on their own page. UI is English, light themed, minimum 1100×700; toolbar buttons supplement keyboard shortcuts.
- History retains 100 edits; a completed pointer drag is one edit. Settings include default project parent folder, recent projects, and resource locations. First-run default parent is the OS Documents folder.
- File/export operations have a working overlay and cancellation checked between files or page elements; native codecs finish the current decode/encode before cancellation is observed. Completed batch imports/exports are retained; cancelled saves do not publish a new manifest.
- Copy/paste uses an internal current-album object clipboard, including across pages; it clears on project switches. Text fields retain normal editing behaviour.
- See README for defensive file/document bounds and manual release checks. Performance at those maximum bounds is not established. Detailed progress estimates and automatic storage cleanup remain follow-up work.

Use dated entries to distinguish agreed decisions from proposals. Include the decision, reason, and consequences for existing files where relevant.

| Date | Decision | Status / implications |
| --- | --- | --- |
| 2026-10-24 | Document the existing .NET 10 / Avalonia / MVVM template and propose a local-first scrapbook workflow. | Planning baseline only; product scope and persistence choices await agreement. |
| 2026-10-25 | Pages are SVG documents; the project manifest remains JSON. | Confirmed SVG storage requirement. Remove JSON page persistence; define SVG metadata, resource references, and rendering compatibility before implementation. |
| 2026-10-25 | Record user answers for audience, shared page sizes, light guided UI, adjacent-page viewing, multiple resource locations, and album PNG/JPEG/PDF export. | Confirmed product direction; remaining implementation choices are listed above. |
