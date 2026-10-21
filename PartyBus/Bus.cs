using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace PartyBus;

public class PfSighting
{
    public ulong ListingId;
    public DateTime FirstSeen;
    public DateTime Time;          // last seen
    public string Title = string.Empty;
    public bool NoneCategory;
    public bool LooksLikeVenue;
    public string Recruiter = string.Empty;
    public string World = string.Empty;
    public string DataCenter = string.Empty;
    public string Description = string.Empty;
    public string District = string.Empty;
    public int Ward, Plot;
    public string? MatchedVenueId;
}

/// <summary>
/// The venue list plus the two ways it changes from outside:
/// 1. Party chat sync. Lines start with "PB1|" and are hidden from chat for anyone with Party Bus.
///    H = hello ("I'm here, send me what changed"), D = data (venue and rating records).
/// 2. Party Finder listings that match a saved venue mark it OPEN and can ping the party.
/// </summary>
public class Bus
{
    public const string Prefix = "PB1|";
    private const string PingTag = "[Party Bus] ";
    private const int MaxLineBytes = 430;

    private readonly Plugin plugin;
    private Configuration Config => plugin.Config;

    public readonly List<PfSighting> Sightings = new();
    public string SyncStatus { get; private set; } = string.Empty;
    private readonly Dictionary<string, DateTime> lastHelloFrom = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> pendingSince = new(StringComparer.OrdinalIgnoreCase);   // sent to them, waiting for their "got it"
    private readonly Dictionary<string, DateTime> ackDue = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>People with Party Bus who tried to sync with you but aren't on your crew list.</summary>
    public readonly Dictionary<string, DateTime> Ignored = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> SyncLog = new();

    private void LogSync(string text)
    {
        SyncLog.Insert(0, $"{DateTime.Now:h:mm tt}  {text}");
        if (SyncLog.Count > 60) SyncLog.RemoveAt(SyncLog.Count - 1);
        SyncStatus = text;
    }
    private DateTime lastHelloSent = DateTime.MinValue;
    private DateTime helloDue = DateTime.MinValue;

    public Bus(Plugin plugin) => this.plugin = plugin;

    public static string MyName => Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

    public IEnumerable<Venue> Active => Config.Venues.Where(v => !v.Deleted);

    // ======================= editing =======================
    public Venue Add(Venue v)
    {
        v.AddedBy = MyName;
        v.Updated = v.ReceivedAt = Places.Now;
        Config.Venues.Add(v);
        plugin.Save();
        Broadcast(v, includeRatings: true);
        return v;
    }

    public void Changed(Venue v)
    {
        v.Updated = v.ReceivedAt = Places.Now;
        plugin.Save();
        Broadcast(v, includeRatings: false);
    }

    public void Delete(Venue v)
    {
        v.Deleted = true;
        Changed(v);
    }

    public void Rate(Venue v, bool? liked)
    {
        var me = MyName;
        if (me.Length == 0) return;
        var r = new Rating { Liked = liked ?? false, Cleared = liked == null, Time = Places.Now };
        v.Ratings[me] = r;
        v.ReceivedAt = Places.Now;
        plugin.Save();
        if (Places.InParty()) SendRecords(new[] { RatingRecord(v, me, r) });
    }

    public bool? MyRating(Venue v) => v.Ratings.TryGetValue(MyName, out var r) && !r.Cleared ? r.Liked : null;
    public int Likes(Venue v) => v.Ratings.Values.Count(r => !r.Cleared && r.Liked);
    public int Dislikes(Venue v) => v.Ratings.Values.Count(r => !r.Cleared && !r.Liked);

    /// <summary>Green if you liked it (or, if you haven't rated it, if most of the crew did).</summary>
    public bool? Marker(Venue v)
    {
        var mine = MyRating(v);
        if (mine != null) return mine;
        int l = Likes(v), d = Dislikes(v);
        return l == d ? null : l > d;
    }

