using System.Windows;
using Blastlog.Playnite.Ui;

namespace Blastlog.Playnite.Screens {
/// <summary>Dashboard (design.md 8 "Dashboard"). For now the earlier plain view; the Blastlog-styled dashboard replaces it.</summary>
public sealed class DashboardScreen : IBlastlogScreen {
  public string Title => "Dashboard";
  public string Icon => Icons.Dashboard;
  public int Order => 10;
  public FrameworkElement Create(BlastlogPlugin plugin) => new DashboardView(plugin);
}
}
