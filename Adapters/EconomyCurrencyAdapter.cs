using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.Economy.Model;

namespace GamingServicesCloud.Adapters;

public class EconomyCurrencyAdapter
{
    readonly ILogger<EconomyCurrencyAdapter> _logger;

    public EconomyCurrencyAdapter(ILogger<EconomyCurrencyAdapter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets a dictionary of all currencies and their current amounts for the player.
    /// </summary>
    public async Task<Dictionary<string, int>> GetCurrenciesAmountMap(IExecutionContext context,
        IGameApiClient gameApiClient)
    {
        var currenciesList = await GetCurrenciesAmountList(context, gameApiClient);
        return currenciesList
            .ToDictionary(c => c.id, c => c.amount);
    }

    /// <summary>
    /// Gets a list of all currencies and their current amounts for the player.
    /// </summary>
    public async Task<List<(string id, int amount)>> GetCurrenciesAmountList(
        IExecutionContext context,
        IGameApiClient gameApiClient)
    {
        var playerCurrenciesResponse = await gameApiClient.EconomyCurrencies.GetPlayerCurrenciesAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            context.PlayerId ?? throw new InvalidOperationException("PlayerId is null")
        );

        return playerCurrenciesResponse.Data.Results
            .Where(c => c.CurrencyId != null)
            .Select(c => (c.CurrencyId!, (int)c.Balance))
            .ToList();
    }

    /// <summary>
    /// Gets the current amount of a specific currency for the player.
    /// </summary>
    public async Task<int> GetCurrencyAmount(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string currencyId,
        bool returnZeroIfNotFound = true)
    {
        try {
            var playerId = context.PlayerId ?? throw new InvalidOperationException("PlayerId is null");

            var playerCurrenciesData = await gameApiClient.EconomyCurrencies.GetPlayerCurrenciesAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                playerId
            );

            CurrencyBalanceResponse? targetCurrency =
                playerCurrenciesData.Data.Results.FirstOrDefault(c => c.CurrencyId == currencyId);

            if (targetCurrency == null) {
                if (returnZeroIfNotFound) {
                    _logger.LogInformation(
                        "[CurrencyAdapter] " +
                        "Currency '{CurrencyId}' not found for player '{PlayerId}'. Defaulting to 0.",
                        currencyId,
                        playerId);
                    return 0;
                }

                _logger.LogError(
                    "[CurrencyAdapter] " +
                    "Failed to get currency amount. Error: Currency '{CurrencyId}' not found.",
                    currencyId);
                throw new Exception($"Currency '{currencyId}' not found for player '{playerId}'.");
            }

            return (int)targetCurrency.Balance;
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[CurrencyAdapter] " +
                "Failed to get currency amount for '{CurrencyId}'. Error: {Error}", currencyId,
                ex.Message);
            throw new Exception(
                $"Failed to get currency '{currencyId}' for player {context.PlayerId}. Error: {ex.Message}");
        }
    }


    /// <summary>
    /// Sets the absolute balance of a specific currency for the player.
    /// </summary>
    public async Task<CurrencyBalanceResponse> SetCurrencyAmount(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string currencyId,
        long amount)
    {
        try {
            var playerId = context.PlayerId ?? throw new InvalidOperationException("PlayerId is null");
            var setBalanceRequest = new CurrencyBalanceRequest(currencyId, amount);

            var response = await gameApiClient.EconomyCurrencies.SetPlayerCurrencyBalanceAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                playerId,
                currencyId,
                setBalanceRequest
            );

            _logger.LogInformation(
                "[CurrencyAdapter] " +
                "Successfully set currency '{CurrencyId}' to {Amount} for player '{PlayerId}'.",
                currencyId, amount, playerId);
            return response.Data;
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[CurrencyAdapter] " +
                "Failed to set currency '{CurrencyId}' to {Amount} for player '{PlayerId}'. Error: {Error}", currencyId,
                amount, context.PlayerId, ex.Message);
            throw new Exception(
                $"Failed to set currency '{currencyId}' for player {context.PlayerId}. Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Increments the player's currency balance by a specified amount.
    /// </summary>
    public async Task<CurrencyBalanceResponse> IncrementCurrencyAmount(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string currencyId,
        long amountToIncrement)
    {
        try {
            var playerId = context.PlayerId ?? throw new InvalidOperationException("PlayerId is null");
            var incrementRequest = new CurrencyModifyBalanceRequest(currencyId, amountToIncrement);

            var response = await gameApiClient.EconomyCurrencies.IncrementPlayerCurrencyBalanceAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                playerId,
                currencyId,
                incrementRequest
            );

            _logger.LogInformation(
                "[CurrencyAdapter] " +
                "Incremented currency '{CurrencyId}' by {Amount} for player '{PlayerId}'.",
                currencyId, amountToIncrement, playerId);
            return response.Data;
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[CurrencyAdapter] " +
                "Failed to increment currency '{CurrencyId}' for player '{PlayerId}'. Error: {Error}",
                currencyId, context.PlayerId, ex.Message);
            throw new Exception(
                $"Failed to increment currency '{currencyId}' for player {context.PlayerId}. Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Decrements the player's currency balance by a specified amount.
    /// </summary>
    public async Task<CurrencyBalanceResponse> DecrementCurrencyAmount(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string currencyId,
        long amountToDecrement)
    {
        try {
            var playerId = context.PlayerId ?? throw new InvalidOperationException("PlayerId is null");
            var decrementRequest = new CurrencyModifyBalanceRequest(currencyId, amountToDecrement);

            var response = await gameApiClient.EconomyCurrencies.DecrementPlayerCurrencyBalanceAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                playerId,
                currencyId,
                decrementRequest
            );

            _logger.LogInformation(
                "[CurrencyAdapter] " +
                "Decremented currency '{CurrencyId}' by {Amount} for player '{PlayerId}'.",
                currencyId, amountToDecrement, playerId);
            return response.Data;
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[CurrencyAdapter] " +
                "Failed to decrement currency '{CurrencyId}' for player '{PlayerId}'. Error: {Error}",
                currencyId, context.PlayerId, ex.Message);
            throw new Exception(
                $"Failed to decrement currency '{currencyId}' for player {context.PlayerId}. Error: {ex.Message}");
        }
    }
}