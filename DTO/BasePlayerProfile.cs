using Newtonsoft.Json;

namespace GamingServicesCloud.DTO;

public class BasePlayerProfile<TPlayerData> where TPlayerData : class, new()
{
    [JsonProperty("playerData")] public TPlayerData PlayerData { get; set; } = new();
    [JsonProperty("economyData")] public PlayerEconomyData EconomyData { get; set; } = new();
    [JsonProperty("isNewPlayer")] public bool IsNewPlayer { get; set; }
}