using System.Collections.Generic;
using Newtonsoft.Json;

namespace GamingServicesCloud.DTO;

public class PlayerEconomyData
{
    [JsonProperty("currencies")]
    public Dictionary<string, int> Currencies { get; set; } = new Dictionary<string, int>();

    [JsonProperty("itemInventory")]
    public Dictionary<string, int> ItemInventory { get; set; } = new Dictionary<string, int>();
}