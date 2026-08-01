using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.Economy.Model;

namespace GamingServicesCloud.Adapters;

/// <summary>
/// Adapter for Unity Economy Inventory Services (Instance-based items).
/// </summary>
public class EconomyInventoryAdapter
{
    private readonly ILogger<EconomyInventoryAdapter> _logger;

    public EconomyInventoryAdapter(ILogger<EconomyInventoryAdapter> logger)
    {
        _logger = logger;
    }

    #region Item Checks & Counting

    /// <summary>
    /// Checks if the player owns at least one instance of the specified item definition ID.
    /// </summary>
    public async Task<bool> HasItem(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string itemId)
    {
        var instances = await GetItems(context, gameApiClient, itemIds: itemId);
        return instances.Count > 0;
    }

    /// <summary>
    /// Checks if item exists; returns instance count if found, otherwise null.
    /// </summary>
    public async Task<int?> TryGetInventoryItemCount(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string itemId)
    {
        if (!await HasItem(context, gameApiClient, itemId)) {
            return null;
        }

        return await GetItemCount(context, gameApiClient, itemId);
    }

    /// <summary>
    /// Gets total owned instance count for a specific item definition ID.
    /// </summary>
    public async Task<int> GetItemCount(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string itemId)
    {
        var instances = await GetItems(context, gameApiClient, itemIds: itemId);
        return instances.Count;
    }

