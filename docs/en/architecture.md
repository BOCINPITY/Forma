# Architecture

[Documentation](README.md) · [简体中文](../architecture.md)

Forma **0.1.0-preview.9** uses C#/.NET 10, WPF, SkiaSharp, Windows Shell COM/Win32, and local JSON operation records. The product name and app assembly are Forma; existing source names and the IconAdapt data directory are retained for compatibility.

## Modules

| Module | Responsibility | Boundary |
| --- | --- | --- |
| `IconAdapt.App` | WPF workspace, MVVM, settings, comparisons, and progress | Background rendering, debounce/cancellation, UI-thread updates |
| `IconAdapt.Core` | Shortcut, style, source-frame, and batch models; service interfaces | No WPF or Shell dependency |
| `IconAdapt.Imaging` | Visible-bound analysis, resampling, materials, and ICO encoding | No UI-thread dependency; native resources released per call |
| `IconAdapt.Windows` | Desktop discovery, extraction, STA COM, storage, backup, and restoration | Serialized work, a cross-process storage lock, and persistent write-ahead records |

Storage currently shares the Windows library with shortcut transactions. The App saves theme and style settings. A dedicated STA worker serializes Shell COM calls; WPF updates return to the UI context, and decoded images are frozen before crossing threads. Icon handles and native image objects are released according to their ownership.

## Rendering and preview

The processing sequence is:

```text
Scan → parse shortcut → extract native frames → analyze visible bounds
     → scale/composite → encode multi-size ICO → backup and record
     → update icon reference → notify Shell → report results
```

Extraction reads native PNG/DIB frames from ICO files or EXE icon groups, retaining actual dimensions and useful small frames. Shell extraction is a fallback with an explicit source limitation. Core's `IconFrame` / `SourceFrames` carry native pixels. Imaging selects frames by their effective visible span and uses premultiplied-alpha area resampling for reduction. All 12 output sizes are rendered from appropriate sources rather than repeatedly shrinking one composite.

`MacIconMaterial` analyzes alpha area, silhouette, edge coverage, variation, and color distribution. The preview.9 rules distinguish existing backgrounds, circular marks, transparent marks, and fine/complex artwork. Optical size compensation is bounded, and non-background artwork is checked against the target rounded shape for safe margins. Each native frame is analyzed independently; frame selection and drawing use the same effective span. This is pixel analysis with no semantic understanding. Gradient, solid, and transparent modes retain their separate rendering behavior.

The inspector decodes the corresponding exported ICO frame. `NativePixelImage` draws at physical-pixel size; `PixelImageGeometry` handles DPI and pixel alignment. The manifest declares PerMonitorV2. Geometry checks do not replace physical multi-monitor validation. See [icon quality](icon-quality.md).

## Workspace behavior

The WPF workspace combines an icon library, fixed inspector, top restoration action, bottom action bar, and inline operation details. It uses WindowChrome and shared App.xaml resources. The application mark is generated with code geometry.

`Controls/Motion` manages brief opacity, displacement, rotation, and expansion transitions. New transitions continue from current displayed values and replace old clocks, with cleanup on completion or unload. Disabling Windows animations converges immediately to the final state. Source-change animation is enabled only in the inspector and does not alter Imaging output.

`DesktopPreview` shares `ShortcutItem` objects, inspector focus, and size commands with the library. It maintains its own selected-item collection rather than changing the library's default collection view. Subscriptions to scan/selection changes are released on unload. `DesktopImage` uses the exported frame when available, otherwise clearly labels a non-upscaled original. `DesktopBackdrop` draws simulated backgrounds using WPF gradients and paths. Background/view/size changes affect display only; no wallpaper access, Shell layout change, or ICO regeneration occurs.

## Storage

The default root is `%LOCALAPPDATA%\IconAdapt\`, obtained through system folder APIs. `--data-root` allows isolated developer storage.

| Location | Purpose |
| --- | --- |
| `settings.json` | User settings and style parameters |
| `icons\` | Persistent ICOs, separated by batch ID and item number |
| `backups\<batch-id>\` | Original shortcut copies and recovery material |
| `operations\<batch-id>.json` | Versioned operation records |
| `operations.lock` | Cross-process operation lock |

Final ICOs must remain in persistent storage because shortcuts continue to reference them. Cleanup and uninstall behavior must account for those references. Errors are kept in operation records and the interface; there is no separate general diagnostic log. Screenshot automation writes a separate error file on failure.

## Safe apply and restore

1. Resolve and deduplicate real scan locations; validate file type, existence, and write access. Reject unsafe path forms, symbolic links, and junction traversal.
2. Capture original icon path/index, shortcut backup, and the pre-change fingerprint. Persist the intent record before mutation.
3. Generate and verify readable final ICOs. Modify a same-directory shortcut copy through `IShellLink` / `IPersistFile`, verify launch properties and the source hash, then atomically replace the source.
4. Persist per-item states: queued, pending, applied, failed, cancelled, restoring, restored, or conflict. Preserve recoverable intent when a write's outcome is uncertain and report partial success accurately.
5. Before restoration, check for external edits. Normally restore the original icon fields; a conflict preserves the current shortcut rather than silently overwriting it with a full historical copy.

Records use schema, ID, size, hash, and storage-path validation. Updates flush to disk with `Flush(true)`, then use a temporary file and replacement. Device paths, alternate data streams, and symbolic-link escapes are rejected. Property checks permit Shell normalization of `HasRelativePath` while continuing to verify actual launch properties and other behavior flags. Errors identify mismatched fields without exposing launch arguments.

`RestoreDefaultsAsync` holds the STA work item and cross-process lock while restoring all active paths in the current data root, newest layer first. It updates parent-layer fingerprints after each restoration. A conflict blocks older layers for that path while allowing others to continue. Results count unique initial paths; success requires every layer to be restored. Cancellation preserves partial results and records for a later retry. `RestoreAsync(batchId)` remains a lower-level compatibility interface.

The App rescans after restoring to refresh fingerprints and retains full results inline. When scanning an already-adapted shortcut, the service follows a valid backup chain to obtain original artwork and avoid repeated nested framing. Existing backups are retained without a data migration.

`SHChangeNotify` requests a Shell refresh after updates. It does not guarantee immediate changes at every display location. Forma never automatically elevates, restarts Explorer, or deletes the global icon cache.

## Remaining validation

Real desktop cache behavior, mixed-monitor DPI transitions, redirected/OneDrive desktops, unusual permissions, recreated shortcuts, and large scans need continued manual coverage. Automated pixel, COM, and integration checks establish their specific invariants; they do not establish every real-world display outcome.

References: [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/), [SkiaSharp](https://mono.github.io/SkiaSharp/), [SetIconLocation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinkw-seticonlocation), and [Shell icon refresh behavior](https://devblogs.microsoft.com/oldnewthing/20150903-00/?p=91671).
