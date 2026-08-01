using System;
using System.Threading.Tasks;
using GamingServicesCloud.Adapters;
using GamingServicesCloud.DTO;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace DeployTestCloud;

public class PlayerProfileAdapter : BasePlayerProfileAdapter<PlayerProfileAdapter>
{
    protected const string PlayerDataKey = "PlayerData";

    protected readonly ILogger<PlayerProfileAdapter> _logger;
    protected readonly PlayerDataAdapter _playerDataAdapter;
    protected readonly EconomyCurrencyAdapter _currencyAdapter;
    protected readonly EconomyInventoryAdapter _inventoryAdapter;

    public PlayerProfileAdapter(
        ILogger<PlayerProfileAdapter> logger,
        PlayerDataAdapter playerDataAdapter,
        EconomyCurrencyAdapter currencyAdapter,
        EconomyInventoryAdapter inventoryAdapter)
        : base(logger)
    {
        _logger = logger;
        _playerDataAdapter = playerDataAdapter;
        this._currencyAdapter = currencyAdapter;
        _inventoryAdapter = inventoryAdapter;
    }

    protected override Task<(bool, object?)> TryLoadPlayerDataWhenGet(IExecutionContext context,
        IGameApiClient gameApiClient)
    {
        return _playerDataAdapter.TryLoadKey(context, gameApiClient, PlayerDataKey);
    }

    protected override async Task SavePlayerDataWhenInit(IExecutionContext context, IGameApiClient gameApiClient,
        PlayerData playerData)
    {
        await _playerDataAdapter.SaveKey(context, gameApiClient, PlayerDataKey, playerData);
    }

    protected override Task InitializeInventory(IExecutionContext context, IGameApiClient gameApiClient)
    {
        return Task.CompletedTask;
    }

    public override async Task<PlayerEconomyData> GetPlayerEconomyData(IExecutionContext context,
        IGameApiClient gameApiClient)
    {
        try {
            var economyData = new PlayerEconomyData();

            economyData.Currencies = await _currencyAdapter.GetCurrenciesAmountMap(context, gameApiClient);
            economyData.ItemInventory = await _inventoryAdapter.GetItemsCountMap(context, gameApiClient);

            return economyData;
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "[ProfileAdapter] " +
                $"Failed to get economy data for player:{context.PlayerId}. Error: {ex.Message}");
            throw new Exception($"Failed to get economy data for player:{context.PlayerId}. Error: {ex.Message}");
        }
    }

    protected override async Task SavePlayerDataWhenSync(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        PlayerData playerData)
    {
        await _playerDataAdapter.SaveKey(context, gameApiClient, PlayerDataKey, playerData);
    }

    protected override async Task SyncPlayerEconomyData(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        PlayerEconomyData clientEconomyData)
    {
        // 1. Sync Currencies
        if (clientEconomyData == null) {
            _logger.LogWarning(
                "[ProfileAdapter] " +
                "ClientEconomyData is null");
            return;
        }

        if (clientEconomyData.Currencies != null) {
            foreach (var currency in clientEconomyData.Currencies) {
                try {
                    await _currencyAdapter.SetCurrencyAmount(
                        context,
                        gameApiClient,
                        currency.Key,
                        currency.Value
                    );
                }
                catch (Exception ex) {
                    _logger.LogError(ex,
                        "[ProfileAdapter] " +
                        "Failed to sync currency '{CurrencyId}' for player {PlayerId}.",
                        currency.Key, context.PlayerId);
                }
            }
        }

        // 2. Sync Inventory Items (Instance Count Reconciliation)
        if (clientEconomyData.ItemInventory != null) {
            try {
                // Retrieve server-side count map once to minimize network calls
                var serverCountMap = await _inventoryAdapter.GetItemsCountMap(context, gameApiClient);

                foreach (var item in clientEconomyData.ItemInventory) {
                    string itemId = item.Key;
                    int targetCount = item.Value;
                    serverCountMap.TryGetValue(itemId, out int currentCount);

                    int diff = targetCount - currentCount;

                    if (diff > 0) {
                        // Client has more items -> Add missing instances to backend
                        await _inventoryAdapter.AddItems(context, gameApiClient, itemId, diff);
                        _logger.LogInformation(
                            "[ProfileAdapter] " +
                            "Synced item '{ItemId}': Added {Count} instances for player '{PlayerId}'.",
                            itemId, diff, context.PlayerId);
                    }
                    else if (diff < 0) {
                        // Client has fewer items -> Delete extra instances from backend
                        int deleteCount = Math.Abs(diff);
                        await _inventoryAdapter.DeleteItems(context, gameApiClient, itemId, deleteCount);
                        _logger.LogInformation(
                            "[ProfileAdapter] " +
                            "Synced item '{ItemId}': Deleted {Count} instances for player '{PlayerId}'.",
                            itemId, deleteCount, context.PlayerId);
                    }
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to sync inventory items for player {PlayerId}.", context.PlayerId);
            }
        }
    }
}