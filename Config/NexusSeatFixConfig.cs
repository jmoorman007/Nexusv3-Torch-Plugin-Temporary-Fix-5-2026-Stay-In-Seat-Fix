using System.Xml.Serialization;
using Torch;
using Torch.Views;

namespace NexusSeatFix.Config
{
    /// <summary>
    /// Persisted plugin configuration. Serialised to <c>NexusSeatFix.cfg</c> in
    /// the Torch plugin storage folder via <see cref="Torch.Persistent{T}"/>.
    ///
    /// Inherits <see cref="ViewModel"/> so WPF bindings see property-change
    /// notifications and so <see cref="Torch.Persistent{T}"/> auto-saves on edit.
    /// </summary>
    public sealed class NexusSeatFixConfig : ViewModel
    {
        private bool _diagnosticsEnabled;

        [Display(
            Name = "Diagnostic logging",
            Description = "When enabled, log the full MyCockpit.Init / UpdateOnceBeforeFrame / " +
                          "AttachPilot pipeline. Useful for diagnosing pilot-restore regressions; " +
                          "verbose on busy servers.",
            GroupName = "Diagnostics",
            Order = 1)]
        [XmlElement("DiagnosticsEnabled")]
        public bool DiagnosticsEnabled
        {
            get => _diagnosticsEnabled;
            set => SetValue(ref _diagnosticsEnabled, value);
        }
    }
}
