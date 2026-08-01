using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using GamingServicesCloud.DTO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.Economy.Model;
using JsonException = System.Text.Json.JsonException;

namespace GamingServicesCloud.Adapters;

public class StoreAdapter
{
    protected readonly ILogger<StoreAdapter> _logger;
    protected readonly EconomyCurrencyAdapter _currencyAdapter;

    public StoreAdapter(
        ILogger<StoreAdapter> logger,
        EconomyCurrencyAdapter currencyAdapter)
    {
        _logger = logger;
        _currencyAdapter = currencyAdapter;
    }

    #region Virtual Purchases

    /// <summary>
    /// Processes a Virtual Purchase via Unity Economy Services.
    /// Costs and rewards are handled automatically by Unity Economy on the backend.
    /// Returns a tuple containing the status code and a descriptive message.
    /// </summary>
    public async Task<(PurchaseStatusCode status, string message)> ProcessVirtualPurchase(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string virtualPurchaseId)
    {
        if (string.IsNullOrWhiteSpace(virtualPurchaseId)) {
            _logger.LogWarning(
                "[StoreAdapter] " +
                "Virtual purchase requested with null or empty ID.");
            return (PurchaseStatusCode.InvalidPurchaseId, "Virtual Purchase ID cannot be null or empty.");
        }

        if (string.IsNullOrEmpty(context?.PlayerId)) {
            _logger.LogWarning(
                "[StoreAdapter] " +
                "Execution context missing valid PlayerId.");
            return (PurchaseStatusCode.PlayerNotFound, "Player context is invalid or missing PlayerId.");
        }

        try {
            var purchaseRequest = new PlayerPurchaseVirtualRequest(virtualPurchaseId);

            ApiResponse<PlayerPurchaseVirtualResponse> purchaseResponse =
                await gameApiClient.EconomyPurchases.MakeVirtualPurchaseAsync(
                    context,
                    context.AccessToken,
                    context.ProjectId,
                    context.PlayerId,
                    purchaseRequest
                );

            if (purchaseResponse?.Data == null) {
                _logger.LogError(
                    "[StoreAdapter] " +
                    $"Received null response from Economy Service for purchase ID '{virtualPurchaseId}'.");
                return (PurchaseStatusCode.EconomyServiceUnavailable, "No response received from Economy service.");
            }

            _logger.LogInformation(
                "[Business][StoreAdapter] " +
                $"Successfully processed Virtual Purchase '{virtualPurchaseId}' for player '{context.PlayerId}'.");

            return (PurchaseStatusCode.Success, "Purchase processed successfully.");
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[StoreAdapter] " +
                $"ApiException processing purchase '{virtualPurchaseId}' for player '{context.PlayerId}'. Status Code: {ex.Response?.StatusCode}");

            var statusCode = MapApiExceptionToPurchaseStatusCode(ex);
            return (statusCode, ex.Message ?? "An error occurred while communicating with Economy Services.");
        }
        catch (TimeoutException ex) {
            _logger.LogError(ex,
                "[StoreAdapter] " +
                $"Timeout while processing Virtual Purchase '{virtualPurchaseId}'.");
            return (PurchaseStatusCode.Timeout, "The operation timed out.");
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "[StoreAdapter] " +
                $"Unexpected exception in ProcessVirtualPurchase for player '{context.PlayerId}'.");
            return (PurchaseStatusCode.ServerError, "An internal server error occurred while processing the purchase.");
        }
    }

    #endregion

    #region Real Money Purchases

    /// <summary>
    /// Processes a Real Money Purchase by validating store receipts (Google Play, Apple App Store, Fake Store)
    /// and redeeming rewards via Unity Economy Services.
    /// Returns a tuple containing the status code and a descriptive message.
    /// </summary>
    public async Task<(PurchaseStatusCode status, string message)> ProcessRealMoneyPurchase(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string productId,
        string receipt,
        double localPrice,
        string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(productId)) {
            _logger.LogWarning(
                "[StoreAdapter] Real money purchase requested with null or empty Product ID.");
            return (PurchaseStatusCode.InvalidPurchaseId, "Product ID cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(receipt)) {
            _logger.LogWarning(
                "[StoreAdapter] Real money purchase requested with null or empty receipt.");
            return (PurchaseStatusCode.InvalidReceipt, "Receipt cannot be null or empty.");
        }

        if (string.IsNullOrEmpty(context?.PlayerId)) {
            _logger.LogWarning(
                "[StoreAdapter] Execution context missing valid PlayerId.");
            return (PurchaseStatusCode.PlayerNotFound, "Player context is invalid or missing PlayerId.");
        }

        try {
            await ProcessStoreReceipt(context, gameApiClient, productId, receipt, localPrice, currencyCode);

            _logger.LogInformation(
                "[Business][StoreAdapter] " +
                $"Successfully processed Real Money Purchase '{productId}' for player '{context.PlayerId}'.");

            return (PurchaseStatusCode.Success, "Real money purchase processed successfully.");
        }
        catch (Newtonsoft.Json.JsonException ex) {
            _logger.LogError(ex,
                "[StoreAdapter] " +
                $"Invalid JSON format in receipt for purchase '{productId}' for player '{context.PlayerId}'.");
            return (PurchaseStatusCode.InvalidReceipt, "Receipt format is invalid or corrupted.");
        }
        catch (JsonException ex) {
            _logger.LogError(ex,
                "[StoreAdapter] " +
                $"Invalid receipt structure for purchase '{productId}' for player '{context.PlayerId}'.");
            return (PurchaseStatusCode.InvalidReceipt, $"Invalid receipt data: {ex.Message}");
        }
        catch (ArgumentException ex) {
            _logger.LogWarning(ex,
                "[StoreAdapter] " +
                $"Invalid argument or unsupported store for real money purchase '{productId}'.");

            if (ex.Message.Contains("Unsupported store", StringComparison.OrdinalIgnoreCase)) {
                return (PurchaseStatusCode.UnsupportedStore, ex.Message);
            }

            return (PurchaseStatusCode.InvalidRequest, ex.Message);
        }
        catch (ApiException ex) {
            _logger.LogError(ex,
                "[StoreAdapter] " +
                $"ApiException processing real money purchase '{productId}' for player '{context.PlayerId}'. Status Code: {ex.Response?.StatusCode}");

            var statusCode = MapApiExceptionToPurchaseStatusCode(ex);
            return (statusCode, ex.Message ?? "An error occurred while validating receipt with Economy Services.");
        }
        catch (TimeoutException ex) {
            _logger.LogError(ex,
                "[StoreAdapter] " +
                $"Timeout while processing Real Money Purchase '{productId}'.");
            return (PurchaseStatusCode.Timeout, "The operation timed out.");
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "[StoreAdapter] " +
                $"Unexpected exception in ProcessRealMoneyPurchase for player '{context.PlayerId}'.");
            return (PurchaseStatusCode.ServerError, "An internal server error occurred while processing the real money purchase.");
        }
    }

    private async Task ProcessStoreReceipt(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string productId,
        string receipt,
        double localCost,
        string localCurrency)
    {
        var receiptData =
            JsonConvert.DeserializeAnonymousType(receipt, new { Store = "", Payload = "" })
            ?? throw new JsonException("Unified receipt is null.");

        if (string.IsNullOrWhiteSpace(receiptData.Store) || string.IsNullOrWhiteSpace(receiptData.Payload)) {
            throw new JsonException("Unified receipt is missing Store or Payload field.");
        }

        var store = receiptData.Store.ToLowerInvariant();

        switch (store) {
            case "fake":
                _logger.LogInformation(
                    "[StoreAdapter] " +
                    $"Processing fake store receipt for testing purposes for player '{context.PlayerId}'.");
                await ApplyPurchaseRewardsFromConfiguration(context, gameApiClient, productId);
                break;
            case "googleplay":
                await RedeemGooglePlayPurchase(
                    context, gameApiClient,
                    productId, receiptData.Payload,
                    localCost, localCurrency);
                break;
            case "appleappstore":
                await RedeemAppleAppStorePurchase(
                    context, gameApiClient,
                    productId, receiptData.Payload,
                    localCost, localCurrency);
                break;
            default:
                throw new ArgumentException($"Unsupported store type: '{store}'.");
        }
    }

    private async Task ApplyPurchaseRewardsFromConfiguration(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string productId)
    {
        var configResponse = await gameApiClient.EconomyConfiguration.GetPlayerConfigurationAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            context.PlayerId!
        );

        var realMoneyPurchase = GetRealMoneyPurchaseFromConfig(configResponse.Data.Results, productId);

        if (realMoneyPurchase?.Rewards == null || realMoneyPurchase.Rewards.Count == 0) {
            _logger.LogWarning(
                "[StoreAdapter] " +
                $"No rewards configured for fake store purchase '{productId}' for player '{context.PlayerId}'.");
            throw new InvalidOperationException($"No rewards configured for fake store purchase '{productId}'.");
        }

        await DistributeConfiguredRewards(
            context, gameApiClient,
            realMoneyPurchase.Rewards,
            configResponse.Data.Results);

        _logger.LogInformation(
            "[Business][StoreAdapter] " +
            $"Successfully applied rewards for fake store purchase '{productId}' for player '{context.PlayerId}'.");
    }

    private async Task DistributeConfiguredRewards(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        List<Reward> rewards,
        List<PlayerConfigurationResponseResultsInner> configResults)
    {
        StringBuilder rewardLog = new StringBuilder();
        List<Task> tasks = new();
        foreach (var reward in rewards) {
            string resourceId = reward.ResourceId;
            long amount = reward.Amount;

            rewardLog.AppendLine($"Process reward: {resourceId} Amount: {amount}");
            var task = _currencyAdapter.IncrementCurrencyAmount(context, gameApiClient, resourceId, amount);
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
    }

    private RealMoneyPurchaseResource? GetRealMoneyPurchaseFromConfig(
        List<PlayerConfigurationResponseResultsInner> results,
        string productId)
    {
        foreach (var result in results) {
            if (result.ActualInstance is RealMoneyPurchaseResource purchase && purchase.Id == productId) {
                return purchase;
            }
        }

        _logger.LogError(
            "[StoreAdapter] " +
            $"Real money purchase '{productId}' not found in player configuration.");
        throw new InvalidOperationException($"Real money purchase '{productId}' not found in configuration.");
    }

    private async Task RedeemGooglePlayPurchase(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string productId,
        string googlePayload,
        double localCost,
        string localCurrency)
    {
        var googleReceipt = JsonConvert.DeserializeAnonymousType(googlePayload,
                                new { json = "", signature = "" })
                            ?? throw new JsonException("Failed to parse Google receipt payload.");

        if (string.IsNullOrWhiteSpace(googleReceipt.json) || string.IsNullOrWhiteSpace(googleReceipt.signature)) {
            throw new JsonException("Google receipt payload is missing json/signature.");
        }

        var googleRequest = new PlayerPurchaseGoogleplaystoreRequest(
            id: productId,
            purchaseData: googleReceipt.json,
            purchaseDataSignature: googleReceipt.signature,
            localCost: (int)(localCost * 100), // Convert to cents
            localCurrency: localCurrency
        );

        var purchaseResult = await gameApiClient.EconomyPurchases.RedeemGooglePlayPurchaseAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            context.PlayerId!,
            googleRequest
        );

        StringBuilder rewardLog = new StringBuilder();
        if (purchaseResult?.Data?.Rewards?.Currency != null) {
            foreach (var currency in purchaseResult.Data.Rewards.Currency) {
                rewardLog.AppendLine($"Granted currency: {currency.Id} {currency.Amount}");
            }
        }

        _logger.LogInformation(
            "[Business][StoreAdapter] " +
            $"Successfully redeemed Google Play purchase '{productId}' for player '{context.PlayerId}'. Rewards:\n{rewardLog}");
    }

    private async Task RedeemAppleAppStorePurchase(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string productId,
        string applePayload,
        double localCost,
        string localCurrency)
    {
        if (string.IsNullOrWhiteSpace(applePayload)) {
            throw new ArgumentException("Apple receipt payload cannot be null or empty.", nameof(applePayload));
        }

        var appleRequest = new PlayerPurchaseAppleappstoreRequest(
            id: productId,
            receipt: applePayload,
            localCost: (int)(localCost * 100), // Convert to cents
            localCurrency: localCurrency
        );

        var purchaseResult = await gameApiClient.EconomyPurchases.RedeemAppleAppStorePurchaseAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            context.PlayerId!,
            appleRequest
        );

        StringBuilder rewardLog = new StringBuilder();
        if (purchaseResult?.Data?.Rewards?.Currency != null) {
            foreach (var currency in purchaseResult.Data.Rewards.Currency) {
                rewardLog.AppendLine($"Granted currency: {currency.Id} {currency.Amount}");
            }
        }

        _logger.LogInformation(
            "[Business][StoreAdapter] " +
            $"Successfully redeemed Apple App Store purchase '{productId}' for player '{context.PlayerId}'. Rewards:\n{rewardLog}");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Maps Unity API HTTP response status codes to domain PurchaseStatusCode enum values.
    /// </summary>
    private PurchaseStatusCode MapApiExceptionToPurchaseStatusCode(ApiException ex)
    {
        return ex.Response?.StatusCode switch
        {
            HttpStatusCode.BadRequest => PurchaseStatusCode.InvalidRequest,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PurchaseStatusCode.Unauthorized,
            HttpStatusCode.NotFound => PurchaseStatusCode.PurchaseUnavailable,
            HttpStatusCode.Conflict => PurchaseStatusCode.DuplicateTransaction,
            HttpStatusCode.UnprocessableContent or HttpStatusCode.UnprocessableEntity => PurchaseStatusCode
                .InsufficientCurrency,
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                PurchaseStatusCode.EconomyServiceUnavailable,
            _ => PurchaseStatusCode.ServerError
        };
    }

    #endregion
}