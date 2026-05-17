using System;
using HarmonyLib;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using VRageMath;

namespace NexusSeatFix.Patches
{
    /// <summary>
    /// Repairs the "character not in seat after Nexus sector/server transfer" regression.
    ///
    /// Current Space Engineers builds added two new bail-out guards at the very top of
    /// <see cref="MyCockpit.AttachPilot"/>:
    ///
    /// <code>
    ///   if (!MySession.IsInsideWorld(pilot.PositionComp.GetPosition())) return;
    ///   if (!MySession.IsInsideWorld(this .PositionComp.GetPosition())) return;
    /// </code>
    ///
    /// These guards are NOT present in the open-sourced (OLD) version of MyCockpit.cs
    /// against which Nexus V3 was written. After Nexus deserialises a grid on the
    /// destination server, the cockpit's <c>Init</c> rebuilds the pilot character from
    /// the saved <c>MyObjectBuilder_Cockpit.Pilot</c> object builder, which carries the
    /// pilot's absolute world position from the source server. When the destination
    /// world has a smaller bounding cube than the source position, the first guard
    /// fires, <c>AttachPilot</c> silently returns, and the pilot is never re-seated.
    /// Nexus's own <c>GridTransportMessage.SpawnCharactersInGrid</c> then binds the
    /// identity to that orphan character and the player ends up controlled but free-
    /// floating next to the grid.
    ///
    /// This prefix snaps the pilot's WorldMatrix to the cockpit's WorldMatrix when
    /// (and only when) the pilot's current position is outside the world. That makes
    /// the guard pass; AttachPilot's own body sets the matrix again a few lines later
    /// using exactly the same source argument, so the snap is a harmless pre-position.
    /// </summary>
    [HarmonyPatch(typeof(MyCockpit), nameof(MyCockpit.AttachPilot),
        new[] { typeof(MyCharacter), typeof(int), typeof(bool), typeof(bool), typeof(bool) })]
    public static class AttachPilotPatch
    {
        private static readonly Logger Log = LogManager.GetLogger("NexusSeatFix");

        // Per-process flag so we don't spam the log if some bizarre case loops.
        private static int _snapCount;

        [HarmonyPrefix]
        public static void Prefix(MyCockpit __instance, MyCharacter pilot)
        {
            if (__instance == null || pilot == null) return;
            if (pilot.PositionComp == null) return;

            try
            {
                Vector3D pilotPos = pilot.PositionComp.GetPosition();

                // Fast path: pilot is already inside world bounds. The vanilla guard
                // will pass; we have nothing to do.
                if (MySession.IsInsideWorld(pilotPos)) return;

                // The pilot was likely deserialised from a saved object builder whose
                // absolute world position came from a different (larger) sector. Snap
                // the pilot to the cockpit's WorldMatrix - the same operation
                // AttachPilot itself performs at line 1128 of the OLD source, just
                // moved ahead of the new IsInsideWorld guard. Use the cockpit as the
                // `source` so the position component knows this is a deliberate move.
                MatrixD seatMatrix = __instance.WorldMatrix;

                // Be defensive: if for any reason the cockpit itself is out of world,
                // there is nothing we can do here - log and let vanilla bail.
                if (!MySession.IsInsideWorld(seatMatrix.Translation))
                {
                    Log.Warn(
                        "[NexusSeatFix] Cockpit '{0}' is itself outside world bounds at {1}; cannot reseat pilot '{2}'. " +
                        "Likely the grid spawned at an unintended location on the destination server.",
                        __instance.DisplayNameText, seatMatrix.Translation, pilot.DisplayName);
                    return;
                }

                pilot.PositionComp.SetWorldMatrix(ref seatMatrix, __instance);

                // Only log the first handful per session - this can fire many times in
                // a row when a fleet of seated grids transfers together.
                if (System.Threading.Interlocked.Increment(ref _snapCount) <= 10)
                {
                    Log.Info(
                        "[NexusSeatFix] Snapped pilot '{0}' from out-of-world position {1} to cockpit '{2}' at {3} " +
                        "to bypass MyCockpit.AttachPilot's IsInsideWorld guard.",
                        pilot.DisplayName, pilotPos, __instance.DisplayNameText, seatMatrix.Translation);
                }
            }
            catch (Exception ex)
            {
                // Never let our patch throw out of a prefix - that would corrupt the
                // pilot-attach state worse than the bug we are fixing.
                Log.Error(ex, "[NexusSeatFix] Prefix threw; letting vanilla AttachPilot run unmodified.");
            }
        }
    }
}
