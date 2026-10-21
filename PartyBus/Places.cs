using System;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace PartyBus;

/// <summary>Housing locations, world/data center names and address text.</summary>
public static class Places
{
    public static readonly string[] Districts = { "Mist", "The Lavender Beds", "The Goblet", "Shirogane", "Empyreum" };

    /// <summary>Turns "LB", "lavender", "The Lavender Beds", "empy", "ishgard"... into one of <see cref="Districts"/>.</summary>
    public static string NormalizeDistrict(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("mist")) return "Mist";
        if (t.Contains("lavender") || t.Contains("beds") || Regex.IsMatch(t, @"\blb\b")) return "The Lavender Beds";
        if (t.Contains("goblet") || Regex.IsMatch(t, @"\bgob\b")) return "The Goblet";
        if (t.Contains("shirogane") || Regex.IsMatch(t, @"\bshiro\b")) return "Shirogane";
        if (t.Contains("empyre") || Regex.IsMatch(t, @"\bempy\b") || t.Contains("ishgard")) return "Empyreum";
        return string.Empty;
    }

    public static string WorldName(uint id)
    {
        try { return Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>().GetRowOrDefault(id)?.Name.ExtractText() ?? string.Empty; }
        catch { return string.Empty; }
    }

    public static string DataCenterOf(uint worldId)
    {
        try { return Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>().GetRowOrDefault(worldId)?.DataCenter.Value.Name.ExtractText() ?? string.Empty; }
        catch { return string.Empty; }
    }

    public static string DataCenterOf(string worldName)
    {
        try
        {
            foreach (var w in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>())
                if (w.Name.ExtractText().Equals(worldName, StringComparison.OrdinalIgnoreCase))
                    return w.DataCenter.Value.Name.ExtractText();
        }
        catch { }
        return string.Empty;
    }

    private static string DistrictOfTerritory(uint territory)
    {
        try { return NormalizeDistrict(Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(territory)?.PlaceName.Value.Name.ExtractText() ?? string.Empty); }
        catch { return string.Empty; }
    }

    /// <summary>Where you're standing: inside a house, or outside on a plot in a housing ward.</summary>
    public static unsafe bool TryHere(out string world, out string dc, out string district, out int ward, out int plot)
    {
        world = dc = district = string.Empty; ward = plot = 0;
        var hm = HousingManager.Instance();
        if (hm == null) return false;
        uint worldId, territory;
        if (hm->IsInside())
        {
            var id = hm->GetCurrentIndoorHouseId().Id;
            if (id == 0 || id == ulong.MaxValue) return false;
            var unit = id & 0xFF;
            ward = (int)((id >> 16) & 0x3F) + 1;
            plot = (unit & 0x80) != 0 ? 0 : (int)(unit & 0x7F) + 1;
            territory = (uint)((id >> 32) & 0xFFFF);
            worldId = (uint)((id >> 48) & 0xFFFF);
        }
        else if (hm->IsOutside())
        {
            ward = hm->GetCurrentWard() + 1;
            var p = hm->GetCurrentPlot();
            plot = p >= 0 ? p + 1 : 0;
            territory = Plugin.ClientState.TerritoryType;
            var me = Plugin.ObjectTable.LocalPlayer;
            if (me == null) return false;
            worldId = me.CurrentWorld.RowId;
        }
        else return false;

        world = WorldName(worldId);
        dc = DataCenterOf(worldId);
        district = DistrictOfTerritory(territory);
        return ward > 0;
    }

    public static string Address(Venue v)
    {
        var plot = v.Plot > 0 ? $" Plot {v.Plot}" : string.Empty;
        var where = $"{(v.District.Length > 0 ? v.District : "?")} Ward {v.Ward}{plot}";
        return $"{where}, {v.World} ({v.DataCenter})";
    }

    private static readonly Regex WardRx = new(@"\b(?:w|ward)\s*\.?\s*#?\s*(\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlotRx = new(@"\b(?:p|plot)\s*\.?\s*#?\s*(\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Pulls "Mist W12 P30" style addresses out of a Party Finder description.</summary>
    public static (string District, int Ward, int Plot) ParseAddress(string text)
    {
        var w = WardRx.Match(text);
        var p = PlotRx.Match(text);
        return (NormalizeDistrict(text),
                w.Success ? int.Parse(w.Groups[1].Value) : 0,
                p.Success ? int.Parse(p.Groups[1].Value) : 0);
    }

    public static unsafe bool InParty()
    {
        try { return Plugin.PartyList.Length > 1 || InfoProxyCrossRealm.IsCrossRealmParty(); }
        catch { return Plugin.PartyList.Length > 1; }
    }

    public static string Norm(string s)
    {
        var chars = new System.Text.StringBuilder(s.Length);
        foreach (var c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) chars.Append(c);
        return chars.ToString();
    }

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
