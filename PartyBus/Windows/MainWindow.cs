using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PartyBus.Windows;

public class MainWindow : Window
{
    private readonly Plugin plugin;
    private Bus Bus => plugin.Bus;
    private Configuration Config => plugin.Config;

    private static readonly Vector4 Green = new(0.35f, 0.95f, 0.45f, 1f);
    private static readonly Vector4 Red = new(1f, 0.38f, 0.38f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Pink = new(1f, 0.45f, 0.85f, 1f);

    // list filters
    private string search = string.Empty;
    private int markerFilter;   // 0 all, 1 green, 2 red, 3 unrated
    private string dcFilter = "All";
    private bool openOnly;

    // editor
    private Venue? editing;
    private Venue draft = new();
    private string draftMsg = string.Empty;
    private int tab = -1;

    // crew / import
    private string newCrew = string.Empty;
    private string importCode = string.Empty;
    private string importMsg = string.Empty;

    public MainWindow(Plugin plugin) : base("Party Bus###PartyBusMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(640, 440), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }

    private void Save() => plugin.Save();

    public override void Draw()
    {
        var open = Bus.Active.Count(Bus.IsOpen);
        ImGui.TextColored(Pink, $"{Bus.Active.Count()} venues");
        ImGui.SameLine();
        ImGui.TextColored(open > 0 ? Green : Grey, open > 0 ? $"-  {open} open right now" : "-  none seen open yet (open Party Finder to check)");
        if (Bus.SyncStatus.Length > 0) { ImGui.SameLine(); ImGui.TextColored(Grey, "  |  " + Bus.SyncStatus); }

        if (!ImGui.BeginTabBar("##pbtabs")) return;
        var openCount = Bus.OpenNow(Config.OpenNowMinutes, Config.OpenNowVenuesOnly).Count();
        if (ImGui.BeginTabItem($"Open Now ({openCount})###opennow")) { DrawOpenNow(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Venues###venues")) { DrawList(); ImGui.EndTabItem(); }
        var editFlags = tab == 1 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (ImGui.BeginTabItem(editing == null ? "Add a venue###edit" : "Edit venue###edit", editFlags)) { DrawEditor(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Crew & Sync###crew")) { DrawCrew(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Settings###settings")) { DrawSettings(); ImGui.EndTabItem(); }
        tab = -1;
        ImGui.EndTabBar();
    }

    // ================================================================ list
    private void DrawList()
    {
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##search", "search name, world, notes...", ref search, 60);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        string[] markers = { "All", "Green", "Red", "Not rated" };
        if (ImGui.BeginCombo("##mk", markers[markerFilter]))
        {
            for (var i = 0; i < markers.Length; i++) if (ImGui.Selectable(markers[i], markerFilter == i)) markerFilter = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        if (ImGui.BeginCombo("##dc", dcFilter))
        {
            foreach (var dc in new[] { "All" }.Concat(Bus.Active.Select(v => v.DataCenter).Where(d => d.Length > 0).Distinct().OrderBy(d => d)))
                if (ImGui.Selectable(dc, dc == dcFilter)) dcFilter = dc;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.Checkbox("Open now", ref openOnly);

        var s = search.Trim();
        var list = Bus.Active
            .Where(v => s.Length == 0 || v.Name.Contains(s, StringComparison.OrdinalIgnoreCase) || v.World.Contains(s, StringComparison.OrdinalIgnoreCase)
                        || v.Notes.Contains(s, StringComparison.OrdinalIgnoreCase) || v.District.Contains(s, StringComparison.OrdinalIgnoreCase))
            .Where(v => markerFilter switch { 1 => Bus.Marker(v) == true, 2 => Bus.Marker(v) == false, 3 => Bus.Marker(v) == null, _ => true })
            .Where(v => dcFilter == "All" || v.DataCenter == dcFilter)
            .Where(v => !openOnly || Bus.IsOpen(v))
            .OrderByDescending(Bus.IsOpen).ThenByDescending(v => Bus.Marker(v) == true).ThenBy(v => v.Name)
            .ToList();

        if (!ImGui.BeginTable("##venues", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
                new Vector2(0, ImGui.GetContentRegionAvail().Y - 4))) return;
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 22);
        ImGui.TableSetupColumn("Venue", ImGuiTableColumnFlags.WidthStretch, 0);
        ImGui.TableSetupColumn("Address", ImGuiTableColumnFlags.WidthStretch, 0);
        ImGui.TableSetupColumn("Crew", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Me", ImGuiTableColumnFlags.WidthFixed, 92);
        ImGui.TableSetupColumn("##act", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableHeadersRow();

        foreach (var v in list)
        {
            ImGui.PushID(v.Id);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var m = Bus.Marker(v);
            Dot(m == true ? Green : m == false ? Red : Grey, m != null);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(m == true ? "Green: liked" : m == false ? "Red: didn't like it" : "Not rated yet");

            ImGui.TableNextColumn();
            ImGui.Text(v.Name);
            if (Bus.IsOpen(v))
            {
                ImGui.SameLine(); ImGui.TextColored(Green, "OPEN");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Seen {Ago(v.LastSeenOpen)}\n{v.LastSeenInfo}");
            }
            if (v.Notes.Length > 0) { ImGui.SameLine(); ImGui.TextColored(Grey, "(i)"); if (ImGui.IsItemHovered()) ImGui.SetTooltip(v.Notes); }

            ImGui.TableNextColumn();
            ImGui.TextColored(Grey, Places.Address(v));

            ImGui.TableNextColumn();
            ImGui.TextColored(Green, Bus.Likes(v).ToString()); ImGui.SameLine(); ImGui.TextColored(Grey, "/"); ImGui.SameLine();
            ImGui.TextColored(Red, Bus.Dislikes(v).ToString());
            if (ImGui.IsItemHovered() && v.Ratings.Count > 0)
                ImGui.SetTooltip(string.Join("\n", v.Ratings.Where(r => !r.Value.Cleared).Select(r => $"{r.Key}: {(r.Value.Liked ? "liked it" : "didn't like it")}")));

            ImGui.TableNextColumn();
            var mine = Bus.MyRating(v);
            if (RateButton("Like", mine == true, Green)) Bus.Rate(v, mine == true ? null : true);
            ImGui.SameLine();
            if (RateButton("Nope", mine == false, Red)) Bus.Rate(v, mine == false ? null : false);

            ImGui.TableNextColumn();
            if (ImGui.SmallButton("Copy")) ImGui.SetClipboardText($"{v.Name} - {Places.Address(v)}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copy the name and address");
            ImGui.SameLine();
            if (ImGui.SmallButton("Edit")) { editing = v; draft = Clone(v); draftMsg = string.Empty; tab = 1; }
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    /// <summary>A filled (or hollow) colored circle marker.</summary>
    private static void Dot(Vector4 color, bool filled)
    {
        var size = ImGui.GetTextLineHeight();
        var p = ImGui.GetCursorScreenPos();
        var center = new Vector2(p.X + size / 2, p.Y + size / 2);
        var dl = ImGui.GetWindowDrawList();
        if (filled) dl.AddCircleFilled(center, size * 0.38f, ImGui.GetColorU32(color));
        else dl.AddCircle(center, size * 0.34f, ImGui.GetColorU32(color));
        ImGui.Dummy(new Vector2(size, size));
    }

    private static bool RateButton(string label, bool on, Vector4 color)
    {
        if (on) ImGui.PushStyleColor(ImGuiCol.Button, color with { W = 0.55f });
        var clicked = ImGui.SmallButton(label);
        if (on) ImGui.PopStyleColor();
        return clicked;
    }

    private static string Ago(long unix)
    {
        var t = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
        return t.TotalMinutes < 1 ? "just now" : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min ago" : $"{(int)t.TotalHours}h {t.Minutes}m ago";
    }

    private static Venue Clone(Venue v) => new()
    {
        Id = v.Id, Name = v.Name, DataCenter = v.DataCenter, World = v.World, District = v.District,
        Ward = v.Ward, Plot = v.Plot, Notes = v.Notes,
    };

    // ================================================================ editor
    private void DrawEditor()
    {
        if (ImGui.Button("Fill in from where I'm standing"))
        {
            if (Places.TryHere(out var w, out var dc, out var d, out var ward, out var plot))
            {
                draft.World = w; draft.DataCenter = dc; draft.District = d; draft.Ward = ward; draft.Plot = plot;
                draftMsg = $"Got it: {Places.Address(draft)}";
            }
            else draftMsg = "Stand inside the venue, or on its plot in the housing ward, then try again.";
        }
        ImGui.SameLine();
        ImGui.TextColored(Grey, draftMsg);

        ImGui.Spacing();
        var name = draft.Name;
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("Venue name", ref name, 60)) draft.Name = name;

        var dcv = draft.DataCenter;
        ImGui.SetNextItemWidth(140);
        if (ImGui.InputText("Data center", ref dcv, 20)) draft.DataCenter = dcv;
        ImGui.SameLine();
        var wv = draft.World;
        ImGui.SetNextItemWidth(140);
        if (ImGui.InputText("World", ref wv, 20))
        {
            draft.World = wv;
            if (draft.DataCenter.Length == 0) { var found = Places.DataCenterOf(wv.Trim()); if (found.Length > 0) draft.DataCenter = found; }
        }

        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("District", draft.District.Length > 0 ? draft.District : "pick one"))
        {
            foreach (var d in Places.Districts) if (ImGui.Selectable(d, d == draft.District)) draft.District = d;
            ImGui.EndCombo();
        }
        var wardv = draft.Ward;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Ward", ref wardv, 1)) draft.Ward = Math.Clamp(wardv, 0, 30);
        ImGui.SameLine();
        var plotv = draft.Plot;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Plot (0 = apartment)", ref plotv, 1)) draft.Plot = Math.Clamp(plotv, 0, 60);

        var notes = draft.Notes;
        ImGui.InputTextMultiline("Notes", ref notes, 300, new Vector2(-80, 60));
        draft.Notes = notes;

        ImGui.Spacing();
        var ok = draft.Name.Trim().Length > 0 && draft.World.Trim().Length > 0 && draft.Ward > 0;
        ImGui.BeginDisabled(!ok);
        if (ImGui.Button(editing == null ? "Save venue" : "Save changes"))
        {
            if (editing == null)
            {
                Bus.Add(new Venue
                {
                    Name = draft.Name.Trim(), DataCenter = draft.DataCenter.Trim(), World = draft.World.Trim(), District = draft.District,
                    Ward = draft.Ward, Plot = draft.Plot, Notes = draft.Notes.Trim(),
                });
                draftMsg = $"Saved {draft.Name.Trim()}.";
            }
            else
            {
                editing.Name = draft.Name.Trim(); editing.DataCenter = draft.DataCenter.Trim(); editing.World = draft.World.Trim();
                editing.District = draft.District; editing.Ward = draft.Ward; editing.Plot = draft.Plot; editing.Notes = draft.Notes.Trim();
                Bus.Changed(editing);
                draftMsg = $"Saved changes to {editing.Name}.";
                editing = null;
            }
            draft = new Venue();
        }
        ImGui.EndDisabled();
        if (!ok) { ImGui.SameLine(); ImGui.TextColored(Grey, "Needs a name, world and ward."); }

        if (editing != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) { editing = null; draft = new Venue(); }
            ImGui.SameLine();
            if (ImGui.Button("Delete venue") && ImGui.GetIO().KeyCtrl) { Bus.Delete(editing); editing = null; draft = new Venue(); draftMsg = "Deleted."; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click. Removes it for the whole crew.");
            ImGui.TextColored(Grey, $"Added by {editing?.AddedBy}");
        }
        ImGui.Spacing();
        ImGui.TextColored(Grey, "New venues and changes are shared with your crew automatically when you're in a party together.");
    }

    // ================================================================ open now
    private string openSearch = string.Empty;
    private bool openNewOnly;

    private void DrawOpenNow()
    {
        if (Bus.LastPfRefresh == DateTime.MinValue)
        {
            ImGui.TextWrapped("Open Party Finder in game (/partyfinder) and every venue advertising right now shows up here - even ones that aren't on your list. Reopen or refresh Party Finder to update it.");
            if (ImGui.Button("Open Party Finder")) Chat.Send("/partyfinder");
            return;
        }
        ImGui.TextColored(Grey, $"From Party Finder, last read {AgoTime(Bus.LastPfRefresh)}. Shows your current data center.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Open Party Finder")) Chat.Send("/partyfinder");

        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##osearch", "search listings...", ref openSearch, 60);
        ImGui.SameLine();
        var vo = Config.OpenNowVenuesOnly;
        if (ImGui.Checkbox("Venues only", ref vo)) { Config.OpenNowVenuesOnly = vo; Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Off: show everything in Party Finder's \"None\" category (some aren't venues).");
        ImGui.SameLine();
        ImGui.Checkbox("Not on my list", ref openNewOnly);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        var mins = Config.OpenNowMinutes;
        if (ImGui.InputInt("min", ref mins, 5)) { Config.OpenNowMinutes = Math.Clamp(mins, 5, 240); Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Keep listings for this many minutes after they were last seen in Party Finder.");

        var q = openSearch.Trim();
        var rows = Bus.OpenNow(Config.OpenNowMinutes, Config.OpenNowVenuesOnly)
            .Select(s => (S: s, V: s.MatchedVenueId == null ? null : Bus.Active.FirstOrDefault(v => v.Id == s.MatchedVenueId)))
            .Where(r => !openNewOnly || r.V == null)
            .Where(r => q.Length == 0 || r.S.Description.Contains(q, StringComparison.OrdinalIgnoreCase) || r.S.Recruiter.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || r.S.World.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.V != null && Bus.Marker(r.V) == true)
            .ThenBy(r => r.V != null && Bus.Marker(r.V) == false)
            .ThenBy(r => r.S.World).ThenBy(r => r.S.Title)
            .ToList();

        if (!ImGui.BeginTable("##open", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
                new Vector2(0, ImGui.GetContentRegionAvail().Y - 4))) return;
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 22);
        ImGui.TableSetupColumn("Venue", ImGuiTableColumnFlags.WidthStretch, 0);
        ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthFixed, 190);
        ImGui.TableSetupColumn("##a", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableHeadersRow();
        for (var i = 0; i < rows.Count; i++)
        {
            var (s, v) = rows[i];
            ImGui.PushID("on" + i);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (v != null) { var m = Bus.Marker(v); Dot(m == true ? Green : m == false ? Red : Grey, m != null); }
            else { ImGui.TextColored(Gold, "new"); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(v == null ? "Not on your list yet" : $"On your list as {v.Name}");

            ImGui.TableNextColumn();
            ImGui.Text(v?.Name ?? (s.Title.Length > 0 ? s.Title : s.Recruiter));
            ImGui.SameLine(); ImGui.TextColored(Grey, $"by {s.Recruiter}");
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(Grey, s.Description);
            ImGui.PopTextWrapPos();

            ImGui.TableNextColumn();
            ImGui.Text($"{s.World} ({s.DataCenter})");
            if (s.Ward > 0) ImGui.TextColored(Grey, $"{(s.District.Length > 0 ? s.District : "?")} W{s.Ward}{(s.Plot > 0 ? $" P{s.Plot}" : "")}");
            else if (v != null) ImGui.TextColored(Grey, $"{v.District} W{v.Ward}{(v.Plot > 0 ? $" P{v.Plot}" : "")}");
            ImGui.TextColored(Grey, $"seen {AgoTime(s.Time)}");

            ImGui.TableNextColumn();
            if (v == null)
            {
                if (ImGui.SmallButton("Add to my list"))
                {
                    editing = null;
                    draft = new Venue { Name = s.Title, World = s.World, DataCenter = s.DataCenter, District = s.District, Ward = s.Ward, Plot = s.Plot, Notes = $"From PF ({s.Recruiter})" };
                    draftMsg = "Filled in from the listing - check the name and address.";
                    tab = 1;
                }
            }
            else
            {
                var mine = Bus.MyRating(v);
                if (RateButton("Like", mine == true, Green)) Bus.Rate(v, mine == true ? null : true);
                ImGui.SameLine();
                if (RateButton("Nope", mine == false, Red)) Bus.Rate(v, mine == false ? null : false);
            }
            if (ImGui.SmallButton("Copy")) ImGui.SetClipboardText(s.Ward > 0
                ? $"{v?.Name ?? s.Title} - {(s.District.Length > 0 ? s.District : "?")} Ward {s.Ward}{(s.Plot > 0 ? $" Plot {s.Plot}" : "")}, {s.World} ({s.DataCenter})"
                : v != null ? $"{v.Name} - {Places.Address(v)}" : $"{s.Title} ({s.World}): {s.Description}");
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private static string AgoTime(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : $"{(int)d.TotalHours}h {d.Minutes}m ago";
    }

    // ================================================================ crew
    private void DrawCrew()
    {
        ImGui.TextWrapped("Party Bus shares your list through party chat. The sync lines are hidden for anyone who has Party Bus, but party members without it will see them, so only sync with your crew.");
        ImGui.Spacing();

        var auto = Config.AutoSync;
        if (ImGui.Checkbox("Sync automatically when I join a party or a crewmate joins mine", ref auto)) { Config.AutoSync = auto; Save(); }
        if (ImGui.Button("Sync with my party now")) Bus.SyncNow();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Sends your venues to the party and asks everyone for theirs.\nYour crewmates need YOU on their crew list too, or their Party Bus ignores it.");
        ImGui.SameLine();
        if (ImGui.Button("Send my whole list")) Bus.SendEverything();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("For a new crewmate. Takes a few lines of party chat, sent a few seconds apart.");

        ImGui.Separator();
        ImGui.TextColored(Pink, $"Crew ({Config.Crew.Count}) - only these people can change your list");
        var any = Config.AcceptFromAnyoneInParty;
        if (ImGui.Checkbox("Accept updates from anyone in my party (not just my crew)", ref any)) { Config.AcceptFromAnyoneInParty = any; Save(); }

        ImGui.SetNextItemWidth(200);
        var enter = ImGui.InputTextWithHint("##crew", "character name", ref newCrew, 40, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button("Add") || enter) && newCrew.Trim().Length > 0) { AddCrew(newCrew.Trim()); newCrew = string.Empty; }
        ImGui.SameLine();
        if (ImGui.Button("Add everyone in my party"))
            foreach (var n in Places.PartyNames()) AddCrew(n);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Works for normal and cross-world parties.");

        var waiting = Bus.Ignored.Keys.Where(k => !Config.Crew.Any(c => c.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList();
        if (waiting.Count > 0)
        {
            ImGui.TextColored(Gold, "These people tried to sync with you but aren't on your crew:");
            foreach (var w in waiting)
            {
                ImGui.PushID("ign" + w);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Add {w}")) { AddCrew(w); Bus.Ignored.Remove(w); }
                ImGui.PopID();
            }
        }

        string? remove = null;
        foreach (var c in Config.Crew)
        {
            ImGui.PushID("c" + c);
            if (ImGui.SmallButton("x")) remove = c;
            ImGui.SameLine(); ImGui.Text(c);
            if (Config.LastSyncedWith.TryGetValue(c, out var t)) { ImGui.SameLine(); ImGui.TextColored(Grey, $"last synced {Ago(t)}"); }
            ImGui.PopID();
        }
        if (remove != null) { Config.Crew.Remove(remove); Save(); }

        if (Bus.SyncLog.Count > 0 && ImGui.CollapsingHeader($"Sync log ({Bus.SyncLog.Count})"))
            foreach (var l in Bus.SyncLog) ImGui.TextColored(Grey, l);

        ImGui.Separator();
        ImGui.TextColored(Pink, "Share without a party");
        if (ImGui.Button("Copy share code (all venues)")) ImGui.SetClipboardText(Bus.Export(false));
        ImGui.SameLine();
        if (ImGui.Button("Copy share code (green only)")) ImGui.SetClipboardText(Bus.Export(true));
        ImGui.SetNextItemWidth(-90);
        ImGui.InputTextWithHint("##imp", "paste a friend's share code here", ref importCode, 60000);
        ImGui.SameLine();
        if (ImGui.Button("Import"))
        {
            try
            {
                var n = Bus.Import(importCode);
                importMsg = n < 0 ? "That isn't a Party Bus code." : $"Imported {n} update(s).";
                if (n >= 0) importCode = string.Empty;
            }
            catch { importMsg = "That code looks damaged - copy it again."; }
        }
        if (importMsg.Length > 0) ImGui.TextColored(Grey, importMsg);
    }

    private void AddCrew(string name)
    {
        var me = Bus.MyName;
        if (name.Length == 0 || name.Equals(me, StringComparison.OrdinalIgnoreCase)) return;
        if (Config.Crew.Any(c => c.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        Config.Crew.Add(name);
        Save();
    }

    // ================================================================ settings
    private void DrawSettings()
    {
        Check("Read Party Finder listings (for Open Now and OPEN alerts)", Config.ScanPartyFinder, v => Config.ScanPartyFinder = v);
        ImGui.Spacing();
        ImGui.TextColored(Pink, "When a venue on your list shows up open in Party Finder");
        Check("Tell me in chat", Config.NotifyMe, v => Config.NotifyMe = v);
        ImGui.SameLine();
        Check("with a sound", Config.NotifySound, v => Config.NotifySound = v);
        Check("Ping my party (\"[Party Bus] Name is OPEN! ...\")", Config.PingParty, v => Config.PingParty = v);
        Check("Only for green venues", Config.OnlyLikedVenues, v => Config.OnlyLikedVenues = v);
        var cd = Config.PingCooldownHours;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Hours before pinging about the same venue again", ref cd, 1)) { Config.PingCooldownHours = Math.Clamp(cd, 1, 24); Save(); }
        var oh = Config.OpenForHours;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Show OPEN for this many hours after it's seen", ref oh, 1)) { Config.OpenForHours = Math.Clamp(oh, 1, 12); Save(); }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(180);
        var gap = Config.SecondsBetweenMessages;
        if (ImGui.SliderFloat("Seconds between party messages", ref gap, 1.0f, 8.0f, "%.1f s")) { Config.SecondsBetweenMessages = gap; Save(); }
        if (plugin.Queue.Pending > 0) ImGui.TextColored(Gold, $"{plugin.Queue.Pending} message(s) waiting to send");
    }

    private void Check(string label, bool value, Action<bool> set)
    {
        var v = value;
        if (ImGui.Checkbox(label, ref v)) { set(v); Save(); }
    }
}