    public bool IsOpen(Venue v) => v.LastSeenOpen > 0 && Places.Now - v.LastSeenOpen < Config.OpenForHours * 3600L;

    // ======================= sync: sending =======================
    private static string Clean(string s) => s.Replace("|", "/").Replace("^", "'").Replace("~", "-").Replace("\n", " ").Trim();

    private static string VenueRecord(Venue v)
        => string.Join("^", "V", v.Id, v.Updated, Clean(v.Name), Clean(v.DataCenter), Clean(v.World), Clean(v.District),
            v.Ward, v.Plot, Clean(v.Notes.Length > 100 ? v.Notes[..100] : v.Notes), Clean(v.AddedBy), v.Deleted ? 1 : 0);

    private static string RatingRecord(Venue v, string person, Rating r)
        => string.Join("^", "R", v.Id, Clean(person), r.Cleared ? "-" : r.Liked ? "G" : "X", r.Time);

    private IEnumerable<string> RecordsFor(Venue v, bool includeRatings)
    {
        yield return VenueRecord(v);
        if (includeRatings)
            foreach (var (p, r) in v.Ratings) yield return RatingRecord(v, p, r);
    }

    private void Broadcast(Venue v, bool includeRatings)
    {
        if (Places.InParty()) SendRecords(RecordsFor(v, includeRatings));
    }

    /// <summary>Packs records into as few party chat lines as possible.</summary>
    private void SendRecords(IEnumerable<string> records)
    {
        var line = new StringBuilder();
        foreach (var rec in records)
        {
            if (line.Length > 0 && Encoding.UTF8.GetByteCount(line + "~" + rec) + Prefix.Length + 5 > MaxLineBytes)
            {
                plugin.SendLine($"/p {Prefix}D|{line}");
                line.Clear();
            }
            if (line.Length > 0) line.Append('~');
            line.Append(rec);
        }
        if (line.Length > 0) plugin.SendLine($"/p {Prefix}D|{line}");
    }

    /// <summary>Everything that changed on this computer since we last synced with this person.</summary>
    private void SendChangesFor(string person)
    {
        Config.LastSyncedWith.TryGetValue(person, out var since);
        var recs = new List<string>();
        foreach (var v in Config.Venues.Where(v => Math.Max(v.Updated, v.ReceivedAt) > since))
            recs.AddRange(RecordsFor(v, includeRatings: true));
        if (recs.Count == 0) return;
        // Only marked as synced once they reply "got it" (so a missed sync is retried next time).
        pendingSince[person] = Places.Now;
        LogSync($"Sent {recs.Count(r => r[0] == 'V')} venue(s) to {person}");
        SendRecords(recs);
    }

    public void SendHello()
    {
        if (!Places.InParty()) { SyncStatus = "You're not in a party."; return; }
        lastHelloSent = DateTime.Now;
        plugin.SendLine($"/p {Prefix}H|{Active.Count()}");
        LogSync("Asked the party for updates");
    }

    /// <summary>The "Sync now" button: ask for their changes AND send mine (everything they might not have).</summary>
    public void SyncNow()
    {
        if (!Places.InParty()) { LogSync("You're not in a party."); return; }
        SendHello();
        var since = Config.Crew.Count == 0 ? 0 : Config.Crew.Select(c => Config.LastSyncedWith.TryGetValue(c, out var t) ? t : 0).Min();
        var recs = Config.Venues.Where(v => Math.Max(v.Updated, v.ReceivedAt) > since).SelectMany(v => RecordsFor(v, true)).ToList();
        if (recs.Count == 0) return;
        foreach (var c in Config.Crew) pendingSince[c] = Places.Now;
        LogSync($"Sent {recs.Count(r => r[0] == 'V')} venue(s) to the party");
        SendRecords(recs);
    }