    /// <summary>
    /// Gets owned instance counts as a dictionary map (Key: ItemId, Value: Count).
    /// </summary>
    public async Task<Dictionary<string, int>> GetItemsCountMap(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        params string[]? itemIds)
    {
        try {
            var items = await GetItems(context, gameApiClient, itemIds: itemIds);
            return items
                .Where(i => !string.IsNullOrEmpty(i.InventoryItemId))
                .GroupBy(i => i.InventoryItemId)
                .ToDictionary(g => g.Key, g => g.Count());
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "[InventoryAdapter] " +
                "Failed to get item count map for player '{PlayerId}'.", context.PlayerId);
            throw;
        }
    }

    /// <summary>
    /// Gets owned instance counts as a list of tuples.
    /// </summary>
    public async Task<List<(string itemId, int count)>> GetItemCountList(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        params string[]? itemIds)
    {
        try {
            var items = await GetItems(context, gameApiClient, itemIds: itemIds);
            return items
                .Where(i => !string.IsNullOrEmpty(i.InventoryItemId))
                .GroupBy(i => i.InventoryItemId)
                .Select(g => (itemId: g.Key, count: g.Count()))
                .ToList();
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "[InventoryAdapter] " +
                "Failed to get item count list for player '{PlayerId}'.", context.PlayerId);
            throw;
        }
    }

    #endregion

    #region CRUD Operations

    /// <summary>
    /// Adds a single new item instance for the player.
    /// </summary>
    public async Task<InventoryResponse> AddItem(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string itemId,
        Dictionary<string, object>? instanceData = null)
    {
        try {
            var playerId = context.PlayerId ?? throw new InvalidOperationException("PlayerId is null");
            var request = new AddInventoryRequest(itemId, instanceData: instanceData);

            var response = await gameApiClient.EconomyInventory.AddInventoryItemAsync(
                context, context.AccessToken, context.ProjectId, playerId, request);

            _logger.LogInformation(
                "[InventoryAdapter] " +
                "Added item '{ItemId}' for player '{PlayerId}'. Instance ID: {InstanceId}",
                itemId, playerId, response.Data?.PlayersInventoryItemId);

            return response.Data;
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[InventoryAdapter] " +
                "Failed to add item '{ItemId}' for player '{PlayerId}'.", itemId, context.PlayerId);
            throw;
        }
    }

    /// <summary>
    /// Adds multiple instances of an item definition.
    /// </summary>
    public async Task<List<InventoryResponse>> AddItems(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string itemId,
        int count,
        Dictionary<string, object>? instanceData = null)
    {
        if (count <= 0) return new List<InventoryResponse>();

        var tasks = new List<Task<InventoryResponse>>();
        for (int i = 0; i < count; i++) {
            tasks.Add(AddItem(context, gameApiClient, itemId, instanceData));
        }

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// Updates custom instance data using its unique PlayersInventoryItemId.
    /// </summary>
    public async Task<InventoryResponse> UpdateItemData(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string playersInventoryItemId,
        Dictionary<string, object> instanceData)
    {
        try {
            var playerId = context.PlayerId ?? throw new InvalidOperationException("PlayerId is null");
            var request = new InventoryRequestUpdate(instanceData: instanceData);

            var response = await gameApiClient.EconomyInventory.UpdateInventoryItemAsync(
                context, context.AccessToken, context.ProjectId, playerId, playersInventoryItemId, request);

            _logger.LogInformation(
                "[InventoryAdapter] " +
                "Updated instance data for '{PlayersInventoryItemId}'.", playersInventoryItemId);
            return response.Data;
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[InventoryAdapter] " +
                "Failed to update item '{PlayersInventoryItemId}'.", playersInventoryItemId);
            throw;
        }
    }

    /// <summary>
    /// Deletes a specific inventory item instance.
    /// </summary>
    public async Task DeleteItem(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string playersInventoryItemId)
    {
        try {
            var playerId = context.PlayerId ?? throw new InvalidOperationException("PlayerId is null");

            await gameApiClient.EconomyInventory.DeleteInventoryItemAsync(
                context, context.AccessToken, context.ProjectId, playerId, playersInventoryItemId);

            _logger.LogInformation(
                "[InventoryAdapter] " +
                "Deleted item instance '{PlayersInventoryItemId}' for player '{PlayerId}'.",
                playersInventoryItemId, playerId);
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[InventoryAdapter] " +
                "Failed to delete item '{PlayersInventoryItemId}'.", playersInventoryItemId);
            throw;
        }
    }

    /// <summary>
    /// Deletes up to countToDelete instances of an item definition.
    /// </summary>
    public async Task<int> DeleteItems(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string itemId,
        int countToDelete)
    {
        if (countToDelete <= 0) return 0;

        var instances = await GetItems(context, gameApiClient, itemIds: itemId);
        var targetInstances = instances.Take(countToDelete).ToList();

        var deleteTasks = targetInstances
            .Where(i => !string.IsNullOrEmpty(i.PlayersInventoryItemId))
            .Select(i => DeleteItem(context, gameApiClient, i.PlayersInventoryItemId));

        await Task.WhenAll(deleteTasks);

        _logger.LogInformation(
            "[InventoryAdapter] " +
            "Deleted {Count} instances of item '{ItemId}' for player '{PlayerId}'.",
            targetInstances.Count, itemId, context.PlayerId);

        return targetInstances.Count;
    }

    #endregion

    #region Fetch & Data Parsing Helpers

    /// <summary>
    /// Gets raw inventory item instances owned by the player, optionally filtered.
    /// </summary>
    public async Task<List<InventoryResponse>> GetItems(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        int? limit = null,
        params string[]? itemIds)
    {
        try {
            List<string>? ids = itemIds?.Length > 0 ? itemIds.ToList() : null;

            var response = await gameApiClient.EconomyInventory.GetPlayerInventoryAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"),
                inventoryItemIds: ids,
                limit: limit
            );

            return response.Data?.Results?.ToList() ?? new List<InventoryResponse>();
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[InventoryAdapter] " +
                "Failed to fetch items for player '{PlayerId}'.", context.PlayerId);
            throw;
        }
    }

    /// <summary>
    /// Extracts custom property from an instance's InstanceData.
    /// </summary>
    public T? GetInstanceCustomData<T>(InventoryResponse itemInstance, string key)
    {
        if (itemInstance?.InstanceData == null) return default;

        try {
            var jObject = itemInstance.InstanceData as JObject
                          ?? JObject.Parse(itemInstance.InstanceData.ToString() ?? "{}");

            var token = jObject[key];
            return token != null ? token.ToObject<T>() : default;
        }
        catch (Exception ex) {
            _logger.LogWarning(
                "[InventoryAdapter] " +
                "Failed to parse instance custom data for '{ItemId}'. Key: '{Key}', Error: {Message}",
                itemInstance.InventoryItemId, key, ex.Message);
            return default;
        }
    }

    /// <summary>
    /// Deserializes entire InstanceData into a strongly-typed class or struct.
    /// </summary>
    public T? GetParsedInstanceData<T>(InventoryResponse itemInstance)
    {
        if (itemInstance?.InstanceData == null) return default;

        try {
            string json = itemInstance.InstanceData.ToString() ?? "{}";
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "[InventoryAdapter] " +
                "Failed to deserialize instance data for item '{InstanceId}'.",
                itemInstance.PlayersInventoryItemId);
            return default;
        }
    }

    #endregion
}