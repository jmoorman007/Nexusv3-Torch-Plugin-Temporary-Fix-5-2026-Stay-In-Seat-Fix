using System;
using HarmonyLib;
using NLog;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using VRage;
using VRage.Game;
using VRage.ObjectBuilders;
using VRageMath;

namespace NexusSeatFix.Patches
{
    /// <summary>
    /// Pure-logging prefix patches that trace the entire deferred pilot-reattach
    /// pipeline on a destination server during a Nexus grid transfer. Behaviourally
    /// inert - every prefix returns void/true, never aborts the original method.
    ///
    /// Enable / disable via <see cref="Enabled"/>. When disabled all prefixes return
    /// immediately so there is zero overhead on a production deployment.
    /// </summary>
    public static class DiagnosticPatches
    {
        // Set to true to re-enable the verbose deferred-attach trace. Useful when
        // a future SE update changes pilot-restore behaviour and we need to see
        // the full Init -> UpdateOnceBeforeFrame -> AttachPilot pipeline.
        public static bool Enabled = false;

        internal static readonly Logger Log = LogManager.GetLogger("NexusSeatFix");

        // Reflective accessors for MyCockpit private fields. Computed once at
        // class-load time; if a field is missing/renamed, the FieldRef is null
        // and we silently skip diagnostics for that field.
        internal static readonly AccessTools.FieldRef<MyCockpit, MyCharacter> SavedPilotRef =
            TryFieldRef<MyCharacter>("m_savedPilot");
        internal static readonly AccessTools.FieldRef<MyCockpit, MyCharacter> PilotRef =
            TryFieldRef<MyCharacter>("m_pilot");
        internal static readonly AccessTools.FieldRef<MyCockpit, bool> DefferAttachRef =
            TryFieldRef<bool>("m_defferAttach");

        private static AccessTools.FieldRef<MyCockpit, T> TryFieldRef<T>(string name)
        {
            try { return AccessTools.FieldRefAccess<MyCockpit, T>(name); }
            catch { return null; }
        }

        internal static string Vec(Vector3D v) =>
            $"({v.X:F1}, {v.Y:F1}, {v.Z:F1})";

        internal static bool IsInside(Vector3D v)
        {
            try { return MySession.IsInsideWorld(v); }
            catch { return true; }
        }

        internal static string Describe(MyCharacter c)
        {
            if (c == null) return "<null>";
            try
            {
                return $"'{c.DisplayName}' [eid={c.EntityId}] inScene={c.InScene} closed={c.Closed} " +
                       $"markedForClose={c.MarkedForClose} pos={Vec(c.PositionComp?.GetPosition() ?? Vector3D.Zero)} " +
                       $"insideWorld={IsInside(c.PositionComp?.GetPosition() ?? Vector3D.Zero)}";
            }
            catch (Exception ex) { return $"<describe-error: {ex.Message}>"; }
        }

        internal static string Describe(MyCockpit c)
        {
            if (c == null) return "<null>";
            try
            {
                return $"'{c.DisplayNameText}' [eid={c.EntityId}] grid='{c.CubeGrid?.DisplayName}' " +
                       $"pos={Vec(c.PositionComp?.GetPosition() ?? Vector3D.Zero)} " +
                       $"insideWorld={IsInside(c.PositionComp?.GetPosition() ?? Vector3D.Zero)}";
            }
            catch (Exception ex) { return $"<describe-error: {ex.Message}>"; }
        }
    }