    /// <summary>Sends your entire list to the party (for a friend who's brand new or out of date).</summary>
    public void SendEverything()
    {
        if (!Places.InParty()) { SyncStatus = "You're not in a party."; return; }
        var recs = Config.Venues.SelectMany(v => RecordsFor(v, true)).ToList();
        foreach (var c in Config.Crew) pendingSince[c] = Places.Now;
        SendRecords(recs);
        LogSync($"Sending all {Active.Count()} venues to the party...");
    }

    public void ScheduleHello(int seconds) => helloDue = DateTime.Now.AddSeconds(seconds);

    public void Update()
    {
        foreach (var (person, due) in ackDue.ToList())
        {
            if (DateTime.Now < due) continue;
            ackDue.Remove(person);
            plugin.SendLine($"/p {Prefix}A|{Clean(person)}");
        }
        if (helloDue != DateTime.MinValue && DateTime.Now >= helloDue)
        {
            helloDue = DateTime.MinValue;
            if (Config.AutoSync && Places.InParty()) SendHello();
        }
    }

    // ======================= sync: receiving =======================
    private static readonly Regex JoinRx = new(@"(joins? (the|your|a|.+?'s) (cross-world )?party|party has been formed|You join)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Returns true if the line belongs to Party Bus and should be hidden.</summary>
    public bool OnChat(XivChatType type, SeString sender, SeString message)
    {
        var text = message.TextValue;

        if (type is XivChatType.SystemMessage or XivChatType.Notice or XivChatType.SystemError)
        {
            if (Config.AutoSync && JoinRx.IsMatch(text)) ScheduleHello(6);
            return false;
        }
        if (type is not (XivChatType.Party or XivChatType.CrossParty)) return false;

        var from = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault()?.PlayerName ?? StripName(sender.TextValue);
        var self = from.Length == 0 || from.Equals(MyName, StringComparison.OrdinalIgnoreCase);

        if (text.StartsWith(PingTag, StringComparison.Ordinal))
        {
            if (!self) OnPing(text[PingTag.Length..], from);
            return false;   // pings stay visible for everyone
        }
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        if (self) return true;
        if (!Allowed(from))
        {
            if (!Ignored.ContainsKey(from)) LogSync($"Ignored sync from {from} - not on your crew list");
            Ignored[from] = DateTime.Now;
            return true;
        }

        try
        {
            var body = text[Prefix.Length..];
            if (body.StartsWith("H|"))
            {
                if (lastHelloFrom.TryGetValue(from, out var t) && (DateTime.Now - t).TotalSeconds < 20) return true;
                lastHelloFrom[from] = DateTime.Now;
                SendChangesFor(from);
                // ask them back, so changes flow both ways
                if ((DateTime.Now - lastHelloSent).TotalSeconds > 60) SendHello();
            }
            else if (body.StartsWith("D|"))
            {
                var n = 0;
                foreach (var rec in body[2..].Split('~')) n += Apply(rec.Split('^')) ? 1 : 0;
                if (n > 0) { plugin.Save(); LogSync($"Got {n} update(s) from {from}"); }
                ackDue[from] = DateTime.Now.AddSeconds(4);   // one "got it" per burst of lines
            }
            else if (body.StartsWith("A|"))
            {
                // "got it" - only counts if it's addressed to me
                if (body[2..].Trim().Equals(Clean(MyName), StringComparison.OrdinalIgnoreCase) && pendingSince.TryGetValue(from, out var sentAt))
                {
                    Config.LastSyncedWith[from] = sentAt;
                    pendingSince.Remove(from);
                    plugin.Save();
                    LogSync($"{from} got your venues");
                }
            }
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Bad Party Bus line from {0}", from); }
        return true;
    }

    private static string StripName(string s)
    {
        // party sender text can carry a party number glyph in front; keep letters, apostrophes, hyphens and spaces
        var sb = new StringBuilder();
        foreach (var c in s) if (char.IsLetter(c) || c is ' ' or '\'' or '-') sb.Append(c);
        return sb.ToString().Trim();
    }

    public bool Allowed(string person)
        => Config.AcceptFromAnyoneInParty || Config.Crew.Any(c => c.Equals(person, StringComparison.OrdinalIgnoreCase));

    private bool Apply(string[] f)
    {
        if (f.Length == 0) return false;
        if (f[0] == "V" && f.Length >= 12)
        {
            var upd = long.Parse(f[2]);
            var v = Config.Venues.FirstOrDefault(x => x.Id == f[1]);
            if (v == null) { v = new Venue { Id = f[1] }; Config.Venues.Add(v); }
            else if (v.Updated >= upd) return false;
            v.Updated = upd; v.ReceivedAt = Places.Now;
            v.Name = f[3]; v.DataCenter = f[4]; v.World = f[5]; v.District = f[6];
            v.Ward = int.Parse(f[7]); v.Plot = int.Parse(f[8]); v.Notes = f[9]; v.AddedBy = f[10]; v.Deleted = f[11] == "1";
            return true;
        }
        if (f[0] == "R" && f.Length >= 5)
        {
            var v = Config.Venues.FirstOrDefault(x => x.Id == f[1]);
            if (v == null) return false;   // the venue record always comes first; ignore strays
            var time = long.Parse(f[4]);
            if (v.Ratings.TryGetValue(f[2], out var old) && old.Time >= time) return false;
            v.Ratings[f[2]] = new Rating { Liked = f[3] == "G", Cleared = f[3] == "-", Time = time };
            v.ReceivedAt = Places.Now;
            return true;
        }
        return false;
    }

    // ======================= party finder =======================
    private static readonly string[] VenueWords = { "venue", "club", "bar", "lounge", "cafe", "café", "tavern", "nightclub", "open", "ward", "plot", "mist", "lavender", "goblet", "shirogane", "empyreum", "dj", "drinks" };

    public void OnListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        if (!Config.ScanPartyFinder) return;
        var desc = listing.Description.TextValue.Trim();
        if (desc.Length == 0) return;
        var worldId = listing.World.RowId;
        var world = Places.WorldName(worldId);
        var dc = Places.DataCenterOf(worldId);
        var (district, ward, plot) = Places.ParseAddress(desc);

        var match = Active.FirstOrDefault(v => Matches(v, desc, world, district, ward, plot));
        var lower = desc.ToLowerInvariant();
        var noneCategory = (uint)listing.Category == 0;   // venues post under "None"
        var looksLikeVenue = match != null || (ward > 0 && district.Length > 0) || VenueWords.Count(w => lower.Contains(w)) >= 2;
        if (!looksLikeVenue && !noneCategory) return;

        var recruiter = listing.Name.TextValue;
        var existing = Sightings.FirstOrDefault(s => s.ListingId == listing.Id && listing.Id != 0)
                       ?? Sightings.FirstOrDefault(s => s.Recruiter == recruiter && s.Description == desc);
        if (existing != null)
        {
            existing.Time = DateTime.Now; existing.MatchedVenueId = match?.Id; existing.Description = desc;
            existing.Title = TitleOf(desc); existing.District = district; existing.Ward = ward; existing.Plot = plot;
            existing.LooksLikeVenue = looksLikeVenue;
        }
        else
        {
            Sightings.Insert(0, new PfSighting
            {
                ListingId = listing.Id, FirstSeen = DateTime.Now, Time = DateTime.Now, Recruiter = recruiter, World = world, DataCenter = dc,
                Description = desc, Title = TitleOf(desc), NoneCategory = noneCategory, LooksLikeVenue = looksLikeVenue,
                District = district, Ward = ward, Plot = plot, MatchedVenueId = match?.Id,
            });
            if (Sightings.Count > 300) Sightings.RemoveRange(300, Sightings.Count - 300);
        }
        LastPfRefresh = DateTime.Now;

        if (match == null) return;
        match.LastSeenOpen = Places.Now;
        match.LastSeenInfo = $"{recruiter}: {(desc.Length > 120 ? desc[..120] + "..." : desc)}";
        plugin.Save();
        MaybePing(match);
    }

    public DateTime LastPfRefresh { get; private set; } = DateTime.MinValue;

    /// <summary>A short name for a listing: its first line or first phrase (venue ads usually lead with the venue name).</summary>
    public static string TitleOf(string desc)
    {
        var t = desc.Replace("\r", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? desc;
        foreach (var sep in new[] { " | ", " - ", " ~ ", " :: ", "!", "|", "\u2022" })
        {
            var i = t.IndexOf(sep, StringComparison.Ordinal);
            if (i >= 3) { t = t[..i]; break; }
        }
        t = t.Trim(' ', '*', '-', '~', '|', '=', '+', '.', ':');
        return t.Length > 42 ? t[..42].TrimEnd() + "..." : t;
    }

    /// <summary>Listings seen in Party Finder recently - what's open right now.</summary>
    public IEnumerable<PfSighting> OpenNow(int minutes, bool venuesOnly)
        => Sightings.Where(s => (DateTime.Now - s.Time).TotalMinutes <= minutes && (!venuesOnly || s.LooksLikeVenue || s.MatchedVenueId != null));

    private static bool Matches(Venue v, string desc, string world, string district, int ward, int plot)
    {
        if (v.World.Length > 0 && world.Length > 0 && !v.World.Equals(world, StringComparison.OrdinalIgnoreCase))
        {
            // a venue can advertise from another world on the same DC, so only the address can match then
            if (!(ward > 0 && ward == v.Ward && (plot == 0 || plot == v.Plot) && district == v.District)) return false;
            return desc.Contains(v.World, StringComparison.OrdinalIgnoreCase);
        }
        var name = Places.Norm(v.Name);
        if (name.Length >= 4 && Places.Norm(desc).Contains(name)) return true;
        return ward > 0 && ward == v.Ward && plot > 0 && plot == v.Plot && (district.Length == 0 || district == v.District);
    }

    private void MaybePing(Venue v)
    {
        if (Config.OnlyLikedVenues && Marker(v) != true) return;
        if (Places.Now - v.LastPinged < Config.PingCooldownHours * 3600L) return;
        v.LastPinged = Places.Now;
        plugin.Save();

        if (Config.NotifyMe)
        {
            var se = Config.NotifySound ? " <se.6>" : string.Empty;
            Chat.Send($"/echo [Party Bus] {v.Name} is OPEN! {Places.Address(v)}{se}");
        }
        if (Config.PingParty && Places.InParty())
            plugin.SendLine($"/p {PingTag}{v.Name} is OPEN! {Places.Address(v)} (seen in Party Finder)");
    }

    /// <summary>A crewmate's plugin said a venue is open: mark it here too.</summary>
    private void OnPing(string text, string from)
    {
        var i = text.IndexOf(" is OPEN!", StringComparison.Ordinal);
        if (i <= 0) return;
        var name = text[..i];
        var v = Active.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (v == null) return;
        v.LastSeenOpen = Places.Now;
        v.LastSeenInfo = $"{from} spotted it in Party Finder";
        v.LastPinged = Places.Now;   // don't re-ping the party about the same venue
        plugin.Save();
    }

    // ======================= export / import =======================
    public string Export(bool likedOnly)
    {
        var recs = Active.Where(v => !likedOnly || Marker(v) == true).SelectMany(v => RecordsFor(v, true));
        return "PBLIST1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("~", recs)));
    }

    public int Import(string code)
    {
        code = code.Trim();
        if (!code.StartsWith("PBLIST1:")) return -1;
        var body = Encoding.UTF8.GetString(Convert.FromBase64String(code[8..]));
        var n = 0;
        foreach (var rec in body.Split('~', StringSplitOptions.RemoveEmptyEntries)) n += Apply(rec.Split('^')) ? 1 : 0;
        plugin.Save();
        return n;
    }
}
