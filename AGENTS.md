# MillViewMemories — development and product brief

## Purpose and status

MillViewMemories is a C# Avalonia desktop application for digital scrapbooking: arranging photographs, decorative elements, backgrounds, and written memories into pages grouped into projects/albums.

This file guides agents working anywhere in this repository. Keep it current as product decisions are agreed. The repository currently contains a blank application template; the workflows and format below are **proposed defaults**, not implemented features or confirmed user requirements. Do not interpret this planning document as an instruction to implement every feature at once.

## Confirmed technical baseline

- Application project: `MillViewMemories/MillViewMemories.csproj`.
- Target framework: `net10.0`, nullable reference types enabled.
- Template packages: Avalonia 12.1.4, Avalonia.Desktop, Fluent theme, Inter fonts, CommunityToolkit.Mvvm 8.4.2, and development diagnostics.
- Existing organisation: `Models/`, `ViewModels/`, `Views/`, and embedded application `Assets/`.
- No test project or solution file was present when this brief was created.
- Read the project file before changing dependencies. Preserve the existing stack unless a change has a clear reason.

## Product direction and audience

Proposed experience: a local-first creative tool for people who want to preserve personal memories without needing professional design software. Prioritise a clear workspace, dependable saving, and predictable editing over a large feature catalogue.

Planning questions to resolve with the user:

- Who is the primary audience, and what experience do they have with scrapbooking/design tools?
- Which operating systems must version one support? Windows is the current development environment; cross-platform support still needs agreement and verification.
- Are pages primarily for home printing, commercial printing, screen viewing, or all three?
- What are the preferred page sizes, measurement units, and default orientation?
- Is an album simply an ordered set of pages, or are sections, spreads, and covers required?
- Are there existing scrapbook files or resource collections to import?
- What visual style, branding, accessibility needs, and languages should guide the interface?

Record confirmed answers in this file rather than silently replacing assumptions in code.

## Proposed initial scope

The first useful version should let a user:

1. Create, open, save, and reopen a scrapbook project in a chosen folder.
2. Add, rename, duplicate, reorder, and delete pages.
3. Import photographs and decorative images into the project.
4. Place images, text, and a solid background on a page.
5. Select, move, resize, rotate, duplicate, delete, and reorder page elements.
6. Use undo/redo for document edits and receive clear unsaved-change prompts.
7. Export a page as a PNG at a chosen resolution.

Proposed later work: reusable templates, shared resource libraries, cropping and masks, alignment guides, multi-selection, PDF export, printing, spreads, rich text, and advanced image effects. Cloud synchronisation, accounts, collaboration, online marketplaces, and AI features require separate requirements.

Agree the first implementation milestone before starting broad feature development. A useful vertical slice is: create one page, import one image, add text, save, reopen, and export with the same layout.

## Overall user workflow

### Start or resume

- Offer New Project and Open Project; show recent projects when available.
- New Project collects a title, storage location, and default page dimensions.
- Validate the destination and explain filesystem errors with actionable messages.
- Opening a project validates its format before displaying editable content.

### Gather resources

- Import files through a picker; drag-and-drop can follow when basic import is reliable.
- Copy imported content into the project so moving or deleting the source file does not break the scrapbook.
- Generate thumbnails without blocking the UI. Report unsupported or damaged files individually.
- Show resources in a tray with previews, names, and whether they are used.

### Compose pages

- Use a page navigator, central canvas, resource tray, and properties panel.
- Show the page boundary clearly; keep zoom/pan separate from document coordinates.
- Selecting an element exposes relevant properties and visible manipulation handles.
- Keep stacking order explicit. Page deletion and resource cleanup must be undoable or confirmed when destructive.

### Save and recover

- Save updates the current project; Save As creates an independent copy including its resources.
- Show dirty/saved state and prompt to save, discard, or cancel before closing a changed project.
- Autosave recovery is separate from normal saves and must never silently replace a good user save.
- Warn when another application instance has the project open; version one should avoid concurrent editing.

### Export

- Choose the page, output destination, and pixel dimensions or DPI.
- Render only document content, without editor handles or guides.
- Explain export failures and request confirmation before overwriting an existing output.

## Project, page, and resource storage

### Proposed editable project format

Start with a self-contained directory and a JSON manifest using a proposed `.mvm` extension. Confirm the extension before registering OS file associations. A portable archive can be added later if needed; do not make it the working format without assessing save/recovery costs.

```text
Holiday Album/
  project.mvm                 # JSON manifest and ordered page references
  pages/
    <page-id>.json             # One page document with ordered elements
  resources/
    <resource-id>.<extension>  # Imported original content
  previews/                   # Regenerable thumbnails; never authoritative
```

- Store a `schemaVersion` in the manifest and page documents, stable IDs, title, and UTC creation/modification timestamps.
- The manifest owns ordered page IDs and a resource index. Each page owns dimensions, background, and its ordered element list.
- Resource records include ID, project-relative path, original filename, media type, dimensions when applicable, and optionally a content hash for deduplication.
- Elements refer to resource IDs, never source-machine absolute paths. Treat a resource as immutable; image transforms belong to page elements.
- Store page and element IDs independently of names. Renaming a page must not break references.
- Use UTF-8 JSON, invariant numeric representations, and explicit DTOs rather than serialising Avalonia controls or runtime types.
- Resolve all persisted paths inside the project root. Reject path traversal and unexpected absolute paths.
- Treat previews as disposable. Missing previews must not prevent opening a project.

