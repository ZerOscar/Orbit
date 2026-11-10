using Avalonia.Controls;
using Orbit.App.Controls;

namespace Orbit.App.Views;

public partial class RadialWindow : Window
{
    public RadialWindow() => InitializeComponent();

    public DonutMenu DonutControl => Donut;
}
