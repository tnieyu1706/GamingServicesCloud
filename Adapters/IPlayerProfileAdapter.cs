using System;
using System.Threading.Tasks;
using DeployTestCloud;
using GamingServicesCloud.DTO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace GamingServicesCloud.Adapters;

public interface IPlayerProfileAdapter
{
    Task<PlayerProfile> LoadPlayerProfile(IExecutionContext context, IGameApiClient gameApiClient);

    Task<(bool playerExists, PlayerData? playerData)> TryGetPlayerData(
        IExecutionContext context,
        IGameApiClient gameApiClient);

    Task<PlayerProfile> InitializeNewPlayer(IExecutionContext context, IGameApiClient gameApiClient);

    Task<PlayerEconomyData> GetPlayerEconomyData(IExecutionContext context, IGameApiClient gameApiClient);

    Task<PlayerProfile> SyncPlayerProfile(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        PlayerData clientPlayerData,
        PlayerEconomyData? clientEconomyData = null);
}

public abstract class BasePlayerProfileAdapter<T> : IPlayerProfileAdapter
{
    protected readonly ILogger<T> _logger;

    protected BasePlayerProfileAdapter(ILogger<T> logger)
    {
        _logger = logger;
    }

    public async Task<PlayerProfile> LoadPlayerProfile(IExecutionContext context, IGameApiClient gameApiClient)
    {
        var (playerExists, playerData) = await TryGetPlayerData(context, gameApiClient);

        if (!playerExists || playerData is null) {
            return await InitializeNewPlayer(context, gameApiClient);
        }

        var economyData = await GetPlayerEconomyData(context, gameApiClient);

        return new PlayerProfile()
        {
            PlayerData = playerData,
            EconomyData = economyData,
            IsNewPlayer = false
        };
    }

    public async Task<(bool playerExists, PlayerData? playerData)> TryGetPlayerData(
        IExecutionContext context,
        IGameApiClient gameApiClient)
    {
        try {
            var (success, playerDataRaw) = await TryLoadPlayerDataWhenGet(context, gameApiClient);

            if (playerDataRaw == null) return (false, null);

            var playerData = JsonConvert.DeserializeObject<PlayerData>(playerDataRaw.ToString() ?? "{}");
            return (playerData != null, playerData);
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "[ProfileAdapter] " +
                $"Failed to get player data for player:{context.PlayerId}. Error: {ex.Message}");
            return (false, null);
        }
    }

    protected abstract Task<(bool, object?)> TryLoadPlayerDataWhenGet(
        IExecutionContext context,
        IGameApiClient gameApiClient);

    public async Task<PlayerProfile> InitializeNewPlayer(IExecutionContext context, IGameApiClient gameApiClient)
    {
        PlayerData newPlayerData = new PlayerData()
        {
            DisplayName = "New Player",
            Experience = 0,
        };

        PlayerEconomyData newEconomyData;

        try {
            newEconomyData = await InitializeNewPlayerEconomy(context, gameApiClient);

            await SavePlayerDataWhenInit(context, gameApiClient, newPlayerData);

            _logger.LogInformation(
                "[Business][ProfileAdapter] " +
                $"New player initialized: {context.PlayerId}");
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "[ProfileAdapter] " +
                $"Failed to initialize new player for player:{context.PlayerId}. Error: {ex.Message}");
            throw new Exception($"Failed to initialize new player for player:{context.PlayerId}. Error: {ex.Message}");
        }

        return new PlayerProfile()
        {
            PlayerData = newPlayerData,
            EconomyData = newEconomyData,
            IsNewPlayer = true
        };
    }

    protected abstract Task SavePlayerDataWhenInit(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        PlayerData playerData);

    private async Task<PlayerEconomyData> InitializeNewPlayerEconomy(
        IExecutionContext context,
        IGameApiClient gameApiClient)
    {
        await InitializeInventory(context, gameApiClient);
        return await GetPlayerEconomyData(context, gameApiClient);
    }

    protected abstract Task InitializeInventory(IExecutionContext context, IGameApiClient gameApiClient);

    public abstract Task<PlayerEconomyData> GetPlayerEconomyData(
        IExecutionContext context,
        IGameApiClient gameApiClient);

    public virtual async Task<PlayerProfile> SyncPlayerProfile(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        PlayerData clientPlayerData,
        PlayerEconomyData? clientEconomyData = null)
    {
        try {
            var playerId = context.PlayerId ?? throw new InvalidOperationException("PlayerId is null");

            await SavePlayerDataWhenSync(context, gameApiClient, clientPlayerData);

            if (clientEconomyData != null) {
                await SyncPlayerEconomyData(context, gameApiClient, clientEconomyData);
            }

            var latestEconomyData = await GetPlayerEconomyData(context, gameApiClient);

            _logger.LogInformation(
                "[Business][ProfileAdapter] " +
                "Successfully synced player data for player: {PlayerId}", playerId);

            return new PlayerProfile()
            {
                PlayerData = clientPlayerData,
                EconomyData = latestEconomyData,
                IsNewPlayer = false
            };
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "[ProfileAdapter] " +
                "Failed to sync player data for player:{PlayerId}. Error: {Error}", context.PlayerId,
                ex.Message);
            throw new Exception($"Failed to sync player data for player:{context.PlayerId}. Error: {ex.Message}");
        }
    }

    protected abstract Task SavePlayerDataWhenSync(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        PlayerData playerData);

    protected abstract Task SyncPlayerEconomyData(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        PlayerEconomyData clientEconomyData);
}