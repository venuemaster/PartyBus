using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace PartyBus;

[Serializable]
public class Rating
{
    public bool Liked { get; set; }
    public bool Cleared { get; set; }   // rating removed
    public long Time { get; set; }
}

[Serializable]
public class Venue
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = string.Empty;
    public string DataCenter { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public int Ward { get; set; }
    public int Plot { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string AddedBy { get; set; } = string.Empty;
    public long Updated { get; set; }        // when the venue details last changed (unix seconds, set by whoever changed it)
    public long ReceivedAt { get; set; }     // when this copy last changed on THIS computer (for syncing)
    public bool Deleted { get; set; }
    public Dictionary<string, Rating> Ratings { get; set; } = new();
    public long LastSeenOpen { get; set; }
    public string LastSeenInfo { get; set; } = string.Empty;
    public long LastPinged { get; set; }
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public List<Venue> Venues { get; set; } = new();

    // crew & sync
    public List<string> Crew { get; set; } = new();
    public bool AcceptFromAnyoneInParty { get; set; } = false;
    public bool AutoSync { get; set; } = true;
    public Dictionary<string, long> LastSyncedWith { get; set; } = new();
    public float SecondsBetweenMessages { get; set; } = 3.0f;

    // party finder
    public bool ScanPartyFinder { get; set; } = true;
    public bool NotifyMe { get; set; } = true;
    public bool NotifySound { get; set; } = true;
    public bool PingParty { get; set; } = true;
    public bool OnlyLikedVenues { get; set; } = true;
    public int PingCooldownHours { get; set; } = 3;
    public int OpenForHours { get; set; } = 3;   // how long "OPEN" shows after a listing is seen
    public int OpenNowMinutes { get; set; } = 30; // Open Now tab: listings seen within this many minutes
    public bool OpenNowVenuesOnly { get; set; } = true;
}
