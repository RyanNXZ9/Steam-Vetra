using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CatalogEntitlementManager.Core.Services;
using CatalogEntitlementManager.Models;
using CatalogEntitlementManager.Storage;

namespace CatalogEntitlementManager.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IConfigStore _configStore;
    private readonly IManifestClientService _manifestClientService;
    private readonly IWorkspaceProvisionerService _workspaceProvisionerService;
    private readonly ILauncherProtocolService _launcherProtocolService;
    private readonly ISteamLocatorService _steamLocatorService;
    private readonly IGameFixService _gameFixService;

    private string _searchQuery = string.Empty;
    private string _selectedFilter = "All";
    private bool _isSettingsDrawerOpen;
    private bool _isFixesModalOpen;
    private TitleCardViewModel? _selectedFixTitle;
    private string _selectedFixInstallDirectory = string.Empty;
    private string _fixStatusMessage = string.Empty;
    private bool _isFixBusy;
    private bool _hasAppliedFixOnDisk;

    private string _targetBaseDirectory = string.Empty;
    private string _luaPluginRelativeDirectory = @"config\stplug-in";
    private string _depotCacheRelativeDirectory = @"depotcache";
    private bool _mirrorManifestsToConfigDepotCache = true;
    private string _manifestEndpointUrl = "https://generator.ryuu.lol/api/download/{appId}";
    private string _manifestBranch = "public";
    private bool _useOfflineManifestGenerator;
    private bool _restartSteamAfterProvision = true;
    private string _steamProcessStatusText = "Steam: Offline";
    private bool _isSteamRunning;
    private string _steamLibraryStatusText = "Library: Ready";
    private bool _isSteamLibraryReady;

    public ObservableCollection<TitleCardViewModel> AllTitles { get; } = new();
    public ObservableCollection<TitleCardViewModel> FilteredTitles { get; } = new();
    public ObservableCollection<SteamStoreGame> StoreGames { get; } = new();
    public ObservableCollection<OperationLogEntry> OperationLogs { get; } = new();
    public ObservableCollection<GameFixDto> AvailableFixes { get; } = new();

    public IReadOnlyList<string> FilterOptions { get; } = new[] { "All", "In Library", "Available" };

    public IReadOnlyList<string> Themes { get; } = new[] { "Glass", "GTA 6", "CodeNexus" };
    private string _selectedTheme = "Glass";
    public string SelectedTheme
    {
        get => _selectedTheme;
        set => SetProperty(ref _selectedTheme, Themes.Contains(value) ? value : "Glass");
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyCatalogFilter();
            }
        }
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                ApplyCatalogFilter();
            }
        }
    }

    public bool IsSettingsDrawerOpen
    {
        get => _isSettingsDrawerOpen;
        set => SetProperty(ref _isSettingsDrawerOpen, value);
    }

    public bool IsFixesModalOpen
    {
        get => _isFixesModalOpen;
        set => SetProperty(ref _isFixesModalOpen, value);
    }

    public TitleCardViewModel? SelectedFixTitle
    {
        get => _selectedFixTitle;
        set => SetProperty(ref _selectedFixTitle, value);
    }

    public string SelectedFixInstallDirectory
    {
        get => _selectedFixInstallDirectory;
        set
        {
            if (SetProperty(ref _selectedFixInstallDirectory, value))
            {
                HasAppliedFixOnDisk = _gameFixService.GetAppliedFixIds(value).Count > 0;
            }
        }
    }

    public string FixStatusMessage
    {
        get => _fixStatusMessage;
        set => SetProperty(ref _fixStatusMessage, value);
    }

    public bool IsFixBusy
    {
        get => _isFixBusy;
        set => SetProperty(ref _isFixBusy, value);
    }

    public bool HasAppliedFixOnDisk
    {
        get => _hasAppliedFixOnDisk;
        set => SetProperty(ref _hasAppliedFixOnDisk, value);
    }

    public string TargetBaseDirectory
    {
        get => _targetBaseDirectory;
        set
        {
            if (SetProperty(ref _targetBaseDirectory, value))
            {
                OnPropertyChanged(nameof(BaseDirectoryBadgeText));
                RefreshSteamProcessStatus();
            }
        }
    }

    public string LuaPluginRelativeDirectory
    {
        get => _luaPluginRelativeDirectory;
        set
        {
            if (SetProperty(ref _luaPluginRelativeDirectory, value))
            {
                RefreshSteamProcessStatus();
            }
        }
    }

    public string DepotCacheRelativeDirectory
    {
        get => _depotCacheRelativeDirectory;
        set
        {
            if (SetProperty(ref _depotCacheRelativeDirectory, value))
            {
                RefreshSteamProcessStatus();
            }
        }
    }

    public bool MirrorManifestsToConfigDepotCache
    {
        get => _mirrorManifestsToConfigDepotCache;
        set => SetProperty(ref _mirrorManifestsToConfigDepotCache, value);
    }

    public string BaseDirectoryBadgeText => string.IsNullOrWhiteSpace(TargetBaseDirectory)
        ? "Steam Dir: Unconfigured"
        : $"Steam: {TargetBaseDirectory}";

    public string ManifestEndpointUrl
    {
        get => _manifestEndpointUrl;
        set => SetProperty(ref _manifestEndpointUrl, value);
    }

    public string ManifestBranch
    {
        get => _manifestBranch;
        set => SetProperty(ref _manifestBranch, value);
    }

    public bool UseOfflineManifestGenerator
    {
        get => _useOfflineManifestGenerator;
        set => SetProperty(ref _useOfflineManifestGenerator, value);
    }

    public bool RestartSteamAfterProvision
    {
        get => _restartSteamAfterProvision;
        set => SetProperty(ref _restartSteamAfterProvision, value);
    }

    public string SteamProcessStatusText
    {
        get => _steamProcessStatusText;
        set => SetProperty(ref _steamProcessStatusText, value);
    }

    public bool IsSteamRunning
    {
        get => _isSteamRunning;
        set => SetProperty(ref _isSteamRunning, value);
    }

    public string SteamLibraryStatusText
    {
        get => _steamLibraryStatusText;
        set => SetProperty(ref _steamLibraryStatusText, value);
    }

    public bool IsSteamLibraryReady
    {
        get => _isSteamLibraryReady;
        set => SetProperty(ref _isSteamLibraryReady, value);
    }

    public RelayCommand PrimaryCardActionCommand { get; }
    public RelayCommand TriggerSteamRunCommand { get; }
    public RelayCommand ToggleSettingsDrawerCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand AutoDetectSteamPathCommand { get; }
    public RelayCommand RestartSteamNowCommand { get; }
    public RelayCommand AddCustomAppIdCommand { get; }
    public RelayCommand AddStoreGameToCatalogCommand { get; }
    public RelayCommand RequestGeneratorUpdateCommand { get; }
    public RelayCommand OpenFixesModalCommand { get; }
    public RelayCommand CloseFixesModalCommand { get; }
    public RelayCommand ApplyOnlineFixCommand { get; }
    public RelayCommand RevertAppliedFixCommand { get; }
    public RelayCommand RunSteamlessDrmCommand { get; }
    public RelayCommand OpenRyuuFixesCatalogCommand { get; }

    public MainViewModel(
        IConfigStore configStore,
        IManifestClientService manifestClientService,
        IWorkspaceProvisionerService workspaceProvisionerService,
        ILauncherProtocolService launcherProtocolService,
        ISteamLocatorService steamLocatorService,
        IGameFixService? gameFixService = null)
    {
        _configStore = configStore;
        _manifestClientService = manifestClientService;
        _workspaceProvisionerService = workspaceProvisionerService;
        _launcherProtocolService = launcherProtocolService;
        _steamLocatorService = steamLocatorService;
        _gameFixService = gameFixService ?? new GameFixService(configStore);

        PrimaryCardActionCommand = new RelayCommand(async param => await ExecutePrimaryCardActionAsync(param as TitleCardViewModel));
        TriggerSteamRunCommand = new RelayCommand(param => ExecuteSteamRun(param as TitleCardViewModel));
        ToggleSettingsDrawerCommand = new RelayCommand(_ => IsSettingsDrawerOpen = !IsSettingsDrawerOpen);
        SaveSettingsCommand = new RelayCommand(_ => SaveConfiguration());
        AutoDetectSteamPathCommand = new RelayCommand(_ => DetectSteamDirectory());
        RestartSteamNowCommand = new RelayCommand(async _ => await RestartSteamNowAsync());
        AddCustomAppIdCommand = new RelayCommand(async _ => await AddCustomAppIdFromSearchAsync());
        AddStoreGameToCatalogCommand = new RelayCommand(param => AddStoreGameToCatalog(param as SteamStoreGame));
        RequestGeneratorUpdateCommand = new RelayCommand(async param => await RequestGeneratorUpdateAsync(param as TitleCardViewModel));
        OpenFixesModalCommand = new RelayCommand(async param => await OpenFixesModalAsync(param as TitleCardViewModel));
        CloseFixesModalCommand = new RelayCommand(_ => IsFixesModalOpen = false);
        ApplyOnlineFixCommand = new RelayCommand(async param => await ApplyOnlineFixAsync(param as GameFixDto));
        RevertAppliedFixCommand = new RelayCommand(async _ => await RevertAppliedFixAsync());
        RunSteamlessDrmCommand = new RelayCommand(async _ => await RunSteamlessDrmAsync());
        OpenRyuuFixesCatalogCommand = new RelayCommand(_ =>
        {
            if (SelectedFixTitle is not null)
            {
                _gameFixService.OpenRyuuFixesCatalog(SelectedFixTitle.AppId);
            }
        });

        InitializeState();
    }

    private void InitializeState()
    {
        var config = _configStore.Load();
        SelectedTheme = config.Theme;
        LuaPluginRelativeDirectory = string.IsNullOrWhiteSpace(config.LuaPluginRelativeDirectory) ? @"config\stplug-in" : config.LuaPluginRelativeDirectory;
        DepotCacheRelativeDirectory = string.IsNullOrWhiteSpace(config.DepotCacheRelativeDirectory) ? "depotcache" : config.DepotCacheRelativeDirectory;
        MirrorManifestsToConfigDepotCache = config.MirrorManifestsToConfigDepotCache;
        ManifestEndpointUrl = string.IsNullOrWhiteSpace(config.ManifestEndpointUrl)
            ? "https://generator.ryuu.lol/api/download/{appId}"
            : config.ManifestEndpointUrl;
        ManifestBranch = string.IsNullOrWhiteSpace(config.ManifestBranch) ? "public" : config.ManifestBranch;
        UseOfflineManifestGenerator = config.UseOfflineManifestGenerator;
        RestartSteamAfterProvision = config.RestartSteamAfterProvision;

        if (!string.IsNullOrWhiteSpace(config.TargetBaseDirectory))
        {
            TargetBaseDirectory = config.TargetBaseDirectory;
        }
        else
        {
            var detected = _steamLocatorService.DetectSteamInstallPath();
            TargetBaseDirectory = detected ?? Path.Combine(AppContext.BaseDirectory, "Workspace");
            config.TargetBaseDirectory = TargetBaseDirectory;
            _configStore.Save(config);
        }

        var envStatus = _steamLocatorService.InspectSteamEnvironment(
            TargetBaseDirectory,
            LuaPluginRelativeDirectory,
            DepotCacheRelativeDirectory);

        AllTitles.Clear();
        foreach (var item in config.Catalog)
        {
            var isProvisioned = envStatus.ProvisionedAppIds.Contains(item.AppId);
            AllTitles.Add(new TitleCardViewModel(item, isProvisioned));
        }

        RefreshSteamProcessStatus();
        ApplyCatalogFilter();

        AppendLog("INFO", "Init", $"Loaded {AllTitles.Count} catalog titles. Lua Tools integration ready.");


    }

    public void RefreshSteamProcessStatus()
    {
        if (_steamLocatorService.IsSteamProcessRunning(out var pid))
        {
            IsSteamRunning = true;
            SteamProcessStatusText = $"Steam Client Online (PID {pid})";
        }
        else
        {
            IsSteamRunning = false;
            SteamProcessStatusText = "Steam Client Offline";
        }

        var envStatus = _steamLocatorService.InspectSteamEnvironment(
            TargetBaseDirectory,
            LuaPluginRelativeDirectory,
            DepotCacheRelativeDirectory);

        IsSteamLibraryReady = envStatus.IsLibraryReady;
        SteamLibraryStatusText = envStatus.LibraryDetail;

        foreach (var card in AllTitles)
        {
            card.IsReadyInLibrary = envStatus.ProvisionedAppIds.Contains(card.AppId);
        }
    }

    public void ApplyCatalogFilter()
    {
        FilteredTitles.Clear();
        var query = (SearchQuery ?? string.Empty).Trim();

        foreach (var card in AllTitles)
        {
            var matchesFilter = SelectedFilter switch
            {
                "In Library" => card.IsReadyInLibrary,
                "Available" => !card.IsReadyInLibrary,
                _ => true
            };

            if (!matchesFilter)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(query))
            {
                var matchesTitle = card.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
                var matchesAppId = card.AppId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
                var matchesGenre = card.Genre.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!matchesTitle && !matchesAppId && !matchesGenre)
                {
                    continue;
                }
            }

            FilteredTitles.Add(card);
        }

        StoreGames.Clear();
        if (SelectedFilter == "All")
        {
            foreach (var game in SteamStoreGame.Featured)
            {
                if (!AllTitles.Any(card => card.AppId == game.AppId)
                    && (game.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || game.Genre.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || game.AppId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)))
                {
                    StoreGames.Add(game);
                }
            }
        }
    }

    private async Task ExecutePrimaryCardActionAsync(TitleCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        await ProvisionAndStartDownloadAsync(card);
    }

    private async Task ProvisionAndStartDownloadAsync(TitleCardViewModel card)
    {
        if (card.IsProvisioning)
        {
            return;
        }

        try
        {
            card.IsProvisioning = true;
            AppendLog("INFO", "Library", $"Preparing Steam download metadata for {card.Title} (AppID {card.AppId})...");

            var manifest = await _manifestClientService.FetchManifestAsync(card.AppId);

            _steamLocatorService.EnsureSilentLibraryDirectories(
                TargetBaseDirectory,
                LuaPluginRelativeDirectory,
                DepotCacheRelativeDirectory,
                MirrorManifestsToConfigDepotCache);

            var unlockerWasInstalled = _steamLocatorService
                .InspectSteamEnvironment(TargetBaseDirectory, LuaPluginRelativeDirectory, DepotCacheRelativeDirectory)
                .IsUnlockerInstalled;

            if (!unlockerWasInstalled)
            {
                await _steamLocatorService.EnsureUnlockerInstalledAsync(
                    TargetBaseDirectory,
                    msg => AppendLog("INFO", "Library", msg));
            }

            var provisionResult = await _workspaceProvisionerService.ProvisionFilesAsync(
                card.AppId,
                TargetBaseDirectory,
                manifest);

            if (provisionResult.Success)
            {
                card.IsReadyInLibrary = true;
                AppendLog("INFO", "Library", $"Registered {card.Title} (AppID {card.AppId}) in local Steam configuration.");

                RefreshSteamProcessStatus();

                if (RestartSteamAfterProvision || !unlockerWasInstalled || !IsSteamRunning)
                {
                    AppendLog("INFO", "Steam", $"Synchronizing Steam Library and starting download for {card.Title}...");
                    await _launcherProtocolService.RestartSteamAndDownloadAsync(TargetBaseDirectory, card.AppId);
                }
                else
                {
                    _launcherProtocolService.TriggerInstallAppUri(card.AppId);
                }
            }
            else
            {
                AppendLog("ERROR", "Library", provisionResult.Message);
            }
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", "Library", $"Failed to start download for AppID {card.AppId} ({ex.GetType().Name}).");
        }
        finally
        {
            card.IsProvisioning = false;
            RefreshSteamProcessStatus();
            ApplyCatalogFilter();
        }
    }

    private async Task RestartSteamNowAsync()
    {
        AppendLog("INFO", "Steam", "Refreshing Steam client in clean mode...");
        var ok = await _launcherProtocolService.RestartSteamClientAsync(TargetBaseDirectory);
        RefreshSteamProcessStatus();
        AppendLog(ok ? "INFO" : "WARN", "Steam", ok ? "Steam client synchronized." : "Failed to refresh Steam client.");
    }

    private void AddStoreGameToCatalog(SteamStoreGame? storeGame)
    {
        if (storeGame is null)
        {
            return;
        }

        var existing = AllTitles.FirstOrDefault(t => t.AppId == storeGame.AppId);
        if (existing is not null)
        {
            ApplyCatalogFilter();
            return;
        }

        var newTitle = new CatalogTitle
        {
            AppId = storeGame.AppId,
            Title = storeGame.Title,
            Publisher = "Steam Store",
            Genre = storeGame.Genre,
            BuildVersion = "Ryuu Generator",
            LatestDepotsCount = 1,
            DlcCount = 0
        };

        var config = _configStore.Load();
        config.Catalog.Insert(0, newTitle);
        _configStore.Save(config);

        var card = new TitleCardViewModel(newTitle);
        AllTitles.Insert(0, card);
        ApplyCatalogFilter();

        AppendLog("INFO", "Catalog", $"Added '{storeGame.Title}' (AppID {storeGame.AppId}) to catalog.");
    }

    private async Task AddCustomAppIdFromSearchAsync()
    {
        var raw = (SearchQuery ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            AppendLog("WARN", "Catalog", "Type a game name or Steam AppID in the search box to add it to your catalog.");
            return;
        }

        uint resolvedAppId = 0;
        var resolvedName = string.Empty;

        if (!uint.TryParse(raw, out resolvedAppId) || resolvedAppId == 0)
        {
            // Live search on Steam Store API by game name (same endpoint as LuaToolsApiClient.SearchAsync)
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(raw)}&l=english&cc=US";
                var json = await http.GetStringAsync(url);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    var first = items[0];
                    if (first.TryGetProperty("id", out var idProp) && idProp.TryGetUInt32(out var foundId))
                    {
                        resolvedAppId = foundId;
                        resolvedName = first.TryGetProperty("name", out var nameProp)
                            ? (nameProp.GetString() ?? $"Steam App {foundId}")
                            : $"Steam App {foundId}";
                    }
                }
            }
            catch
            {
            }
        }
        else
        {
            // Try to resolve real title name from Steam storesearch / appdetails
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var url = $"https://store.steampowered.com/api/appdetails?appids={resolvedAppId}";
                var json = await http.GetStringAsync(url);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(resolvedAppId.ToString(), out var appNode) &&
                    appNode.TryGetProperty("data", out var dataNode) &&
                    dataNode.TryGetProperty("name", out var nameNode))
                {
                    resolvedName = nameNode.GetString() ?? $"Steam App {resolvedAppId}";
                }
            }
            catch
            {
            }
        }

        if (resolvedAppId == 0)
        {
            AppendLog("WARN", "Catalog", $"Could not find any Steam game matching '{raw}'.");
            return;
        }

        var existing = AllTitles.FirstOrDefault(t => t.AppId == resolvedAppId);
        if (existing is not null)
        {
            SearchQuery = existing.Title;
            ApplyCatalogFilter();
            return;
        }

        var newTitle = new CatalogTitle
        {
            AppId = resolvedAppId,
            Title = string.IsNullOrWhiteSpace(resolvedName) ? $"Steam App {resolvedAppId}" : resolvedName,
            Publisher = "Steam Store",
            Genre = "Steam Game",
            BuildVersion = "Ryuu Generator",
            LatestDepotsCount = 1,
            DlcCount = 0
        };

        var config = _configStore.Load();
        config.Catalog.Insert(0, newTitle);
        _configStore.Save(config);

        var card = new TitleCardViewModel(newTitle);
        AllTitles.Insert(0, card);
        ApplyCatalogFilter();

        AppendLog("INFO", "Catalog", $"Added '{newTitle.Title}' (AppID {resolvedAppId}) to catalog. Ready to add to Steam.");
    }

    private async Task RequestGeneratorUpdateAsync(TitleCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        try
        {
            AppendLog("INFO", "RyuuAPI", $"Requesting manifest update on Ryuu Generator for AppID {card.AppId} ({card.Title})...");
            await _manifestClientService.RequestUpdateAsync(card.AppId);
            AppendLog("INFO", "RyuuAPI", $"Update request completed for AppID {card.AppId}.");
        }
        catch (Exception ex)
        {
            AppendLog("WARN", "RyuuAPI", $"Update request failed for AppID {card.AppId} ({ex.GetType().Name}).");
        }
    }

    private async Task OpenFixesModalAsync(TitleCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        SelectedFixTitle = card;
        AvailableFixes.Clear();

        var detectedInstallDir = _gameFixService.ResolveGameInstallDirectory(TargetBaseDirectory, card.AppId);
        SelectedFixInstallDirectory = detectedInstallDir ?? string.Empty;
        HasAppliedFixOnDisk = _gameFixService.GetAppliedFixIds(SelectedFixInstallDirectory).Count > 0;

        FixStatusMessage = string.IsNullOrEmpty(SelectedFixInstallDirectory)
            ? "Game folder not detected in steamapps\\common yet. Install the game on Steam first, or click 'Browse Folder...' to select the game folder."
            : $"Detected install folder: {SelectedFixInstallDirectory}";

        IsFixesModalOpen = true;
        IsFixBusy = true;

        try
        {
            var fixes = await _gameFixService.FetchFixesForAppAsync(card.AppId);
            foreach (var f in fixes)
            {
                AvailableFixes.Add(f);
            }

            if (AvailableFixes.Count == 0)
            {
                FixStatusMessage += " • No specific bypass listed; you can apply any custom Fix .ZIP or run Steamless DRM Strip.";
            }
        }
        catch (Exception ex)
        {
            AppendLog("WARN", "Fixes", $"Could not query fix metadata for AppID {card.AppId}: {ex.Message}");
        }
        finally
        {
            IsFixBusy = false;
        }
    }

    public async Task ApplyLocalFixArchiveAsync(string zipFilePath)
    {
        if (SelectedFixTitle is null || IsFixBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedFixInstallDirectory) || !Directory.Exists(SelectedFixInstallDirectory))
        {
            FixStatusMessage = "Select a valid game installation folder first.";
            return;
        }

        try
        {
            IsFixBusy = true;
            var fixId = AvailableFixes.FirstOrDefault()?.Id ?? $"custom_{SelectedFixTitle.AppId}";
            FixStatusMessage = $"Applying fix archive '{Path.GetFileName(zipFilePath)}'...";

            var res = await _gameFixService.ApplyFixZipAsync(
                SelectedFixTitle.AppId,
                fixId,
                zipFilePath,
                SelectedFixInstallDirectory);

            FixStatusMessage = res.Message;
            HasAppliedFixOnDisk = _gameFixService.GetAppliedFixIds(SelectedFixInstallDirectory).Count > 0;
            AppendLog(res.Success ? "INFO" : "WARN", "Fixes", $"[AppID {SelectedFixTitle.AppId}] {res.Message}");
        }
        finally
        {
            IsFixBusy = false;
        }
    }

    private async Task ApplyOnlineFixAsync(GameFixDto? fix)
    {
        if (SelectedFixTitle is null || fix is null || IsFixBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedFixInstallDirectory) || !Directory.Exists(SelectedFixInstallDirectory))
        {
            FixStatusMessage = "Game installation folder not found. Wait for Steam to create the folder in steamapps\\common or browse to it.";
            return;
        }

        try
        {
            IsFixBusy = true;
            FixStatusMessage = $"Checking fix package '{fix.DisplayTitle}'...";

            var res = await _gameFixService.DownloadAndApplyFixAsync(
                SelectedFixTitle.AppId,
                fix,
                SelectedFixInstallDirectory);

            FixStatusMessage = res.Message;
            HasAppliedFixOnDisk = _gameFixService.GetAppliedFixIds(SelectedFixInstallDirectory).Count > 0;
            AppendLog(res.Success ? "INFO" : "WARN", "Fixes", $"[AppID {SelectedFixTitle.AppId}] {res.Message}");
        }
        finally
        {
            IsFixBusy = false;
        }
    }

    private async Task RevertAppliedFixAsync()
    {
        if (SelectedFixTitle is null || IsFixBusy)
        {
            return;
        }

        try
        {
            IsFixBusy = true;
            var fixId = _gameFixService.GetAppliedFixIds(SelectedFixInstallDirectory).FirstOrDefault()
                        ?? $"custom_{SelectedFixTitle.AppId}";
            FixStatusMessage = "Reverting applied fix and restoring original backups...";

            var res = await _gameFixService.RevertFixAsync(
                SelectedFixTitle.AppId,
                fixId,
                SelectedFixInstallDirectory);

            FixStatusMessage = res.Message;
            HasAppliedFixOnDisk = _gameFixService.GetAppliedFixIds(SelectedFixInstallDirectory).Count > 0;
            AppendLog(res.Success ? "INFO" : "WARN", "Fixes", $"[AppID {SelectedFixTitle.AppId}] {res.Message}");
        }
        finally
        {
            IsFixBusy = false;
        }
    }

    private async Task RunSteamlessDrmAsync()
    {
        if (SelectedFixTitle is null || IsFixBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedFixInstallDirectory) || !Directory.Exists(SelectedFixInstallDirectory))
        {
            FixStatusMessage = "Select a valid game installation folder first.";
            return;
        }

        try
        {
            IsFixBusy = true;
            var res = await _gameFixService.RunSteamlessDrmStripAsync(
                SelectedFixTitle.AppId,
                SelectedFixInstallDirectory,
                msg => FixStatusMessage = msg);

            FixStatusMessage = res.Message;
            AppendLog(res.Success ? "INFO" : "WARN", "Steamless", $"[AppID {SelectedFixTitle.AppId}] {res.Message}");
        }
        finally
        {
            IsFixBusy = false;
        }
    }

    private void ExecuteSteamRun(TitleCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        try
        {
            _launcherProtocolService.TriggerInstallAppUri(card.AppId);
            AppendLog("INFO", "Steam", $"Triggered native Steam download: steam://install/{card.AppId}");
        }
        catch (Exception ex)
        {
            AppendLog("WARN", "Steam", ex.Message);
        }
    }

    public void DetectSteamDirectory()
    {
        var detected = _steamLocatorService.DetectSteamInstallPath();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            TargetBaseDirectory = detected;
                RefreshSteamProcessStatus();
            AppendLog("INFO", "Registry", $"Detected Steam path: {detected}");
        }
        else
        {
            AppendLog("WARN", "Registry", "Steam installation not found in HKCU\\Software\\Valve\\Steam. Select directory manually.");
        }
    }

    public void SaveConfiguration()
    {
        var config = _configStore.Load();
        config.Theme = SelectedTheme;
        config.TargetBaseDirectory = TargetBaseDirectory.Trim();
        config.LuaPluginRelativeDirectory = LuaPluginRelativeDirectory.Trim();
        config.DepotCacheRelativeDirectory = DepotCacheRelativeDirectory.Trim();
        config.MirrorManifestsToConfigDepotCache = MirrorManifestsToConfigDepotCache;
        config.ManifestEndpointUrl = ManifestEndpointUrl.Trim();
        config.ManifestBranch = string.IsNullOrWhiteSpace(ManifestBranch) ? "public" : ManifestBranch.Trim();
        config.UseOfflineManifestGenerator = UseOfflineManifestGenerator;
        config.RestartSteamAfterProvision = RestartSteamAfterProvision;

        _configStore.Save(config);
        RefreshSteamProcessStatus();
        AppendLog("INFO", "Settings", $"Saved configuration to {_configStore.ConfigFilePath}");
    }

    public void AppendLog(string level, string category, string message)
    {
        var entry = new OperationLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Category = category,
            Message = message
        };

        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => OperationLogs.Insert(0, entry));
        }
        else
        {
            OperationLogs.Insert(0, entry);
        }
    }
}