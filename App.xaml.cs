using System.IO;
using System.Windows;
using CatalogEntitlementManager.Core.Services;
using CatalogEntitlementManager.Models;
using CatalogEntitlementManager.Storage;
using CatalogEntitlementManager.UI.ViewModels;
using CatalogEntitlementManager.UI.Views;

namespace CatalogEntitlementManager;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var configStore = new JsonConfigStore();
        var manifestClientService = new ManifestClientService(configStore);
        var workspaceProvisionerService = new WorkspaceProvisionerService();
        var launcherProtocolService = new LauncherProtocolService();
        var steamLocatorService = new SteamLocatorService();
        var mainViewModel = new MainViewModel(configStore, manifestClientService,
            workspaceProvisionerService, launcherProtocolService, steamLocatorService);
        MainWindow = new MainWindow(mainViewModel);
        MainWindow.Show();
    }
}