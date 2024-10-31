using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Capabilities;
using MenuManager;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.Extensions.Logging;



namespace Poor_RockTheVote;

public class PoorRTVConfig : BasePluginConfig
{ 
    [JsonPropertyName("Percentage")] public int Percentage { get; set; } = 50;
    [JsonPropertyName("Nominate")] public bool NominateCFG { get; set; } = true;
    [JsonPropertyName("EndMapVote")] public bool EndMapVote { get; set; } = false;
    [JsonPropertyName("TimeToVote")] public float TimeToVote { get; set; } = 10;
    [JsonPropertyName("DisplayMaps")] public int DisplayMaps { get; set; } = 6;
    [JsonPropertyName("IncludeLast")] public bool IncludeLast { get; set; } = false;
    [JsonPropertyName("IncludeLastX")] public int IncludeLastX { get; set; } = 3;
    [JsonPropertyName("Shuffle")] public bool Shuffle { get; set; } = true;
    [JsonPropertyName("MenuType")] public string MenuServerType { get; set; } = "Button";
    [JsonPropertyName("Debug")] public bool Debug { get; set; } = true;
}

public class Poor_RockTheVote : BasePlugin, IPluginConfig<PoorRTVConfig>
{
    public override string ModuleName => "Poor Rock The Vote";
    public override string ModuleVersion => "1.0";
    public override string ModuleAuthor => "Letaryat";
    public override string ModuleDescription => "Basic RockTheVote plugin";

    private IMenuApi? _api;
    private readonly PluginCapability<IMenuApi?> _pluginCapability = new("menu:nfcore");

    //Config:
    public bool Debug;
    public double Percentage;
    public float TimeToVote;
    public bool Shuffle;
    public bool NominateCFG;
    public bool IncludeLast;
    public int IncludeLastX;
    public string MenuServerType;
    public int Displaymaps;
    public bool EndMapVote;
    /* Abym sie nie zajebal w akcji:
     * 
     * RTVCache - If player used !rtv
     * IfVoted - If player voted for any map
     *
     */

    IDictionary<string, int> RTVCache = new Dictionary<string, int>();
    IDictionary<string, int> Maps = new Dictionary<string, int>();
    List<string> IfVoted = new List<string>();
    List<string> IncludeX = new List<string>();
    public string Mapspath;
    public string CurrentMap;
    public string WonMap;
    public int CountRTV = 0;
    public int percentagertv;
    private int _elapsedTime;
    public float StoreTime;
    public float TimeLeft;
    public float? TimeLimit;
    //public int currMin;
    public bool WarmupEnd = true;
    public bool VotingAllowed = true;
    public bool StartEndVoting = false;
    public bool RTVByUsers = false;
    public float ServerTime;
    private CounterStrikeSharp.API.Modules.Timers.Timer myTimer = null;
    //List<string> Maps = new List<string>();


    public PoorRTVConfig Config { get; set; }

    public void OnConfigParsed(PoorRTVConfig config)
    {
        Debug = config.Debug;
        TimeToVote = config.TimeToVote;
        Percentage = config.Percentage;
        NominateCFG = config.NominateCFG;
        Shuffle = config.Shuffle;
        IncludeLast = config.IncludeLast;
        IncludeLastX = config.IncludeLastX;
        Displaymaps = config.DisplayMaps;
        StoreTime = config.TimeToVote;
        MenuServerType = config.MenuServerType;
        EndMapVote = config.EndMapVote;
    }

    public override void Load(bool hotReload)
    {
        //Creating maps.txt file in config directory:
        string ParentPath = Directory.GetParent(ModuleDirectory)?.Parent?.FullName ?? string.Empty;
        Mapspath = ParentPath + "/configs/plugins/Poor-RockTheVote/maps.txt";
        if(!File.Exists(Mapspath))
        {
            File.Create(Mapspath);
        }
        //Listeners:
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);

        RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatch);

        MapsIntoList();

        Console.WriteLine("Finished Loading Poor - Rock The Vote Plugin");

        //Commands:
        //AddCommand("css_seecache", "Cache", (player, info) => RTVCommand());
        //AddCommand("css_seemaps", "Maps", (player, info) => MapDebuging());
        //AddCommand("css_random", "Random method where I test stuff because I have no idea what I am doing", (player, info) => RandomStuff());
        //AddCommand("css_clearvoting", "Random method where I test stuff because I have no idea what I am doing", (player, info) => ClearVotingCache());
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        //MenuManager API:
        _api = _pluginCapability.Get();
        if (_api == null) Console.WriteLine("MenuManager Core not found...");
        
    }

    private void OnMapStart(string mapName)
    {

        AddTimer(1.0f, () =>
        {
            ClearVotingCache();
            Logger.LogInformation($"Players in cache: {RTVCache.Count}");
        });
        myTimer = null;
    }

    public void OnMapEnd()
    {
        if(IncludeLast == false)
        {
            CurrentMap = Server.MapName;  
        } 
        if(IncludeLast == false || IncludeLastX > 0)
        {
            Server.NextFrame(() =>
            {
                IncludeX.Add(CurrentMap);
            });
        }
        ClearVotingCache();
        Logger.LogInformation($"Mapend: Voting Allowed: {VotingAllowed}");
        DebugAll();
    }

    [ConsoleCommand("css_rtv", "Rock the vote")]
    public void RTVCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || player.IsBot || player.IsHLTV)
        {
            return;
        }

        var playeruserid = player!.UserId.ToString();
        if (IfVoted.Contains(playeruserid!))
        {
            player.PrintToChat($" {Localizer["prefix"]} {Localizer["playervoted"]} ");
            return;
        }
        if(VotingAllowed == false)
        {
            player.PrintToChat($" {Localizer["prefix"]} {Localizer["votestarted"]} ");
            return;
        }

        //Server.PrintToChatAll("Playeruserid: " + playeruserid);

        if (RTVCache.ContainsKey(playeruserid!)){
            //Server.PrintToChatAll("Test");
            if (RTVCache[playeruserid!] == 0)
            {
                Server.PrintToChatAll($" {Localizer["prefix"]} {Localizer["wantstovote", player.PlayerName.ToString()]}");
                LoggingDebugging($"Player with ID: ${playeruserid} voted");
                RTVCache[playeruserid!] = 1;
                player.PrintToChat($" {Localizer["prefix"]} {Localizer["playerforrtv"]} ");
                CountRTV++;
            }
            else
            {
                player.PrintToChat($" {Localizer["prefix"]} {Localizer["playerusedrtv"]} ");
                return;
            }
        }

        //RTVCache[playeruserid!] = 1;
        //Server.PrintToChatAll("Count: " + CountRTV);
        //Server.PrintToChatAll(percentagertv.ToString());
        percentagertv = (int)((double)CountRTV / RTVCache.Count * 100);
        if(percentagertv > Percentage)
        {
            RTVByUsers = true;
            if(player != null)
            {
                EnableVoting();
            }
        }

    }

    [GameEventHandler]
    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var playerid = @event.Userid.UserId.ToString();
        var player = @event.Userid;
        if (player.IsBot || player.IsHLTV) { return HookResult.Continue; }
        if (!RTVCache.ContainsKey(playerid))
        {
            RTVCache.Add(playerid, 0);
            LoggingDebugging($"Player with ID: {playerid} has been added to cache.");
        }
        else
        {
            LoggingDebugging($"Player with ID: {playerid} is already in cache. Aborting.");
        }
        return HookResult.Continue;
    }


    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var playerid = @event.Userid.UserId.ToString();
        var player = @event.Userid;
        if (player.IsBot || player.IsHLTV) { return HookResult.Continue; }
        if (RTVCache.Count == 0) { return HookResult.Continue; }
        if (RTVCache.ContainsKey(playerid)) 
        {
            RTVCache.Remove(playerid);
            LoggingDebugging($"Player {player.PlayerName} with ID {playerid} has been removed from RTV cache.");
        }
        if (IfVoted.Contains(playerid))
        {
            IfVoted.Remove(playerid);
            LoggingDebugging($"Player {player.PlayerName} with ID {playerid} has been removed from IFVoted cache.");
        }


        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnWarmupEnd(EventWarmupEnd @event, GameEventInfo info)
    {
        //WarmupEnd = true;
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult EventOnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if(myTimer == null && VotingAllowed == true)
        {
            ServerTime = Server.CurrentTime;
            CheckTimeLeft();
        }
        
        return HookResult.Continue;
    }

    public HookResult EventCsWinPanelMatch(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        Server.PrintToChatAll($" {Localizer["prefix"]} {Localizer["changingmap",WonMap]}");
        //myTimer.Kill();
        Server.NextFrame(() =>
        {
            ChangeMapMethod();
        });

        return HookResult.Continue;
    }

    public void ChangeMapMethod()
    {
        if(WonMap == null) { return; }

        if(WonMap == CurrentMap)
        {
            LoggingDebugging($"Wonmap == CurrentMap: {WonMap}");
        }

        if (Server.IsMapValid(WonMap))
        {
            Server.ExecuteCommand($"changelevel {WonMap}");
        }
        else
        {
            Server.ExecuteCommand($"ds_workshop_changelevel {WonMap}");
        }
        /*
         * This thing just refresh cache if map didn't changed either because:
         * - It does not exist
         * - You fucked the name up
         * Probably it shouldn't be done like that but it works. So players can vote again when map has "changed" Cannot use ClearVotingCache method since it would fuck up RTVCache and players online on server.
         */
        /*
        foreach (var key in RTVCache)
        {
            RTVCache[key.Key] = 0;
        }
        IfVoted.Clear();
        TimeToVote = StoreTime;
        WarmupEnd = false;
        VotingAllowed = true;
        RTVByUsers = false;
        CountRTV = 0;
        */
        Logger.LogInformation($"ChangeMapMethod: {WonMap}");
    }

    public void VotedMap()
    {
        int maxValue = Maps.Values.Max();
        if (maxValue == 0 || WonMap == null)
        {
            ShuffleMaps();
            WonMap = Maps.First(x => x.Value == maxValue).Key;
            /*
            Random random = new Random();
            Maps = Maps.OrderBy(x => random.Next()).ToDictionary(item => item.Key, item => item.Value);
            WonMap = Maps.First(x => x.Value == maxValue).Key;
            */
            Server.PrintToChatAll($" {Localizer["prefix"]} {Localizer["randomselect", WonMap]}");
            if (RTVByUsers == false) { return; }
            Server.PrintToChatAll($" {Localizer["prefix"]} {Localizer["changingmap", WonMap]}");
            AddTimer(5.0f, () =>
            {
                ChangeMapMethod();
                VotingAllowed = true;
            });
        }
        else
        {
            WonMap = Maps.First(x => x.Value == maxValue).Key;
            Server.PrintToChatAll($" {Localizer["prefix"]} {Localizer["mapwon", WonMap, maxValue]}");
            if (RTVByUsers == false) { return; }
            Server.PrintToChatAll($" {Localizer["prefix"]} {Localizer["changingmap", WonMap]}");
            AddTimer(5.0f, () =>
            {
                ChangeMapMethod();
                VotingAllowed = true;
            });

        }


    }

    public void ShuffleMaps()
    {
        if(Shuffle == false) { return; }
        Random random = new Random();

        if (CurrentMap == null)
        {
            CurrentMap = Server.MapName;
        }
        if (IncludeLast == false)
        {
            Maps.Remove(CurrentMap);
        }
        else if(IncludeLast == false && IncludeLastX > 0)
        {
            foreach(var include in IncludeX)
            {
                Maps.Remove(include);
            }
        }


        Maps = Maps.OrderBy(x => random.Next()).ToDictionary(item => item.Key, item => item.Value);
        LoggingDebugging("*****************");
        LoggingDebugging("Shuffled maps.");
        LoggingDebugging("*****************");

    }

    public void EnableVoting()
    {
        if(RTVCache.Count <= 0) { return; }
        VotingAllowed = false;
        bool EndTimer = false;
        var TimeCount = TimeToVote;
        if(EndTimer == true) { return; }
        if (EndTimer == false)
        {
            AddTimer(5.0f, () =>
            {
                if (TimeCount == 5) { EndTimer = true; return; }
                TimeCount = TimeCount - 5;
                Server.PrintToChatAll($" {Localizer["prefix"]} {Localizer["votetime"]} {TimeCount}");

            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
        }
        //var menu = _api.NewMenu($" {Localizer["menuname"]}");
        var menu = CreateMenu($" {Localizer["menuname"]}");
        int mapdisplayed = 0;
        foreach (var maps in Maps)
        {
            if (mapdisplayed >= Displaymaps) { break; }
            if (IncludeLast == false)
            {
                if (maps.Key == CurrentMap)
                {
                    continue;
                }
            }
            if (IncludeLast == false || IncludeX.Count > 0)
            {
                if (IncludeX.Contains(maps.Key))
                {
                    continue;
                }
            }

            menu.AddMenuOption($"{maps.Key}", (player, option) =>
            {
                if (IfVoted.Contains(player.UserId.ToString()))
                {
                    //player.PrintToChat("Zaglosowales juz");
                }
                Maps[maps.Key] = maps.Value + 1;
                player.PrintToChat($" {Localizer["prefix"]} {Localizer["selected"]} {option.Text}");
                CounterStrikeSharp.API.Modules.Menu.MenuManager.CloseActiveMenu(player);
                IfVoted.Add(player.UserId.ToString());
            });
            mapdisplayed++;
        }

        foreach (var player in Utilities.GetPlayers())
            {
                if(player.IsBot || player.IsHLTV || player == null) { continue; }
                //Server.PrintToChatAll(player.PlayerName.ToString());
                menu.Open(player);

                AddTimer(StoreTime, () =>
                {
                    CounterStrikeSharp.API.Modules.Menu.MenuManager.CloseActiveMenu(player!);
                    VotedMap();
                });
        }
    }

    public void ClearVotingCache()
    {
        if (IncludeX.Count >= IncludeLastX)
        {
            IncludeX.Clear();
            MapsIntoList();
        }
        RTVCache.Clear();
        IfVoted.Clear();
        TimeToVote = StoreTime;
        WarmupEnd = false;
        VotingAllowed = true;
        CountRTV = 0;
        RTVByUsers = false;

        MapsIntoList();
        
        if (Shuffle == true)
        {
            ShuffleMaps();
        }
    }

    public void MapsIntoList()
    {
        var readmaps = File.ReadLines(Mapspath);
        Maps.Clear();
        foreach (string maps in readmaps)
        {
            Maps.Add(maps, 0);
        }
        if (Maps.Count == 0)
        {
            LoggingDebugging("Maps file is empty! Please fill it with data!");
        }
    }

    public void CheckTimeLeft()
    {
        if(EndMapVote == false) { return; }
        TimeLimit = ConVar.Find("mp_timelimit")?.GetPrimitiveValue<float>() ?? 0;
        if(TimeLimit == 0) { return; }
        myTimer = AddTimer(5.0f, () =>
            {
                float currentTimeSec = Server.CurrentTime;
                int currMin = (int)(currentTimeSec / 60);
                //Server.PrintToChatAll("Currmin: " + currMin.ToString());
                //Server.PrintToChatAll("Timelimit: " + TimeLimit.ToString());
                if (TimeLimit - currMin == 2 && VotingAllowed)
                {
                    VotingAllowed = false;
                    Server.PrintToChatAll($" {Localizer["prefix"]} {Localizer["endvotestart"]}");
                    EnableVoting();
                    myTimer.Kill();
                }
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
    }

    [ConsoleCommand("css_nominate", "Nominate map")]
    public void Nominate(CCSPlayerController? player, CommandInfo commandInfo)
    {
        //Server.PrintToChatAll($"Dziala. Nominate: {NominateCFG}, VotingAllowed: {VotingAllowed}");

        if (player.IsBot || player.IsHLTV || player == null || NominateCFG == false) {
            LoggingDebugging("NominateCFG False or something idk");
            return; 
        }
        if (VotingAllowed == false)
        {
            player.PrintToChat($"{Localizer["prefix"]} Nominated is off because voting is not allowed!");
            return;
        }
        IDictionary<string, int> MapsDis = new Dictionary<string, int>();
        MapsDis = Maps.Take(Displaymaps).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        //var menu = _api.NewMenu("Nominate test");
        var menu = CreateMenu($" {Localizer["nominatemenu"]}");
        //var menu = _api.NewMenuForcetype("Nominate", MenuType.ChatMenu);

            foreach (var item in Maps)
            {
                if(MapsDis.ContainsKey(item.Key)) { continue; }
                menu.AddMenuOption($"{item.Key}", (player, option) => { 
                    player.PrintToChat($" {Localizer["prefix"]} {Localizer["selected"]} {option.Text}");
                    Maps.Remove(item.Key);
                    Maps = (new Dictionary<string, int> { { item.Key, item.Value } }).Concat(Maps).ToDictionary(k => k.Key, v => v.Value);
                    CounterStrikeSharp.API.Modules.Menu.MenuManager.CloseActiveMenu(player);
                });
            }

            menu.Open(player);
    }

    public IMenu CreateMenu(string title)
    {
        var menu = _api.NewMenu(title);
        MenuServerType = MenuServerType.ToLower();
       //Server.PrintToChatAll(MenuServerType);
        if (MenuServerType == "defaultmenu")
        {
            menu = _api.NewMenu(title);
        }
        else if(MenuServerType == "buttonmenu")
        {
            menu = _api.NewMenuForcetype(title, MenuType.ButtonMenu);
        }
        else if (MenuServerType == "centermenu")
        {
            menu = _api.NewMenuForcetype(title, MenuType.CenterMenu);
        }
        else if (MenuServerType == "consolemenu")
        {
            menu = _api.NewMenuForcetype(title, MenuType.ConsoleMenu);
        }
        else if (MenuServerType == "chatmenu")
        {
            menu = _api.NewMenuForcetype(title, MenuType.ChatMenu);
        }
        else
        {
            menu = _api.NewMenu(title);
        }
        return menu;
    }

    //Debug stuff:

    public void LoggingDebugging(string message)
    {
        if(Debug == true)
        {
            Logger.LogInformation($"{DateTime.Now} | Poor-RTV | {message}");
        }
    }

    public void DebugAll()
    {
        if(Debug == true)
        {
            Logger.LogInformation($"**** ALL DEBUG *****");
            Logger.LogInformation($"Won map: {WonMap}");
            Logger.LogInformation($"WarmupEnd: {WarmupEnd}");
            Logger.LogInformation($"VotingAllowed: {VotingAllowed}");
            Logger.LogInformation($"CountRTV: {CountRTV}");
            Logger.LogInformation($"myTimer: {myTimer}");
        }
    }

    /*
     * 
     * RTVCache.Clear();
        IfVoted.Clear();
        TimeToVote = StoreTime;
        WarmupEnd = false;
        VotingAllowed = true;
        CountRTV = 0;
        RTVByUsers = false;
     * 
     * 
    public void MapDebuging()
    {
        LoggingDebugging("***** Maps: *****");
        foreach (var maps in Maps)
        {
            LoggingDebugging($"Maps cache: Key: {maps.Key}, {maps.Value}");
        }
        
    }

    public void RandomStuff()
    {
        return;
    }

    public void RTVCommand()
    {
        if (RTVCache.Count == 0)
        {
            LoggingDebugging("Cache is empty.");
        }
        foreach (var kvp in RTVCache)
        {
            LoggingDebugging($"Key: {kvp.Key}, Value: {kvp.Value}");
        }
    }
    */

}
