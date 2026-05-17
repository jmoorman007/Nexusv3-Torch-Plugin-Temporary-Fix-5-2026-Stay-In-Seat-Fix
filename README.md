# NexusSeatFix

A small Torch plugin that restores the "stay in your seat after a Nexus V3 sector / server transfer" behaviour on current Space Engineers builds.

## What goes wrong without this plugin

When a seated player crosses a sector boundary, Nexus V3 emits **two** transports for the same player to the destination server:

1. A `GridTransportMessage` carrying the grid with the pilot embedded in `MyObjectBuilder_Cockpit.Pilot`. On arrival, the cockpit's `Init` recreates the pilot character from the saved OB, the deferred attach fires via `MyCockpit.UpdateOnceBeforeFrame` / `OnRegisteredToGridSystems`, and `AttachPilot` seats the character correctly. *This step works fine.*

2. About **1.5 seconds later**, a separate `CharacterTransportMessage` arrives carrying the player as a free-floating, jetpack-on character. Nexus's `CharacterTransportMessage.TrySpawn` calls `GameUtils.ClearSavedCharacters` (which **closes the seated character entity**) and then `GameUtils.SpawnIntoCharacter` (which **assigns control to the flying duplicate**). The player ends up in the flying character, not the seat.

Nexus already has a guard against this — it logs `"Somehow got character spawn message when player is online. discarding"` — but the guard tests `MyPlayerCollection.IsPlayerOnline`, and at the moment the duplicate arrives the player hasn't connected to the destination yet, so the guard doesn't fire.

## What this plugin does

A single Harmony prefix on `NGPlugin.BoundarySystem.CharacterTransportMessage.TrySpawn`:

1. Looks up the identity for the incoming Steam ID.
2. Checks whether the identity's current character is a non-null, non-closed `MyCharacter` whose `UsingEntity` is a `MyShipController` (cockpit, passenger seat, cryo chamber, remote control).
3. If yes, logs and returns `false` — Nexus's destructive `TrySpawn` is skipped entirely, and the seated state established by the grid transport is preserved.
4. If no, returns `true` and Nexus's `TrySpawn` runs normally.

The plugin also includes a second, **defensive** Harmony prefix on `MyCockpit.AttachPilot` (in `Patches/AttachPilotPatch.cs`). Current Space Engineers builds added an `if (!MySession.IsInsideWorld(pilot.PositionComp.GetPosition())) return;` guard that did not exist in the open-sourced version. In normal Nexus transfers the pilot's saved position is already inside world bounds, so the guard passes and this patch is a no-op. The patch only activates if a future scenario (e.g. mismatched `WorldSizeKm` between two cluster servers) does push the pilot's saved coordinates outside the destination world's bounding cube; in that case it snaps the pilot's `WorldMatrix` to the cockpit's `WorldMatrix` before the guard runs, allowing the deferred attach to proceed.

Verbose diagnostic patches live in `Patches/DiagnosticPatches.cs`. They are disabled by default and can be toggled at runtime from the Torch plugin manager — open the **NexusSeatFix** plugin row and tick **Enable diagnostic logging**. The setting is persisted to `NexusSeatFix.cfg` under the plugin's storage folder (`<Torch>\Instance\NexusSeatFix\` by default) and takes effect immediately without a restart. When on, you get a full trace of every `MyCockpit.Init` → `UpdateOnceBeforeFrame` → `AttachPilot` chain; when off, only the startup-banner lines and the duplicate-transport-discard line are emitted.

## Build

The project targets .NET Framework 4.8. Any modern `dotnet build` works.

```powershell
cd "Space Engineers Spawning - Fixing Nexus\NexusSeatFix"
dotnet build -c Release
```

### Required reference DLLs

The csproj expects the following layout (relative to the project folder):

