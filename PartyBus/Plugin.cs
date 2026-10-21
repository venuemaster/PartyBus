using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PartyBus.Windows;

namespace PartyBus;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IPartyFinderGui PartyFinderGui { get; private set; } = null!;

    private static readonly string[] Commands = { "/partybus", "/pbus" };

    public Configuration Config { get; }
    public ChatQueue Queue { get; }
    public Bus Bus { get; }

    private readonly WindowSystem windowSystem = new("PartyBus");
    private readonly MainWindow window;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Queue = new ChatQueue(this);
        Bus = new Bus(this);
        window = new MainWindow(this);
        windowSystem.AddWindow(window);

        foreach (var c in Commands)
            CommandManager.AddHandler(c, new CommandInfo((_, _) => window.Toggle())
            {
                HelpMessage = "Open Party Bus: your crew's venue list.",
            });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        ChatGui.ChatMessage += OnChatMessage;
        Framework.Update += OnFrameworkUpdate;
        PartyFinderGui.ReceiveListing += Bus.OnListing;

        if (Config.AutoSync) Bus.ScheduleHello(15);
    }

    public void Dispose()
    {
        PartyFinderGui.ReceiveListing -= Bus.OnListing;
        ChatGui.ChatMessage -= OnChatMessage;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        windowSystem.RemoveAllWindows();
        foreach (var c in Commands) CommandManager.RemoveHandler(c);
    }

    private void ToggleWindow() => window.Toggle();

    private void OnChatMessage(IHandleableChatMessage msg)
    {
        var type = (XivChatType)((int)msg.LogKind & 0x7F);
        var message = msg.Message;
        if (type is XivChatType.SystemMessage or XivChatType.SystemError or XivChatType.Notice
            && message.TextValue.StartsWith("Your message was not heard", System.StringComparison.OrdinalIgnoreCase))
        {
            Queue.OnBlocked();
            return;
        }
        try
        {
            if (Bus.OnChat(type, msg.Sender, message)) msg.PreventOriginal();
        }
        catch (System.Exception ex) { Log.Error(ex, "Party Bus failed to read a chat message"); }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try { Queue.Update(); } catch (System.Exception ex) { Log.Error(ex, "Chat queue failed"); }
        try { Bus.Update(); } catch (System.Exception ex) { Log.Error(ex, "Party Bus update failed"); }
    }

    public void Save() => PluginInterface.SavePluginConfig(Config);
    public void SendLine(string line) => Queue.Enqueue(line);
}
