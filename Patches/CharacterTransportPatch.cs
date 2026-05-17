using System;
using System.Reflection;
using HarmonyLib;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using VRage.Game.Entity;

namespace NexusSeatFix.Patches
{
    /// <summary>
    /// Prevents Nexus from overwriting a freshly-seated pilot with a duplicate
    /// CharacterTransportMessage that arrives shortly after a GridTransportMessage.
    ///
    /// Observed flow on the destination server during a seated sector transfer:
    ///
    /// <code>
    ///   t+0.0s   GridTransportMessage.SpawnCharactersInGrid: pilot attached to cockpit (SUCCESS)
    ///   t+1.5s   TransportSync: Received character spawning message!
    ///            CharacterTransportMessage.TrySpawn:
    ///              -> GameUtils.ClearSavedCharacters    (CLOSES the seated character entity)
    ///              -> GameUtils.SpawnIntoCharacter      (assigns control to a free-flying character)
    ///   result: player ends up in the flying character, NOT in the seat
    /// </code>
    ///
    /// Nexus already guards <c>TrySpawn</c> with an <c>IsPlayerOnline</c> check, but
    /// the player has not yet connected to the destination at the moment the
    /// duplicate message arrives, so the guard does not fire. This prefix adds a
    /// second guard: if the player's identity already has a character that is
    /// currently <c>IsUsing</c> a <see cref="MyShipController"/> (cockpit, seat,
    /// cryo chamber, remote control), the duplicate transport is discarded.
    ///
    /// We resolve the target by reflection (<see cref="AccessTools.TypeByName"/>)
    /// so the plugin builds without a hard reference on NGPlugin.dll. If Nexus is
    /// not loaded, <see cref="TargetMethod"/> returns null and Harmony skips this
    /// patch class silently.
    /// </summary>
    [HarmonyPatch]
    public static class CharacterTransportPatch
    {
        internal static readonly Logger Log = LogManager.GetLogger("NexusSeatFix");

        // Reflective handles, resolved once in TargetMethod and reused in Prefix.
        private static FieldInfo _playerField;       // CharacterTransportMessage.Player : PlayerItem
        private static PropertyInfo _steamIdProp;    // PlayerItem.SteamID : ulong

        public static MethodBase TargetMethod()
        {
            var msgType = AccessTools.TypeByName("NGPlugin.BoundarySystem.CharacterTransportMessage");
            if (msgType == null)
            {
                Log.Warn(
                    "[NexusSeatFix] NGPlugin.BoundarySystem.CharacterTransportMessage not found; " +
                    "duplicate-transport guard will not be applied. Is Nexus installed?");
                return null;
            }

            _playerField = AccessTools.Field(msgType, "Player");
            if (_playerField != null)
                _steamIdProp = AccessTools.Property(_playerField.FieldType, "SteamID");

            if (_playerField == null || _steamIdProp == null)
            {
                Log.Warn(
                    "[NexusSeatFix] Could not resolve CharacterTransportMessage.Player or PlayerItem.SteamID. " +
                    "Field/property layout may have changed. Duplicate-transport guard is disabled.");
                return null;
            }

            var target = AccessTools.Method(msgType, "TrySpawn");
            if (target == null)
            {
                Log.Warn("[NexusSeatFix] CharacterTransportMessage.TrySpawn method not found.");
                return null;
            }

            Log.Info(
                "[NexusSeatFix] Patching {0}.{1} with duplicate-transport guard.",
                msgType.FullName, target.Name);
            return target;
        }

        [HarmonyPrefix]
        public static bool Prefix(object __instance)
        {
            try
            {
                if (__instance == null || _playerField == null || _steamIdProp == null)
                    return true; // let original run

                var playerItem = _playerField.GetValue(__instance);
                if (playerItem == null) return true;

                ulong steamId = (ulong)_steamIdProp.GetValue(playerItem);
                if (steamId == 0) return true;

                var players = MySession.Static?.Players;
                if (players == null) return true;

                // The (ulong steamId, int serialId) overload of TryGetPlayerIdentity
                // is the simplest path; serialId 0 is the main human controller.
                MyIdentity identity = players.TryGetPlayerIdentity(steamId, 0);
                if (identity == null) return true;

                MyCharacter currentChar = identity.Character;
                if (currentChar == null || currentChar.MarkedForClose || currentChar.Closed)
                    return true;

                // A character is "in a seat" iff UsingEntity is a MyShipController
                // (cockpit, passenger seat, cryo chamber, remote control, etc).
                // (Note: OLD source used MyCharacter.IsUsing; current SE renamed
                // the property to UsingEntity.)
                var controller = currentChar.UsingEntity as MyShipController;
                if (controller == null)
                    return true; // not seated -> let Nexus's TrySpawn run normally

                Log.Info(
                    "[NexusSeatFix] Discarding duplicate CharacterTransportMessage for steamId={0}: " +
                    "identity '{1}' is already seated in '{2}' on grid '{3}'. " +
                    "The grid transport already restored the seated state.",
                    steamId,
                    identity.DisplayName,
                    (controller as MyEntity)?.DisplayNameText ?? "?",
                    (controller as MyCubeBlock)?.CubeGrid?.DisplayName ?? "?");

                return false; // skip original TrySpawn entirely
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[NexusSeatFix] CharacterTransportPatch.Prefix threw; letting Nexus TrySpawn run unmodified.");
                return true;
            }
        }
    }
}
