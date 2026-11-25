using System.IO;
using System.Text.Json;
using CatalogEntitlementManager.Models;

namespace CatalogEntitlementManager.Storage;

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ConfigFilePath { get; }

    public JsonConfigStore(string? customConfigPath = null)
    {
        ConfigFilePath = string.IsNullOrWhiteSpace(customConfigPath)
            ? Path.Combine(AppContext.BaseDirectory, "appsettings.json")
            : customConfigPath;
    }

    public AppConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            var defaultConfig = CreateDefaultConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
            if (loaded is null)
            {
                return CreateDefaultConfig();
            }

            var defaultCatalog = CreateDefaultConfig().Catalog;
            if (loaded.Catalog.Count == 0)
            {
                loaded.Catalog = defaultCatalog;
            }
            else
            {
                var existingIds = loaded.Catalog.Select(c => c.AppId).ToHashSet();
                var addedAny = false;
                foreach (var defTitle in defaultCatalog)
                {
                    if (!existingIds.Contains(defTitle.AppId))
                    {
                        loaded.Catalog.Add(defTitle);
                        addedAny = true;
                    }
                }

                if (addedAny)
                {
                    Save(loaded);
                }
            }

            return loaded;
        }
        catch
        {
            return CreateDefaultConfig();
        }
    }

    public void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = ConfigFilePath + ".tmp";
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ConfigFilePath, overwrite: true);
    }

    private static AppConfig CreateDefaultConfig() => new()
    {
        ManifestEndpointUrl = "https://generator.ryuu.lol/api/download/{appId}",
        ManifestBranch = "public",
        HttpTimeoutSeconds = 30,
        HttpMaxRetries = 3,
        UseOfflineManifestGenerator = false,
        RestartSteamAfterProvision = true,
        Catalog = new List<CatalogTitle>
        {
            new()
            {
                AppId = 271590,
                Title = "Grand Theft Auto V Legacy",
                Publisher = "Rockstar Games",
                Genre = "Action, Adventure",
                BuildVersion = "Updated Nov 5, 2026",
                LatestDepotsCount = 6,
                DlcCount = 6,
                ManifestFiles = new List<string>
                {
                    "1899671_4635231945872575760.manifest",
                    "271591_5509090605608271459.manifest",
                    "271592_8179666891023172621.manifest",
                    "271593_2967789647252082634.manifest",
                    "271594_7977512212816798234.manifest",
                    "271595_1376135673068470825.manifest"
                },
                IncludedDlcIds = new List<uint> { 362000, 362001, 362002, 362003, 413180, 771300 }
            },
            new()
            {
                AppId = 3240220,
                Title = "Grand Theft Auto V Enhanced",
                Publisher = "Rockstar Games",
                Genre = "Action, Adventure",
                BuildVersion = "721 Downloads This Week",
                LatestDepotsCount = 3,
                DlcCount = 2,
                ManifestFiles = new List<string>
                {
                    "3240221_7182930495817263541.manifest",
                    "3240222_6192837465918273645.manifest",
                    "3240223_5182736455918273640.manifest"
                },
                IncludedDlcIds = new List<uint> { 3240230, 3240231 }
            },
            new()
            {
                AppId = 2483190,
                Title = "Forza Horizon 6",
                Publisher = "Xbox Game Studios",
                Genre = "Racing, Simulation, Sports",
                BuildVersion = "Updated Nov 5, 2026",
                LatestDepotsCount = 1,
                MissingDepotsCount = 5,
                DlcCount = 38,
                ManifestFiles = new List<string> { "2483191_8260642490601717688.manifest" },
                IncludedDlcIds = new List<uint>
                {
                    4439240, 4439250, 4439260, 4439270, 4439280, 4439290, 4439300,
                    4439750, 4439790, 4439800, 4439810, 4439820, 4439830, 4439840,
                    4439890, 4439900, 4440380, 4444140, 4444150, 4520350, 4520360,
                    4562050, 4610350, 4797330, 4797340, 4797350, 4797360, 4913450,
                    4913460, 4913470, 4913480, 4949510, 4949520, 4949530, 4949540,
                    5067170, 5067180, 5134010
                },
                MissingDlcIds = new List<uint> { 5004270, 5067200, 5104170, 5104180, 5104200 }
            },
            new()
            {
                AppId = 1551360,
                Title = "Forza Horizon 5",
                Publisher = "Xbox Game Studios",
                Genre = "Racing, Open World",
                BuildVersion = "Premium Edition",
                LatestDepotsCount = 4,
                DlcCount = 42,
                ManifestFiles = new List<string> { "1551361_8192039485716253419.manifest", "1551362_7182930495817263520.manifest" },
                IncludedDlcIds = new List<uint> { 1613280, 1613281, 1613282, 1798680 }
            },
            new()
            {
                AppId = 1174180,
                Title = "Red Dead Redemption 2",
                Publisher = "Rockstar Games",
                Genre = "Action, Adventure",
                BuildVersion = "614 Downloads This Week",
                LatestDepotsCount = 7,
                DlcCount = 2,
                ManifestFiles = new List<string>
                {
                    "1174181_4757128409362785099.manifest",
                    "1174182_2258488483491377476.manifest",
                    "1174183_4785346550216140531.manifest",
                    "1174184_2698939236196220127.manifest",
                    "1174185_8173342615013177810.manifest",
                    "1174186_357926288062992323.manifest",
                    "1899671_4635231945872575760.manifest"
                },
                IncludedDlcIds = new List<uint> { 1190050, 1190051 }
            },
            new()
            {
                AppId = 2668510,
                Title = "Red Dead Redemption",
                Publisher = "Rockstar Games",
                Genre = "Action, Adventure",
                BuildVersion = "Undead Nightmare Included",
                LatestDepotsCount = 2,
                DlcCount = 1,
                ManifestFiles = new List<string> { "2668511_6482910394857162534.manifest" }
            },
            new()
            {
                AppId = 2358720,
                Title = "Black Myth: Wukong",
                Publisher = "Game Science",
                Genre = "Action RPG, Mythology",
                BuildVersion = "Deluxe Edition",
                LatestDepotsCount = 3,
                DlcCount = 2,
                ManifestFiles = new List<string> { "2358721_9182736455123456789.manifest" },
                IncludedDlcIds = new List<uint> { 2672910 }
            },
            new()
            {
                AppId = 2322010,
                Title = "God of War Ragnarök",
                Publisher = "PlayStation Publishing",
                Genre = "Action, Adventure",
                BuildVersion = "Valhalla Included",
                LatestDepotsCount = 4,
                DlcCount = 6,
                ManifestFiles = new List<string> { "2322011_8273645519283746510.manifest" },
                IncludedDlcIds = new List<uint> { 2970090 }
            },
            new()
            {
                AppId = 1593500,
                Title = "God of War",
                Publisher = "PlayStation Publishing",
                Genre = "Action, Adventure",
                BuildVersion = "Latest Build",
                LatestDepotsCount = 2,
                DlcCount = 0,
                ManifestFiles = new List<string> { "1593501_7364551928374651029.manifest" }
            },
            new()
            {
                AppId = 2215430,
                Title = "Ghost of Tsushima DIRECTOR'S CUT",
                Publisher = "PlayStation Publishing",
                Genre = "Action, Open World",
                BuildVersion = "Iki Island + Legends",
                LatestDepotsCount = 3,
                DlcCount = 4,
                ManifestFiles = new List<string> { "2215431_6253441928374651038.manifest" },
                IncludedDlcIds = new List<uint> { 2852110 }
            },
            new()
            {
                AppId = 1817070,
                Title = "Marvel’s Spider-Man Remastered",
                Publisher = "PlayStation Publishing",
                Genre = "Action, Superhero",
                BuildVersion = "Complete Edition",
                LatestDepotsCount = 2,
                DlcCount = 1,
                ManifestFiles = new List<string> { "1817071_5142331928374651047.manifest" }
            },
            new()
            {
                AppId = 2651280,
                Title = "Marvel’s Spider-Man 2",
                Publisher = "PlayStation Publishing",
                Genre = "Action, Superhero",
                BuildVersion = "Deluxe Edition",
                LatestDepotsCount = 3,
                DlcCount = 3,
                ManifestFiles = new List<string> { "2651281_4031221928374651056.manifest" }
            },
            new()
            {
                AppId = 814380,
                Title = "Sekiro™: Shadows Die Twice",
                Publisher = "FromSoftware",
                Genre = "Action, Souls-like",
                BuildVersion = "GOTY Edition",
                LatestDepotsCount = 2,
                DlcCount = 0,
                ManifestFiles = new List<string> { "814381_3920111928374651065.manifest" }
            },
            new()
            {
                AppId = 374320,
                Title = "DARK SOULS™ III",
                Publisher = "FromSoftware",
                Genre = "Action RPG",
                BuildVersion = "Deluxe Edition + Ringed City",
                LatestDepotsCount = 3,
                DlcCount = 2,
                ManifestFiles = new List<string> { "374321_2819001928374651074.manifest" },
                IncludedDlcIds = new List<uint> { 442010, 506970 }
            },
            new()
            {
                AppId = 1086940,
                Title = "Baldur's Gate 3",
                Publisher = "Larian Studios",
                Genre = "CRPG, Adventure",
                BuildVersion = "Patch 7 Digital Deluxe",
                LatestDepotsCount = 4,
                DlcCount = 1,
                ManifestFiles = new List<string> { "1086941_1708991928374651083.manifest" },
                IncludedDlcIds = new List<uint> { 2378500 }
            },
            new()
            {
                AppId = 990080,
                Title = "Hogwarts Legacy",
                Publisher = "Warner Bros. Games",
                Genre = "Open World, Magic RPG",
                BuildVersion = "Digital Deluxe + Fix",
                LatestDepotsCount = 3,
                DlcCount = 4,
                ManifestFiles = new List<string> { "990081_9607881928374651092.manifest" },
                IncludedDlcIds = new List<uint> { 2178560, 2178561 }
            },
            new()
            {
                AppId = 1196590,
                Title = "Resident Evil Village",
                Publisher = "CAPCOM",
                Genre = "Survival Horror",
                BuildVersion = "Gold Edition + Winters' Expansion",
                LatestDepotsCount = 2,
                DlcCount = 6,
                ManifestFiles = new List<string> { "1196591_8506771928374651101.manifest" },
                IncludedDlcIds = new List<uint> { 1731080 }
            },
            new()
            {
                AppId = 2183900,
                Title = "Warhammer 40,000: Space Marine 2",
                Publisher = "Focus Entertainment",
                Genre = "Action, Third-Person Shooter",
                BuildVersion = "Ultra Edition",
                LatestDepotsCount = 3,
                DlcCount = 5,
                ManifestFiles = new List<string> { "2183901_7405661928374651110.manifest" },
                IncludedDlcIds = new List<uint> { 2792090 }
            },
            new()
            {
                AppId = 2161700,
                Title = "Persona 3 Reload",
                Publisher = "SEGA / ATLUS",
                Genre = "JRPG, Turn-Based",
                BuildVersion = "Episode Aigis Included",
                LatestDepotsCount = 2,
                DlcCount = 8,
                ManifestFiles = new List<string> { "2161701_6304551928374651119.manifest" },
                IncludedDlcIds = new List<uint> { 2689210 }
            },
            new()
            {
                AppId = 1623730,
                Title = "Palworld",
                Publisher = "Pocketpair",
                Genre = "Survival, Open World Crafting",
                BuildVersion = "Sakurajima Update",
                LatestDepotsCount = 2,
                DlcCount = 1,
                ManifestFiles = new List<string> { "1623731_5203441928374651128.manifest" }
            },
            new()
            {
                AppId = 292030,
                Title = "The Witcher 3: Wild Hunt",
                Publisher = "CD PROJEKT RED",
                Genre = "Open World RPG",
                BuildVersion = "Complete Next-Gen Edition",
                LatestDepotsCount = 4,
                DlcCount = 18,
                ManifestFiles = new List<string> { "292031_4102331928374651137.manifest" },
                IncludedDlcIds = new List<uint> { 378649, 378648 }
            },
            new()
            {
                AppId = 534380,
                Title = "Dying Light 2 Stay Human: Reloaded Edition",
                Publisher = "Techland",
                Genre = "Survival Horror, Parkour",
                BuildVersion = "Reloaded Edition",
                LatestDepotsCount = 3,
                DlcCount = 12,
                ManifestFiles = new List<string> { "534381_3001221928374651146.manifest" }
            },
            new()
            {
                AppId = 1774580,
                Title = "STAR WARS Jedi: Survivor™",
                Publisher = "Electronic Arts",
                Genre = "Action, Sci-Fi",
                BuildVersion = "Deluxe Edition",
                LatestDepotsCount = 3,
                DlcCount = 2,
                ManifestFiles = new List<string> { "1774581_2900111928374651155.manifest" }
            },
            new()
            {
                AppId = 1716740,
                Title = "Starfield",
                Publisher = "Bethesda Softworks",
                Genre = "Space RPG",
                BuildVersion = "Shattered Space Included",
                LatestDepotsCount = 3,
                DlcCount = 3,
                ManifestFiles = new List<string> { "1716741_1899001928374651164.manifest" },
                IncludedDlcIds = new List<uint> { 2721670 }
            },
            new()
            {
                AppId = 1364780,
                Title = "Street Fighter™ 6",
                Publisher = "CAPCOM",
                Genre = "Fighting, Arcade",
                BuildVersion = "Year 2 Ultimate Pass",
                LatestDepotsCount = 3,
                DlcCount = 8,
                ManifestFiles = new List<string> { "1364781_1788991928374651173.manifest" }
            },
            new()
            {
                AppId = 1778820,
                Title = "TEKKEN 8",
                Publisher = "Bandai Namco",
                Genre = "Fighting, 3D",
                BuildVersion = "Ultimate Edition",
                LatestDepotsCount = 2,
                DlcCount = 6,
                ManifestFiles = new List<string> { "1778821_1677881928374651182.manifest" }
            },
            new()
            {
                AppId = 2379780,
                Title = "Balatro",
                Publisher = "Playstack",
                Genre = "Roguelike Deckbuilder",
                BuildVersion = "Latest Build",
                LatestDepotsCount = 1,
                DlcCount = 0,
                ManifestFiles = new List<string> { "2379781_1566771928374651191.manifest" }
            },
            new()
            {
                AppId = 367520,
                Title = "Hollow Knight",
                Publisher = "Team Cherry",
                Genre = "Metroidvania, Souls-like",
                BuildVersion = "Godmaster Edition",
                LatestDepotsCount = 1,
                DlcCount = 2,
                ManifestFiles = new List<string> { "367521_1455661928374651200.manifest" }
            },
            new()
            {
                AppId = 413150,
                Title = "Stardew Valley",
                Publisher = "ConcernedApe",
                Genre = "Farming Sim, RPG",
                BuildVersion = "1.6 Update",
                LatestDepotsCount = 1,
                DlcCount = 0,
                ManifestFiles = new List<string> { "413151_1344551928374651209.manifest" }
            },
            new()
            {
                AppId = 105600,
                Title = "Terraria",
                Publisher = "Re-Logic",
                Genre = "Sandbox, Survival",
                BuildVersion = "1.4.4.9 Labor of Love",
                LatestDepotsCount = 1,
                DlcCount = 0,
                ManifestFiles = new List<string> { "105601_1233441928374651218.manifest" }
            },
            new()
            {
                AppId = 1091500,
                Title = "Cyberpunk 2077",
                Publisher = "CD PROJEKT RED",
                Genre = "Open World RPG",
                BuildVersion = "441 Downloads This Week",
                LatestDepotsCount = 3,
                DlcCount = 3,
                ManifestFiles = new List<string>
                {
                    "1091501_9182736455463728192.manifest",
                    "1091502_8273645546372819203.manifest",
                    "2138330_7364554637281920394.manifest"
                },
                IncludedDlcIds = new List<uint> { 2138330, 1495710, 2291720 }
            },
            new()
            {
                AppId = 1888930,
                Title = "The Last of Us™ Part I",
                Publisher = "PlayStation Publishing",
                Genre = "Action, Adventure",
                BuildVersion = "Ryuu's Picks",
                LatestDepotsCount = 2,
                DlcCount = 2,
                ManifestFiles = new List<string>
                {
                    "1888931_6455463728192039485.manifest",
                    "1888932_5546372819203948571.manifest"
                },
                IncludedDlcIds = new List<uint> { 2254890, 2254891 }
            },
            new()
            {
                AppId = 2531310,
                Title = "The Last of Us™ Part II Remastered",
                Publisher = "PlayStation Publishing",
                Genre = "Action, Adventure",
                BuildVersion = "Ryuu's Picks",
                LatestDepotsCount = 2,
                DlcCount = 1,
                ManifestFiles = new List<string>
                {
                    "2531311_4637281920394857162.manifest",
                    "2531312_3728192039485716253.manifest"
                },
                IncludedDlcIds = new List<uint> { 2815430 }
            },
            new()
            {
                AppId = 495420,
                Title = "State of Decay 2",
                Publisher = "Xbox Game Studios",
                Genre = "Survival, Open World",
                BuildVersion = "Ryuu's Picks",
                LatestDepotsCount = 2,
                DlcCount = 4,
                ManifestFiles = new List<string>
                {
                    "495421_2819203948571625341.manifest",
                    "495422_1920394857162534182.manifest"
                },
                IncludedDlcIds = new List<uint> { 814340, 814341, 1145020, 1249070 }
            },
            new()
            {
                AppId = 329430,
                Title = "State of Decay: Year-One",
                Publisher = "Xbox Game Studios",
                Genre = "Survival, Action",
                BuildVersion = "Ryuu's Picks",
                LatestDepotsCount = 1,
                DlcCount = 2,
                ManifestFiles = new List<string> { "329431_9203948571625341827.manifest" },
                IncludedDlcIds = new List<uint> { 329435, 329436 }
            },
            new()
            {
                AppId = 1245620,
                Title = "ELDEN RING",
                Publisher = "FromSoftware",
                Genre = "Action RPG",
                BuildVersion = "1.14.0",
                LatestDepotsCount = 3,
                DlcCount = 2,
                ManifestFiles = new List<string>
                {
                    "1245621_8172635445362718293.manifest",
                    "1245622_7162534453627182930.manifest",
                    "2778580_6152433453627182931.manifest"
                },
                IncludedDlcIds = new List<uint> { 2778580, 1888160 }
            },
            new()
            {
                AppId = 2050650,
                Title = "Resident Evil 4",
                Publisher = "CAPCOM",
                Genre = "Survival Horror",
                BuildVersion = "1.1.0",
                LatestDepotsCount = 2,
                DlcCount = 6,
                ManifestFiles = new List<string>
                {
                    "2050651_5142332453627182932.manifest",
                    "2050652_4132231453627182933.manifest"
                },
                IncludedDlcIds = new List<uint> { 2109300, 2109301, 2109302, 2109303, 2109310, 2109320 }
            },
            new()
            {
                AppId = 730,
                Title = "Counter-Strike 2",
                Publisher = "Valve",
                Genre = "Tactical Shooter",
                BuildVersion = "1.40.4.8",
                LatestDepotsCount = 2,
                DlcCount = 1,
                ManifestFiles = new List<string>
                {
                    "731_8492019482716543210.manifest",
                    "2347770_7492019482716543211.manifest"
                },
                IncludedDlcIds = new List<uint> { 2279720 }
            },
            new()
            {
                AppId = 570,
                Title = "Dota 2",
                Publisher = "Valve",
                Genre = "MOBA",
                BuildVersion = "7.37d",
                LatestDepotsCount = 2,
                DlcCount = 0,
                ManifestFiles = new List<string>
                {
                    "571_6492019482716543212.manifest",
                    "373301_5492019482716543213.manifest"
                }
            }
        }
    };
}