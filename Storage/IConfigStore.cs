using CatalogEntitlementManager.Models;

namespace CatalogEntitlementManager.Storage;

public interface IConfigStore
{
    AppConfig Load();
    void Save(AppConfig config);
    string ConfigFilePath { get; }
}