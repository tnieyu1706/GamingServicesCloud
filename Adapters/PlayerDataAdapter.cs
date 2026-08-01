using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace GamingServicesCloud.Adapters;

public class PlayerDataAdapter
{
    readonly ILogger<PlayerDataAdapter> _logger;

    public PlayerDataAdapter(ILogger<PlayerDataAdapter> logger)
    {
        _logger = logger;
    }

    public async Task SaveKey(IExecutionContext context, IGameApiClient gameApiClient, string key, object value)
    {
        try {
            await gameApiClient.CloudSaveData.SetItemAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"),
                new SetItemBody(key, value)
            );
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[PlayerDataAdapter] " +
                "Failed to save data. Error: {Error}", ex.Message);
            throw new Exception($"Failed to save data for playerId {context.PlayerId}. Error: {ex.Message}");
        }
    }

    public async Task<(bool, object?)> TryLoadKey(IExecutionContext context, IGameApiClient gameApiClient, string key)
    {
        try {
            var result = await LoadKey(context, gameApiClient, key);
            if (result == null)
                return (false, null);

            return (true, result);
        }
        catch (Exception ex) {
            return (false, null);
        }
    }

    public async Task<object?> LoadKey(IExecutionContext context, IGameApiClient gameApiClient, string key)
    {
        try {
            var result =
                await gameApiClient.CloudSaveData.GetItemsAsync(
                    context,
                    context.AccessToken,
                    context.ProjectId,
                    context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"),
                    [key]
                );

            return result.Data.Results
                .FirstOrDefault()?.Value;
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[PlayerDataAdapter] " +
                "Failed to get data. Error: {Error}", ex.Message);
            throw new Exception($"Failed to get data for playerId {context.PlayerId}. Error: {ex.Message}");
        }
    }
}