    [HarmonyPatch(typeof(MyCockpit), "Init",
        new[] { typeof(MyObjectBuilder_CubeBlock), typeof(MyCubeGrid) })]
    public static class MyCockpit_Init_Diag
    {
        [HarmonyPrefix]
        public static void Prefix(MyCockpit __instance, MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            if (!DiagnosticPatches.Enabled) return;
            try
            {
                var cob = objectBuilder as MyObjectBuilder_Cockpit;
                if (cob == null) return;

                var pilotPos = cob.Pilot?.PositionAndOrientation?.Position ?? new SerializableVector3D();
                DiagnosticPatches.Log.Info(
                    "[Diag/Init] Cockpit Init: type='{0}' subtype='{1}' grid='{2}' hasPilotOB={3}{4}",
                    objectBuilder?.GetType().Name,
                    objectBuilder?.SubtypeName,
                    cubeGrid?.DisplayName,
                    cob.Pilot != null,
                    cob.Pilot != null
                        ? $" pilotEntityId={cob.Pilot.EntityId} pilotName='{cob.Pilot.CharacterModel}' " +
                          $"savedPos={DiagnosticPatches.Vec((Vector3D)pilotPos)} " +
                          $"savedPosInsideWorld={DiagnosticPatches.IsInside((Vector3D)pilotPos)}"
                        : "");
            }
            catch (Exception ex)
            {
                DiagnosticPatches.Log.Error(ex, "[Diag/Init] prefix threw");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(MyCockpit __instance)
        {
            if (!DiagnosticPatches.Enabled) return;
            try
            {
                MyCharacter saved = DiagnosticPatches.SavedPilotRef?.Invoke(__instance);
                bool deffer = DiagnosticPatches.DefferAttachRef?.Invoke(__instance) ?? false;
                DiagnosticPatches.Log.Info(
                    "[Diag/Init-post] Cockpit Init done: {0}  m_defferAttach={1} m_savedPilot={2}",
                    DiagnosticPatches.Describe(__instance), deffer, DiagnosticPatches.Describe(saved));
            }
            catch (Exception ex)
            {
                DiagnosticPatches.Log.Error(ex, "[Diag/Init-post] postfix threw");
            }
        }
    }

    [HarmonyPatch(typeof(MyCockpit), "UpdateOnceBeforeFrame")]
    public static class MyCockpit_UpdateOnceBeforeFrame_Diag
    {
        [HarmonyPrefix]
        public static void Prefix(MyCockpit __instance)
        {
            if (!DiagnosticPatches.Enabled) return;
            try
            {
                MyCharacter saved = DiagnosticPatches.SavedPilotRef?.Invoke(__instance);
                if (saved == null) return; // only log when there's actually work to do
                bool deffer = DiagnosticPatches.DefferAttachRef?.Invoke(__instance) ?? false;
                DiagnosticPatches.Log.Info(
                    "[Diag/UpdateOnce] cockpit={0} m_defferAttach={1} m_savedPilot={2}",
                    DiagnosticPatches.Describe(__instance), deffer, DiagnosticPatches.Describe(saved));
            }
            catch (Exception ex)
            {
                DiagnosticPatches.Log.Error(ex, "[Diag/UpdateOnce] prefix threw");
            }
        }
    }

    [HarmonyPatch(typeof(MyCockpit), "OnRegisteredToGridSystems")]
    public static class MyCockpit_OnRegisteredToGridSystems_Diag
    {
        [HarmonyPrefix]
        public static void Prefix(MyCockpit __instance)
        {
            if (!DiagnosticPatches.Enabled) return;
            try
            {
                MyCharacter saved = DiagnosticPatches.SavedPilotRef?.Invoke(__instance);
                if (saved == null) return;
                bool deffer = DiagnosticPatches.DefferAttachRef?.Invoke(__instance) ?? false;
                DiagnosticPatches.Log.Info(
                    "[Diag/OnRegistered] cockpit={0} m_defferAttach={1} m_savedPilot={2}",
                    DiagnosticPatches.Describe(__instance), deffer, DiagnosticPatches.Describe(saved));
            }
            catch (Exception ex)
            {
                DiagnosticPatches.Log.Error(ex, "[Diag/OnRegistered] prefix threw");
            }
        }
    }

    /// <summary>
    /// Diagnostic counterpart to <see cref="AttachPilotPatch"/>. Runs as a separate
    /// postfix so we can observe what AttachPilot did - in particular whether
    /// <c>m_pilot</c> ended up set (success) or left null (the IsInsideWorld bailout).
    /// </summary>
    [HarmonyPatch(typeof(MyCockpit), nameof(MyCockpit.AttachPilot),
        new[] { typeof(MyCharacter), typeof(int), typeof(bool), typeof(bool), typeof(bool) })]
    public static class MyCockpit_AttachPilot_Diag
    {
        [HarmonyPrefix]
        public static void Prefix(MyCockpit __instance, MyCharacter pilot,
            bool storeOriginalPilotWorld, bool calledFromInit, bool merged)
        {
            if (!DiagnosticPatches.Enabled) return;
            try
            {
                DiagnosticPatches.Log.Info(
                    "[Diag/AttachPilot-pre] cockpit={0} pilot={1} storeOrig={2} calledFromInit={3} merged={4}",
                    DiagnosticPatches.Describe(__instance), DiagnosticPatches.Describe(pilot),
                    storeOriginalPilotWorld, calledFromInit, merged);
            }
            catch (Exception ex)
            {
                DiagnosticPatches.Log.Error(ex, "[Diag/AttachPilot-pre] prefix threw");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(MyCockpit __instance, MyCharacter pilot)
        {
            if (!DiagnosticPatches.Enabled) return;
            try
            {
                MyCharacter actualPilot = DiagnosticPatches.PilotRef?.Invoke(__instance);
                bool success = ReferenceEquals(actualPilot, pilot);
                if (success)
                {
                    DiagnosticPatches.Log.Info(
                        "[Diag/AttachPilot-post] SUCCESS: pilot is now attached to cockpit {0}",
                        DiagnosticPatches.Describe(__instance));
                }
                else
                {
                    DiagnosticPatches.Log.Warn(
                        "[Diag/AttachPilot-post] BAILED: AttachPilot returned without attaching pilot. " +
                        "cockpit={0} requested-pilot={1} actual-m_pilot={2}. " +
                        "Likely cause: the new MyCockpit.AttachPilot guards (e.g. !IsInsideWorld) returned early.",
                        DiagnosticPatches.Describe(__instance), DiagnosticPatches.Describe(pilot),
                        DiagnosticPatches.Describe(actualPilot));
                }
            }
            catch (Exception ex)
            {
                DiagnosticPatches.Log.Error(ex, "[Diag/AttachPilot-post] postfix threw");
            }
        }
    }
}
