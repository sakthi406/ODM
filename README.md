# ODM

Jump to any SOP step in Play mode, saved zone QR scans, without walking the normal flow.

ODM is an editor-only Unity package. It finds the app's runtime controllers by type name using reflection, so it never references or modifies the app's own scripts.

## Install

1. In Unity, open **Window > Package Management > Package Manager**.
2. Click **+ > Add package from disk...** and pick this folder's `package.json`.
3. Open **Tools > ODM**.

For a team install, put this folder in its own Git repository (with `package.json` at the root) and use **+ > Add package from git URL...**.

## Steps tab

Enter Play mode and use the app's normal QR, zone, asset, scenario and procedure selection. Once the instructions screen is open, ODM lists the loaded workflow.

- Each sequence is one collapsible row. The sequence the app is currently on opens by itself and follows the app as it moves.
- Click a step to load it through the app's normal step-loading path. Earlier sequences are marked visited and completed for the current Play session.
- The current step is highlighted. **Current** scrolls back to it.
- **Filter steps** matches sequence, subsequence and step names. Steps are found by name, so renumbering never breaks anything.

ODM does not load another SOP file or pick a scenario for you. The list always matches the SOP the app has loaded.

## Zones tab

Save a zone name with its encrypted QR text. Zones are stored in this Unity user's Editor preferences, not in the project. Press the blue play button on a zone to enter its text into the app's `EditorQRSimulator` and run its normal scan. **Import** and **Export** share zones as a JSON file. The file holds the encrypted QR text, so handle it as your team's data policy requires.

## Compatibility

Unity 2022.3 or newer. ODM expects the app's `SOPFlowController`, `QRBackendController`, `EditorQRSimulator` and `UIManager`, and their current private member names. Type names live in `AppTypes` in `Editor/ODMReflect.cs`. If the app's internals change, update the reflection lookups in `Editor/ODMFlow.cs` and `Editor/ODMZones.cs`.

## Code layout

| File | Role |
| --- | --- |
| `ODMWindow.cs` | The window: header, tabs, step list, zones, status bar |
| `ODMFlow.cs` | Reads the loaded workflow, tracks the current step, performs the jump |
| `ODMZones.cs` | Saved zones, import/export, QR simulator bridge |
| `ODMTheme.cs` | Palette, text, vector icons, buttons, text fields |
| `ODMReflect.cs` | Reflection helpers and the app type names |
