using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using HarmonyLib;
using NexusSeatFix.Config;
using NexusSeatFix.Patches;
using NexusSeatFix.UI;
using NLog;
using Torch;
using Torch.API;
using Torch.API.Plugins;

namespace NexusSeatFix
{
    /// <summary>
    /// Standalone Torch plugin that restores seated-character state after a Nexus V3
    /// sector / server transfer in current Space Engineers builds. See
    /// <see cref="NexusSeatFix.Patches.AttachPilotPatch"/> and
    /// <see cref="NexusSeatFix.Patches.CharacterTransportPatch"/> for the diagnosis
    /// and the actual patch logic.
    ///
    /// Plugin metadata (Name, Guid, Version) is defined in <c>manifest.xml</c>; the
    /// current Torch loader discovers this class by inheritance from
    /// <see cref="TorchPluginBase"/> and no longer uses the obsolete
    /// <c>PluginAttribute</c>.
    /// </summary>
    public sealed class NexusSeatFixPlugin : TorchPluginBase, IWpfPlugin
    {
        private static readonly Logger Log = LogManager.GetLogger("NexusSeatFix");

        private Harmony _harmony;
        private Persistent<NexusSeatFixConfig> _config;
        private NexusSeatFixControl _control;

        /// <summary>Live config object. Bound to the WPF UI; persisted to disk.</summary>
        public NexusSeatFixConfig Config => _config?.Data;

        public override void Init(ITorchBase torch)
        {
            base.Init(torch);

            // Load (or create) the persisted config file under the plugin's
            // StoragePath. StoragePath defaults to <Torch>\Instance\<plugin-guid>\.
            try
            {
                var configPath = Path.Combine(StoragePath, "NexusSeatFix.cfg");
                _config = Persistent<NexusSeatFixConfig>.Load(configPath);

                // Push the initial value into the diagnostic patches, and keep
                // them in sync when the operator toggles the checkbox at runtime.
                DiagnosticPatches.Enabled = _config.Data.DiagnosticsEnabled;
                _config.Data.PropertyChanged += OnConfigChanged;

                Log.Info(
                    "[NexusSeatFix] Config loaded from '{0}'. DiagnosticsEnabled={1}.",
                    configPath, _config.Data.DiagnosticsEnabled);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[NexusSeatFix] Failed to load config; diagnostics will stay disabled.");
                DiagnosticPatches.Enabled = false;
            }

            // Apply all Harmony patches in this assembly.
            try
            {
                _harmony = new Harmony("com.jamesmoorman.nexusseatfix");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());

                Log.Info(
                    "[NexusSeatFix] Patched MyCockpit.AttachPilot prefix. Pilots transferring " +
                    "into seated grids across Nexus sectors should now stay in their seats.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[NexusSeatFix] Failed to apply Harmony patches. Plugin is inert.");
            }
        }

        private void OnConfigChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(NexusSeatFixConfig.DiagnosticsEnabled)) return;
            try
            {
                DiagnosticPatches.Enabled = _config.Data.DiagnosticsEnabled;
                Log.Info(
                    "[NexusSeatFix] Diagnostics {0} via plugin UI.",
                    _config.Data.DiagnosticsEnabled ? "ENABLED" : "DISABLED");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[NexusSeatFix] Error applying diagnostics toggle.");
            }
        }

        /// <summary>
        /// Called by Torch when the plugin row is selected in the plugin manager.
        /// Returns a user control bound to <see cref="Config"/>.
        /// </summary>
        public UserControl GetControl()
        {
            return _control ?? (_control = new NexusSeatFixControl(Config));
        }

        public override void Dispose()
        {
            try
            {
                if (_config?.Data != null)
                    _config.Data.PropertyChanged -= OnConfigChanged;
                _config?.Save();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[NexusSeatFix] Error saving config on dispose.");
            }

            try
            {
                _harmony?.UnpatchAll(_harmony.Id);
                Log.Info("[NexusSeatFix] Unpatched cleanly.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[NexusSeatFix] Error during unpatch.");
            }
            finally
            {
                _harmony = null;
                base.Dispose();
            }
        }
    }
}