```
..\TorchAPI Files\
    Torch.dll
    Torch.API.dll
    NLog.dll
    0Harmony.dll                    <-- copy from Nexus Global zip
    DedicatedServer64\
        Sandbox.Game.dll
        Sandbox.Common.dll
        SpaceEngineers.ObjectBuilders.dll
        VRage.dll
        VRage.Game.dll
        VRage.Math.dll
        VRage.Library.dll
```

Only one file needs to be staged manually: copy **`0Harmony.dll`** out of `Nexus TorchAPI Plugin\Nexus Global.zip` into `TorchAPI Files\`. Everything else is already in the right place in this repo.

If your TorchAPI / DedicatedServer64 folders live elsewhere, edit the `<TorchApiDir>` and `<SeBinDir>` properties in `NexusSeatFix.csproj`.

## Install

On every dedicated server in the Nexus cluster that runs Torch + Nexus Global:

1. Build the plugin (above). Output is `bin\Release\NexusSeatFix.dll` plus `manifest.xml`.
2. Zip those two files together as `NexusSeatFix.zip` (no wrapper folder — they must be at the zip root).
3. Drop the zip into your Torch `Plugins\` folder, next to `Nexus Global.zip`.
4. Restart Torch.

On startup you should see:

```
[NexusSeatFix] Patching NGPlugin.BoundarySystem.CharacterTransportMessage.TrySpawn with duplicate-transport guard.
[NexusSeatFix] Patched MyCockpit.AttachPilot prefix. Pilots transferring into seated grids across Nexus sectors should now stay in their seats.
```

When the duplicate-transport guard actually fires during a seated transfer:

```
[NexusSeatFix] Discarding duplicate CharacterTransportMessage for steamId=<id>:
               identity '<name>' is already seated in '<cockpit name>' on grid '<grid name>'.
               The grid transport already restored the seated state.
```

If you don't see that line during a seated transfer, the seat-restore is working through some other path (or wasn't broken to begin with on that particular hop).

## Uninstall

Remove `NexusSeatFix.zip` from the Torch `Plugins\` folder and restart Torch. The plugin makes no persistent state changes — no DB writes, no config files, no save-file mutations.

## Compatibility

- Requires Harmony, which is already bundled by Nexus Global. **Do not** ship a second copy of `0Harmony.dll` inside this plugin's zip.
- Targets the current Space Engineers `MyCockpit.AttachPilot(MyCharacter, int, bool, bool, bool)` signature. If Keen changes the signature again, Harmony will fail to find the target method at startup, the plugin will log the failure, and the rest of the server will keep running.
- The `CharacterTransportMessage.TrySpawn` patch resolves its target by reflection (`AccessTools.TypeByName`), so this plugin builds and loads even when Nexus is not installed (in which case it does nothing).
- Independent of Nexus version — patches the lowest-cost surface (one method on Keen, one method on Nexus). If either method's signature changes the patch becomes inert with a startup log message; it never breaks the rest of the load.

## Diagnosis source files

For the full diagnosis trail and IL anchors, see the parent folder:

- `Space Engineers (OLD) Source\SpaceEngineers-master\Sources\Sandbox.Game\Game\Entities\Blocks\MyCockpit.cs` — original `AttachPilot` (no IsInsideWorld guard), lines 1102-1191
- `D:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64\Sandbox.Game.dll` — current `MyCockpit.AttachPilot` at rva `0x2e2610`, with the new IsInsideWorld guards
- `Nexus TorchAPI Plugin\Nexus Global.zip\NGPlugin.dll` — `NGPlugin.BoundarySystem.CharacterTransportMessage.TrySpawn` at rva `0x1fa98` and `NGPlugin.BoundarySystem.GridTransportMessage.SpawnCharactersInGrid` at rva `0x20dac`
- `Nexus TorchAPI Plugin\Nexus Global.zip\SeamlessClient.dll` — `ServerSwitcherV2.StartEntitySync` (client-side `MyPlayerCollection.RequestLocalRespawn` invocation)
