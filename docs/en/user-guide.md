# Forma user guide

[Project home](https://github.com/BOCINPITY/Forma) · [简体中文](../user-guide.md)

Forma adapts ordinary Windows application shortcuts. You can compare icons before applying a style, update a selected group, and restore their original icon references later. This guide describes **0.1.0-preview.9**. The current interface uses Chinese labels; the key controls are identified below.

## Start the portable app

Download the Windows x64 ZIP from [Releases](https://github.com/BOCINPITY/Forma/releases), extract the complete `Forma-<version>-win-x64` folder, and open `Forma.exe`. Keep all accompanying files: the EXE alone is insufficient. The self-contained package includes its runtime, so you do not need the .NET SDK.

This is an unsigned development preview, with no installer or automatic updater. It targets Windows x64, with initial validation focused on Windows 11. Close the old app before opening an upgrade.

Forma was previously called IconAdapt. Upgrades continue to use `%LOCALAPPDATA%\IconAdapt\` for settings, icons, backups, and operation records. Keep this directory at its existing location; renaming the app does not require migrating its data.

## Supported shortcuts

Forma scans ordinary `.lnk` files on the current user's desktop and the public desktop, or in a folder you choose. Scans cover that folder only, without recursion. Currently, adaptation requires a shortcut to an accessible `.exe`. Broken, unsupported, or malformed entries show a reason in the interface. Symbolic links and directory junctions are rejected.

Taskbar pins, Start menu entries, system desktop icons, dedicated Store app entries, file associations, and folder icons are outside this version's scope. Updating a desktop shortcut does not guarantee changes to a running app's or taskbar's icon. Forma changes icon references and preserves the target, arguments, working directory, and other launch properties.

## Preview and apply

![Workspace: icon library on the left, actual-size comparison and settings on the right](../images/workspace.png)

1. Scan the desktop or choose a folder containing `.lnk` files.
2. Select the shortcuts to adapt. Focus an item to inspect its original and adapted icon.
3. Click **macOS preset / macOS 预设** for automatic colors and a continuous rounded shape, or choose gradient, solid, or transparent backgrounds. Compare the exported **32 / 48 / 64 / 96px** frames in the inspector.
4. Adjust the artwork scale, corner radius, and transparent-margin trimming. If an icon already has a complete background, retaining its canvas and adjusting scale may work better.
5. Apply the selected items. Forma saves a backup and a persistent operation record before changing each shortcut's icon reference.
6. Read the item statuses and expand **Operation details / 操作详情** at the bottom left for full results. Partial failures retain successful changes; restoration can undo them together.

Scanning, selection, and preview are read-only. Only applying or restoring changes writes to shortcuts. Shell refresh can take time; wait briefly or refresh the desktop manually.

The macOS preset selects the macOS material style, 80% artwork scale, 23% corners, and transparent-margin trimming. It samples colors automatically: existing backgrounds keep their palette, while transparent marks receive a lightly tinted backdrop. This is a conservative pixel-based analysis and does not redraw lettering or application logos. Unusual shapes and translucent artwork may need a different mode or manual scale adjustment.

**Clarity first / 清晰优先** enables margin trimming and sets artwork scale to 80%. New installations default to the macOS style; upgrades preserve existing settings until you choose a preset. Small source images carry a quality warning because enlargement cannot recover missing detail. Artwork extending beyond a background's rounded outline is clipped; reduce its scale if necessary.

The workspace supports windows down to 960 × 680. Short interface transitions follow the Windows animation setting; they do not alter exported icons or final preview pixels.

## Compare the whole group

Select items in the library, then switch to **Desktop preview / 桌面预览**. Compare light and dark backgrounds, original and adapted artwork, and 32 / 48 / 64 / 96px display sizes. Clicking an icon keeps its individual comparison available in the inspector.

![Selected icons in the light desktop preview](../images/desktop-preview-light.png)

![The same desktop preview on a dark background](../images/desktop-preview-dark.png)

Only selected items appear. If none are selected, return to the library to select some. An item without generated output shows its original icon with **Not generated; showing original / 未生成，显示原图**; this is not a successful adaptation. Backgrounds and positions are simulated: Forma does not read your wallpaper or move real desktop icons. Changing preview background, view, or size never applies icons.

## Restore default icons

Click **Restore default icons / 恢复默认图标** at the top and confirm. Forma works backward through repeated adaptations of each shortcut until it reaches the icon reference from before its first adaptation. “Default” includes any custom icon you had already chosen; it does not mean the application's factory icon. Restoration does not reset the style controls.

**Restoration covers all adapted shortcuts recorded in the current data directory**, including those in other folders. It is independent of the current scan and selection. The default directory belongs to the current Windows account. The button is disabled when nothing needs restoring; older backups remain usable.

Forma checks for external edits before restoring. A conflict preserves the current shortcut and stops earlier restoration layers for that path; other shortcuts continue. A shortcut counts as restored only when all its adaptation layers have been undone. Inspect Operation details, resolve permission, file-lock, or conflict issues, and retry. Restoration can continue after interruption or restart.

## Keep the data directory

`%LOCALAPPDATA%\IconAdapt\` contains generated ICOs, original shortcut backups, settings, and operation records. **Keep it while adapted icons are in use.** Shortcuts continuously reference these ICO files. Closing Forma or moving its program folder does not restore icons.

Before removing Forma's data, restore default icons, check that all results succeeded, and confirm the desktop appearance. Backups and records can contain local paths and shortcut information; keep them out of public issue attachments.

## Common problems

| Situation | What to do |
| --- | --- |
| No shortcuts found | The desktop may contain only folders or system icons. Choose the folder that actually contains the `.lnk` files. |
| An item cannot be adapted | Read its reason; check that its target is an accessible EXE and the shortcut is valid. |
| Public desktop access denied | Close Forma, right-click `Forma.exe`, choose **Run as administrator**, approve UAC yourself, rescan, and retry only failed items. Without elevation, select writable shortcuts. Forma never elevates automatically or changes folder permissions. |
| Administrator retry still fails | Check read-only attributes, file locks, and ACL restrictions. If you run as a different Windows account, that account has a separate default data directory and does not automatically see your history. |
| Icon has not refreshed | Wait or refresh manually. Forma does not automatically stop or restart Explorer or delete the global icon cache. |
| Taskbar icon stays unchanged | Taskbar and running application icons are outside the supported scope. |
