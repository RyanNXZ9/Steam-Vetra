using System.Windows;
using CatalogEntitlementManager.UI.Views;
using CatalogEntitlementManager.UI.ViewModels;

namespace CatalogEntitlementManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow(new MainViewModel()).Show();
    }
}

