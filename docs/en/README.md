# Forma documentation

[简体中文](../README.md) · [Project home](https://github.com/BOCINPITY/Forma) · [Releases](https://github.com/BOCINPITY/Forma/releases)

Forma is a Windows desktop icon adaptation app. It brings application shortcuts closer together in visual size, background shape, and spacing while preserving their original artwork. The current version is **0.1.0-preview.9**, an unsigned Windows x64 development preview.

![Forma workspace with synthetic test icons](../images/workspace.png)

Start with the [user guide](user-guide.md) for portable setup, supported shortcuts, read-only previews, applying changes, and restoring icons. Keep `%LOCALAPPDATA%\IconAdapt\` while adapted icons are in use: shortcuts reference the ICO files stored there.

| Guide | What it covers |
| --- | --- |
| [User guide](user-guide.md) | Preview, adapt, restore, storage, and common problems |
| [Icon quality](icon-quality.md) | Native source frames, actual-size previews, and shape-aware adaptation |
| [Development](development.md) | Windows setup, checks, packaging, CI, and releases |
| [Architecture](architecture.md) | Modules, rendering, persistence, and safe shortcut updates |

Additional Chinese documentation: [UI design](../ui-design.md), [product scope](../product.md), [apply troubleshooting](../troubleshooting-apply.md), [verification record](../verification.md), and [changelog](../../CHANGELOG.md).

The project is licensed under [MIT](../../LICENSE). Dependencies retain their own licenses; see [third-party notices](../../THIRD-PARTY-NOTICES.md) and the [upstream license files](../../licenses/). Forma does not bundle Apple icons or third-party application icon packs.