### Page model and coordinates

- Proposed document unit: points (72 points per inch), independent of screen pixels. Convert measurements only at UI and export boundaries.
- Persist page width/height and element position, size, rotation, opacity, and stacking order with sufficient precision.
- Define rotation origin, coordinate origin, aspect-ratio locking, and ordering semantics consistently before building manipulation tools.
- Proposed origin: top left; positive X right; positive Y down; rotation about the element centre.
- Text elements persist their text, font family, size, colour, and alignment. Define font substitution behaviour; do not assume a font exists on another machine.
- Store non-destructive image placement information. Cropping, if added, must have explicitly defined source-space coordinates.
- Do not persist selection, pointer state, or undo history as document content. Decide separately whether zoom and active page are workspace preferences.

### Safe saving and compatibility

- Validate document structure and references before committing a save.
- Stage writes and use atomic replacement where supported; retain a recoverable previous manifest/page set until the new save completes.
- Atomic replacement of individual files does not make a multi-file save atomic. Define a generation/staging and recovery protocol before enabling multi-page saves.
- Prevent save operations from overlapping. Avoid leaving the manifest pointing at partially written pages or resources.
- Handle cancellation, disk-full, access-denied, interrupted saves, missing resources, and corrupt JSON without discarding the current in-memory document.
- Never silently overwrite a format version newer than the application supports. Explain incompatibility and preserve the source.
- Implement explicit migrations for older supported versions and keep fixture projects for compatibility tests.
- Do not delete unused resources automatically. Provide explicit cleanup once reference tracking and recovery are reliable.

### Application data versus user content

- User projects live in a user-selected location, outside the source repository by default.
- Store settings, recent-project paths, caches, logs, and recovery snapshots in appropriate per-user OS application-data/cache locations resolved at runtime.
- Keep private photographs, real scrapbook projects, and user-specific paths out of Git. Commit only small, licensed synthetic/sample fixtures.
- `MillViewMemories/Assets/` is for shipped application assets such as icons, not imported user photographs.
- A future shared library must distinguish application-provided, user-library, and project-owned resources; copy dependencies into a project to keep it portable.

## Application architecture

- Follow MVVM using CommunityToolkit.Mvvm. Keep views focused on presentation and view models on UI state and commands.
- Keep document/domain models independent of Avalonia controls and filesystem access.
- Introduce focused services as needed for project persistence, resource import, thumbnails, export, dialogs, and user settings; avoid speculative abstraction layers.
- Keep pointer interaction and canvas rendering logic separate from serialisable document data.
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
- Agree minimum supported window size, light/dark theme expectations, and high-DPI behaviour before polishing the layout.

## Privacy and resource handling

- Proposed default: all project processing stays local; no telemetry or uploads unless separately requested.
- Treat imported files and project manifests as untrusted input. Bound image dimensions, allocations, and document sizes; handle malformed files gracefully.
- Avoid including personal photo content, metadata, text, or full user paths in diagnostic logs.
- Agree supported import formats and metadata/orientation handling before implementation. Suggested first formats: JPEG and PNG; add other decoders only when required.
- Do not embed executable content or fetch remote resource references from a project.
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

These commands describe the intended workflow; they were not validated as part of creating this planning document. A compatible .NET 10 SDK and package restore access are needed.

When test projects are added, document their commands here. Focus automated tests on document round-trips, stable ordering/references, undo/redo, geometry conversions, format migration, and save/recovery failure paths. Use temporary directories and synthetic images.

For a working editor, manually verify create/import/edit/save/close/reopen/export, missing resources, unsaved-change prompts, keyboard navigation, and high-DPI rendering. Verify every claimed supported OS. Rendering tests should check layout with reasonable tolerances rather than assume identical font pixels across platforms.

## Initial milestones and acceptance criteria

1. **Agree the brief:** confirm platform, scope, page units/sizes, storage format, import formats, and export goals.
2. **Persistent document:** create a project/page, save, reopen, and preserve IDs, dimensions, and ordering; invalid files produce useful errors.
3. **First editing slice:** import a photo, add text, manipulate elements, and undo/redo; saved content survives reopening.
4. **Multi-page workflow:** navigate, duplicate, reorder, and delete pages without breaking resource references.
5. **Export and resilience:** produce a PNG matching page layout at the requested size, verify failure handling and recovery, and complete accessibility/manual checks.

Before calling version one complete, agree measurable limits for page count, image count/resolution, project size, startup/open times, editing responsiveness, and export size. Record known limitations and packaging/install instructions.

## Decision log

Use dated entries to distinguish agreed decisions from proposals. Include the decision, reason, and consequences for existing files where relevant.

| Date | Decision | Status / implications |
| --- | --- | --- |
| 2026-10-24 | Document the existing .NET 10 / Avalonia / MVVM template and propose a local-first scrapbook workflow. | Planning baseline only; product scope and persistence choices await agreement. |
