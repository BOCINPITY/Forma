# Icon quality

[Documentation](README.md) · [简体中文](../icon-quality.md)

Adaptation should preserve an application's identity and detail while making its outer shape and visual size more consistent. A valid ICO and a successful apply/restore cycle do not prove visual quality. Judge icons at their actual display sizes, especially **32px and 48px**.

## Shape-aware adaptation in preview.9

The macOS material mode analyzes alpha coverage, silhouette, and edge variation to distinguish existing backgrounds, solid circular marks, transparent marks, and fine or complex artwork. Classification uses pixels rather than application names or design semantics. Uncertain and translucent shapes take a conservative path.

- Existing backgrounds retain their palette and merge into the new outline, reducing obvious nested frames.
- Circular and transparent marks receive bounded optical size compensation. Fine/complex artwork receives a modest effective-span adjustment to retain useful small-size pixels.
- Non-background artwork gets safe margins based on its visible shape and the target rounded outline, helping preserve diagonal tips. Safety is checked for each native source frame; selection and drawing use the same span.
- Disabling transparent-margin trimming preserves the full canvas. There is no added sharpening, outline, or second scaling pass. The other three styles keep their existing rendering behavior.

These rules cannot replace bespoke icon design or recreate detail missing from a low-resolution source. Fine lines, dense textures, and safety-limited artwork produce hints. Use both the group desktop preview and the actual-size inspector.

![Shape adaptation: preview.8 and preview.9 at the same settings](../images/shape-adaptation-comparison.png)

The comparison uses eight code-generated samples at 80% scale and 23% corners. Output rows use actual 96 / 32 / 48px ICO frames; the source row is a thumbnail. Dense grids and diagonal tips prioritize safe margins. Enlarging every icon is not a quality target.

## macOS material

The macOS preset combines a continuous superellipse outline, gentle vertical lighting, an inner highlight, and a separate shadow. Existing backgrounds fill the new outline with colors estimated from the source; transparent marks receive a lightly tinted material while preserving foreground colors. Small sizes omit fine highlights to keep the main shape readable.

![The same synthetic inputs rendered with gradient and macOS materials](../images/mac-style-comparison.png)

This earlier material comparison uses identical inputs and 80% scale. The bottom rows show exported 32 / 48 / 96px frames rather than screenshots of a scaled large preview. Irregular backgrounds, photographic icons, and semitransparent art may still need another style or manual adjustments.

New installations default to the macOS mode. Existing settings remain unchanged; choosing **macOS preset / macOS 预设** switches explicitly to 80% scale, 23% corners, and margin trimming. The new enum value was appended to preserve the meaning of older saved styles.

## Native frames and sampling

Forma reads native PNG or DIB frames from the selected ICO/EXE icon group and retains useful frames at different sizes. PNG bytes are retained; DIBs are decoded at native dimensions. Actual image dimensions take precedence over ICO directory labels. Resource count, byte size, and decoded-pixel limits bound extraction; invalid frames are skipped and reported.

Source selection considers the actual visible span. A 32px canvas with wide transparent borders is not treated as 32px of useful artwork. Small native frames can preserve hand-tuned details that would be lost by always shrinking the largest image. Shell fallback does not establish that a source really contains a high-resolution frame.

Reduction integrates the full destination-pixel footprint with premultiplied-alpha area sampling to reduce aliasing, colored edges, and ringing. Native 1:1 pixels remain aligned. Enlargement uses ordinary linear interpolation and cannot invent detail. Nonzero translucent edges are retained; very fine marks are not forcibly thickened.

Exported sizes are **16 / 20 / 24 / 32 / 40 / 48 / 64 / 80 / 96 / 128 / 192 / 256px**. Each is generated from a suitable source rather than repeatedly shrinking a large composite. Foreground and backdrop are composed before one antialiased outer-shape clip; the shadow is separate. Excessive scale can still clip artwork, so reduce scale to retain its edges. Transparent mode preserves the original silhouette.

## What the preview proves

The inspector reads the matching exported 32 / 48 / 64 / 96px ICO frame. Switching size displays an existing frame without rerendering. The library displays 48 physical pixels with device-pixel alignment. Source previews choose suitable native frames and do not upscale a small source to suggest nonexistent detail.

**Clarity first / 清晰优先** explicitly enables trimming and 80% scale; an upgrade does not silently replace saved settings. Quality hints identify source-size, low artwork-scale, and potential clipping constraints.

Automated checks cover native-frame selection, thin lines, low-alpha edges, pixel lettering, rounded compositing, all 12 sizes through WIC, and pixel agreement between WPF preview and export. Geometry checks cover 100 / 125 / 150 / 200% DPI and odd-size centering. They are not physical multi-monitor DPI-switching tests.

Real local app icons may be extracted read-only for private comparisons; the originals and paths remain under ignored `.local` storage and are not distributed. Documentation illustrations use synthetic samples. Dense photographic detail, extremely small sources, and very small artwork proportions remain limited by available pixels; Forma does not promise lossless 16px output for every icon.
