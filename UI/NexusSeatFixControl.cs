using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using NexusSeatFix.Config;

namespace NexusSeatFix.UI
{
    /// <summary>
    /// The user control shown in the Torch plugin manager when NexusSeatFix is
    /// selected. Built in pure code (no XAML) to keep the build pipeline simple -
    /// no need to flip <c>UseWPF</c>/<c>Page</c> build actions or worry about
    /// generated <c>g.cs</c> files.
    /// </summary>
    public sealed class NexusSeatFixControl : UserControl
    {
        public NexusSeatFixControl(NexusSeatFixConfig config)
        {
            DataContext = config;
            Content = BuildLayout();
        }

        private static UIElement BuildLayout()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(12),
            };

            // Title
            root.Children.Add(new TextBlock
            {
                Text = "NexusSeatFix",
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 4),
            });

            // Description
            root.Children.Add(new TextBlock
            {
                Text = "Restores the \"stay in your seat\" behaviour for Nexus V3 sector / server transfers " +
                       "on current Space Engineers builds.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = Brushes.Gray,
            });

            // Section header
            root.Children.Add(new TextBlock
            {
                Text = "Diagnostics",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 4),
            });

            // The toggle itself. Two-way bound to NexusSeatFixConfig.DiagnosticsEnabled.
            var checkbox = new CheckBox
            {
                Content = "Enable diagnostic logging",
                Margin = new Thickness(0, 2, 0, 0),
            };
            checkbox.SetBinding(ToggleButton_IsCheckedProperty,
                new Binding(nameof(NexusSeatFixConfig.DiagnosticsEnabled))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                });
            root.Children.Add(checkbox);

            root.Children.Add(new TextBlock
            {
                Text = "When on, every MyCockpit.Init, UpdateOnceBeforeFrame and AttachPilot call is " +
                       "logged with full pilot/cockpit state. Use this when investigating a pilot-restore " +
                       "regression; the noise is significant on a busy server.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 2, 0, 8),
                Foreground = Brushes.Gray,
                FontSize = 11,
            });

            // Footnote
            root.Children.Add(new TextBlock
            {
                Text = "Changes take effect immediately and are persisted to NexusSeatFix.cfg in the " +
                       "Torch plugin storage folder.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                FontSize = 11,
            });

            return root;
        }

        // CheckBox.IsCheckedProperty lives on ToggleButton, the base class.
        // Aliasing it for readability and to avoid the long fully-qualified path
        // in the SetBinding call above.
        private static readonly DependencyProperty ToggleButton_IsCheckedProperty
            = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;
    }
}
