# Application Audit

## Material Findings

| Priority | Finding | Resolution |
| --- | --- | --- |
| P1 | Shell request files persisted with document paths | Bounded validation, one-time consumption, deletion, and stale cleanup implemented and live-tested |
| P1 | No graceful cancellation; external converters could remain running | Ctrl+C token wiring plus Windows Job Object ownership and timeout-specific errors implemented |
| P1 | Cancellation could leave partial or staged PDF output | All operation services now clean staged files; Split rolls back emitted pages and a newly created empty folder |
| P1 | Native UTF-8 buffer overflow risk and launch-failure request leak | Correct buffer sizing/conversion checks and native failure cleanup implemented |
| P1 | Two high-severity vulnerable test dependencies | Migrated to xUnit v3 3.2.2, runner 3.1.5, Test SDK 18.7.0, and coverlet 10.0.1 |
| P2 | Logging could throw while handling another error | Logger is now best-effort, reports success, and traces its own failures |
| P2 | Microsoft Office cancellation could outlive a disposed wait handle | Durable task completion and deferred cleanup implemented; Office is not force-killed to protect open user documents |
| P2 | PDF Gear restore script silently ignored registry restore errors | Removed the empty catch so the action-level result reports failure |
| P3 | Old self-test runs left empty temp folders | Removed verified-empty legacy folders and made the current self-test remove its empty parent root |

No unresolved P0 or P1 findings remain.

## Architecture

- Core PDF services remain separate from CLI presentation and native Explorer integration.
- Atomic sibling staging remains the output boundary; cleanup behavior is now consistent across services.
- Request-file and external-process lifecycle ownership are explicit.
- No broad file split was performed on `Program.cs` or the native COM source because both are stable, heavily covered at their public boundaries, and a large move would add regression risk without changing user outcomes.

## UI And Accessibility

- This product has no graphical frontend. Its user interface is the Windows context menu plus a keyboard-operated Spectre.Console workflow.
- Existing menu labels, PDF icon, progress, success, and friendly error panels were preserved.
- Merge sorting and Split page selection remain the final interactions; no confirmation prompts were reintroduced.
- Ctrl+C cancellation is now documented and checked during sorting and long-running work.
- Live menu validation used an isolated COM probe instead of foreground Explorer automation while the user was working.

## Security And Privacy

- No secrets, network uploads, database, authentication surface, or public API are present.
- Request input is capped at 1 MiB and 2,048 selected files, with enum, identifier, and path-entry validation.
- Arbitrary user-supplied request JSON outside the app-owned temp folder is read but never deleted.
- Dependency audit reports no known vulnerable direct or transitive packages.

## Performance

- No material regression was measured in the critical flows.
- Scan rendering remains CPU-intensive by design and page-bounded; changing Skia/PDF dependencies or scan math was deferred to preserve the accepted visual output.
