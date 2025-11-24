using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.Economy.Model;

namespace UGSTutorialCloud;

public class StoreService
{
    private const string k_HealthPotionPurchaseId = "HEALTH_POTION_VIRTUAL_PURCHASE";

    private readonly ILogger<StoreService> m_Logger;
    private PlayerEconomyService m_PlayerEconomyService;

    public StoreService(ILogger<StoreService> logger, PlayerEconomyService playerEconomy)
    {
        m_Logger = logger;
        m_PlayerEconomyService = playerEconomy;
    }

    /// <summary>
    /// Handles a health potion purchase using the virtual purchase system.
    /// Processes the purchase and adds a potion to the player's inventory.
    /// </summary>
    [CloudCodeFunction("VirtualPurchaseHealthPotion")]
    public async Task<PlayerEconomyData> VirtualPurchaseHealthPotion(IExecutionContext context, IGameApiClient gameApiClient)
    {  
        try
        {
            m_Logger.LogInformation($"VirtualPurchaseHealthPotion called for player {context.PlayerId}");

            await ProcessVirtualPurchase(context, gameApiClient, k_HealthPotionPurchaseId);
            await m_PlayerEconomyService.CleanUpNullOrZeroAmountItems(context, gameApiClient, PlayerEconomyService.k_HealthPotionKey);
            await m_PlayerEconomyService.AddOrUpdateInventoryItemAmount(context, gameApiClient, PlayerEconomyService.k_HealthPotionKey, 1);

            return await m_PlayerEconomyService.GetPlayerEconomyData(context, gameApiClient);
        }

        catch (ApiException ex)
        {
            m_Logger.LogError(ex, $"Failed to purchase potion: {context.PlayerId}");
            throw new Exception($"Failed to purchase potion: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Processes a virtual purchase with the Economy service.
    /// </summary>
    /// <param name="virtualPurchaseID">ID of the virtual purchase to process.</param>

    private async Task ProcessVirtualPurchase(IExecutionContext context, IGameApiClient gameApiClient, string virtualPurchaseID)
    {
        try
        {
            var purchaseRequest = new PlayerPurchaseVirtualRequest(virtualPurchaseID);

            var purchaseResponse = await gameApiClient.EconomyPurchases.MakeVirtualPurchaseAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"),
                purchaseRequest
                );

            if (purchaseResponse == null || purchaseResponse.Data == null || purchaseResponse.Data.Rewards == null)
            {
                m_Logger.LogWarning($"Invalid purchase response structure for {virtualPurchaseID}");
                return;
            }

            List<InventoryExchangeItem> rewardItems = purchaseResponse.Data.Rewards.Inventory;

            // Note:
            // + You can log the entire purchase with: purchaseResponse.Data.ToJson();
            // + The "Amount" parameter logged here is the number of items added, NOT "amount" custom data--custom data will be empty
            m_Logger.LogInformation($"Player {context.PlayerId} virtually purchased {virtualPurchaseID}. Reward : " + JsonConvert.SerializeObject(rewardItems));

        }

        catch (ApiException ex)
        {
            m_Logger.LogError(ex, $"Failed to process potion purchase: {context.PlayerId}");
            throw; // Rethrow to be handled by the calling method
        }
    }

    private async Task AddCustomDataToReward(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        List<InventoryExchangeItem> rewardItems,
        string itemKey,
        string virtualPurchaseId)
    {
        try
        {
            // Get the custom data from the virtual purchase
            var purchaseCustomData = await GetVirtualPurchaseCustomData(context, gameApiClient, virtualPurchaseId);

            if (purchaseCustomData == null)
            {
                m_Logger.LogWarning($"No custom data found for virtual purchase {virtualPurchaseId}");
                return;
            }

            // Find the specific reward item we want to update
            foreach (var item in rewardItems)
            {
                if (item.Id == itemKey &&
                    item.PlayersInventoryItemIds != null &&
                    item.PlayersInventoryItemIds.Count > 0)
                {
                    // Convert purchase custom data to Dictionary
                    Dictionary<string, object> customData;
                    if (purchaseCustomData is Dictionary<string, object> dataDict)
                    {
                        customData = dataDict;
                    }
                    else
                    {
                        // Convert from JObject or other format if needed
                        string json = JsonConvert.SerializeObject(purchaseCustomData);
                        customData = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)!;
                    }

                    if (customData == null)
                    {
                        m_Logger.LogWarning($"Failed to convert purchase custom data to Dictionary");
                        return;
                    }

                    // Ensure we have an amount field (default to 1 if not specified)
                    if (!customData.ContainsKey("amount"))
                    {
                        customData["amount"] = 1;
                    }

                    // Update each rewarded item with the custom data
                    foreach (var playerItemId in item.PlayersInventoryItemIds)
                    {
                        var updateRequest = new InventoryRequestUpdate(instanceData: customData);

                        await gameApiClient.EconomyInventory.UpdateInventoryItemAsync(
                            context,
                            context.AccessToken,
                            context.ProjectId,
                            context.PlayerId!,
                            playerItemId,
                            updateRequest);

                        m_Logger.LogInformation($"Updated inventory item {playerItemId} with custom data from purchase {virtualPurchaseId}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, $"Failed to add custom data to reward for {virtualPurchaseId}");
            // Don't throw - we don't want to fail the purchase
        }
    }

    public async Task<object?> GetVirtualPurchaseCustomData(IExecutionContext context, IGameApiClient gameApiClient, string purchaseId)
    {
        var configResponse = await gameApiClient.EconomyConfiguration.GetPlayerConfigurationAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            context.PlayerId!
        );

        if (configResponse?.Data?.Results == null)
        {
            m_Logger.LogWarning($"Failed to retrieve config or empty response for {purchaseId}");
            return null;
        }

        foreach (var entry in configResponse.Data.Results)
        {
            try
            {
                var purchase = entry.GetVirtualPurchaseResource();
                if (purchase != null && purchase.Id == purchaseId)
                {
                    m_Logger.LogInformation($"Found custom data for {purchaseId}");
                    return purchase.CustomData;
                }
            }
            catch (Exception)
            {
                // Skip entries that aren't virtual purchases
            }
        }
        throw new Exception($"Virtual purchase {purchaseId} not found or has no custom data.");
    }

    /// <summary>
    /// Handles a real money purchase from any supported store.
    /// Validates the receipt and grants rewards to the player.
    /// For fake store (in-editor testing), skips validation and manually grants rewards.
    /// </summary>
    /// <param name="productId">ID of the product being purchased.</param>
    /// <param name="receipt">Receipt data from the store.</param>
    /// <param name="transactionId">Transaction ID for Google Play purchases.</param>
    /// <returns> Updated player economy data after the purchase.</returns>
    [CloudCodeFunction("ProcessRealMoneyPurchase")]
    public async Task<PlayerEconomyData> ProcessRealMoneyPurchase(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string productId,
        string receipt,
        double localPrice,
        string currencyCode)
    {
        try
        {
            await ProcessStoreReceipt(context, gameApiClient, productId, receipt, localPrice, currencyCode);
           
            return await m_PlayerEconomyService.GetPlayerEconomyData(context, gameApiClient)
                ?? throw new InvalidOperationException("Failed to get player economy data");
        }
        catch (ApiException ex)
        {
            m_Logger.LogWarning($"Economy API error processing purchase for product {productId}: {ex.Message}");
            throw;
        }
        catch (Exception e)
        {
            m_Logger.LogError($"Unexpected error processing purchase for product {productId}: {e.Message}");
            throw;
        }
    }

    /// <summary>
    /// Validates and redeems a receipt from various stores by calling the appropriate store-specific method.
    /// </summary>
    /// <param name="productId">ID of the product being purchased.</param>
    /// <param name="receipt">Receipt data from the store.</param>
    /// <param name="transactionId">Transaction ID for Google Play purchases.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessStoreReceipt(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string productId,
        string receipt,
        double localCost,
        string localCurrency)
    {
        var receiptData = JsonConvert.DeserializeAnonymousType(receipt, new { Store = "", Payload = "" })
            ?? throw new JsonException("Unified receipt is null.");

        if (string.IsNullOrWhiteSpace(receiptData.Store) || string.IsNullOrWhiteSpace(receiptData.Payload))
            throw new JsonException("Unified receipt missing Store/Payload.");

        var store = receiptData.Store.ToLowerInvariant();

        switch (store)
        {
            case "fake":
                m_Logger.LogInformation("Using fake store - skipping receipt validation");
                await ApplyPurchaseRewardsFromConfiguration(context, gameApiClient, productId);
                break;

            case "googleplay":
                await RedeemGooglePlayPurchase(context, gameApiClient, productId, receiptData.Payload, localCost, localCurrency);
                break;

            case "appleappstore":
                await RedeemAppleAppStorePurchase(context, gameApiClient, productId, receiptData.Payload, localCost, localCurrency);
                break;

            default:
                throw new ArgumentException($"Unsupported store type: {store}");
        }
    }

    /// <summary>
    /// Redeems a Google Play purchase by validating the receipt with the Economy service.
    /// The Economy service will automatically grant the rewards defined in the configuration.
    /// </summary>
    /// <param name="productId">ID of the product being purchased.</param>
    /// <param name="googlePayload">Receipt data from Google Play.</param>
    private async Task RedeemGooglePlayPurchase(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string productId,
        string googlePayload,
        double localCost,
        string currencyCode)
    {
        // Parse the Google-specific payload
        var googleReceipt = JsonConvert.DeserializeAnonymousType(googlePayload,
                new { json = "", signature = "" }) 
            ?? throw new JsonException("Failed to parse Google receipt payload.");
        
        if (string.IsNullOrWhiteSpace(googleReceipt.json) || string.IsNullOrWhiteSpace(googleReceipt.signature))
            throw new JsonException("Google payload missing json/signature.");

        var googleRequest = new PlayerPurchaseGoogleplaystoreRequest
        {
            Id = productId,
            PurchaseData = googleReceipt.json, // The actual Google receipt JSON
            PurchaseDataSignature = googleReceipt.signature,
            LocalCost = (int)(localCost * 100),
            LocalCurrency = currencyCode,
        };

        var purchaseResult = await gameApiClient.EconomyPurchases.RedeemGooglePlayPurchaseAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            context.PlayerId!,
            googleRequest);

        // Log what the player actually received
        foreach (var currency in purchaseResult.Data.Rewards.Currency)
        {
            m_Logger.LogInformation($"Granted {currency.Amount} {currency.Id}");
        }

        foreach (var item in purchaseResult.Data.Rewards.Inventory)
        {
            m_Logger.LogInformation($"Granted {item.Amount}x {item.Id}");
        }
    }

    private async Task RedeemAppleAppStorePurchase(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string productId,
        string applePayload,
        double localCost,
        string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(applePayload))
            throw new ArgumentException("Apple receipt payload is empty.", nameof(applePayload));

        var appleRequest = new PlayerPurchaseAppleappstoreRequest
        {
            Id = productId,
            Receipt = applePayload,
            LocalCost = (int)(localCost * 100),
            LocalCurrency = currencyCode,
        };

        var purchaseResult = await gameApiClient.EconomyPurchases.RedeemAppleAppStorePurchaseAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            context.PlayerId!,
            appleRequest);

        // Log what the player actually received
        foreach (var currency in purchaseResult.Data.Rewards.Currency)
        {
            m_Logger.LogInformation($"Granted {currency.Amount} {currency.Id}");
        }

        foreach (var item in purchaseResult.Data.Rewards.Inventory)
        {
            m_Logger.LogInformation($"Granted {item.Amount}x {item.Id}");
        }
    }

    /// <summary>
    /// Redeems an Apple App Store purchase by validating the receipt with the Economy service.
    /// The Economy service will automatically grant the rewards defined in the configuration.
    /// </summary>
    /// <param name="productId">ID of the product being purchased.</param>
    /// <param name="receipt">Receipt data from Apple App Store.</param>



    /// <summary>
    /// Manually grants rewards for a real money purchase based on the configuration.
    /// Used primarily for fake store purchases in the editor where receipt validation is skipped.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="gameApiClient">The game API client.</param>
    /// <param name="productId">ID of the product to grant rewards for.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ApplyPurchaseRewardsFromConfiguration(IExecutionContext context, IGameApiClient gameApiClient, string productId)
    {
        try
        {
            var configResponse = await gameApiClient.EconomyConfiguration.GetPlayerConfigurationAsync(
                context, 
                context.AccessToken, 
                context.ProjectId,
                context.PlayerId!
                );

            var realMoneyPurchase = GetRealMoneyPurchaseFromConfig(configResponse.Data.Results, productId);

            if (realMoneyPurchase?.Rewards != null)
            {
                await DistributeConfiguredRewards(context, gameApiClient, configResponse.Data.Results, realMoneyPurchase.Rewards);
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, $"Failed to grant rewards for product {productId}");
            throw;
        }
    }

    /// <summary>
    /// Retrieves the real money purchase definition for the specified product ID from the player configuration.
    /// </summary>
    /// <param name="results">The configuration results from the Economy service.</param>
    /// <param name="productId">The ID of the real money product to locate.</param>
    /// <returns>The matching RealMoneyPurchaseResource if found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no matching real money product is found.</exception>
    private RealMoneyPurchaseResource? GetRealMoneyPurchaseFromConfig(
        List<PlayerConfigurationResponseResultsInner> results,
        string productId)
    {
        foreach (var result in results)
        {
            if (result.ActualInstance is RealMoneyPurchaseResource purchase && purchase.Id == productId)
            {
                return purchase;
            }
        }

        m_Logger.LogError($"Real money purchase not found: {productId}");
        throw new InvalidOperationException($"Real money purchase not found: {productId}");
    }


    /// <summary>
    /// Processes the rewards from a purchase by granting each reward to the player.
    /// </summary>
    /// <param name="configResults">Configuration results from the player configuration.</param>
    /// <param name="rewards">List of rewards to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task DistributeConfiguredRewards(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        List<PlayerConfigurationResponseResultsInner> configResults,
        List<Reward> rewards)
    {
        foreach (var reward in rewards)
        {
            string resourceId = reward.ResourceId;
            int amount = reward.Amount;

            m_Logger.LogInformation($"Processing reward: {resourceId}, Amount: {amount}");

            string resourceType = m_PlayerEconomyService.GetResourceType(configResults, resourceId);
            await m_PlayerEconomyService.GrantResourceReward(context, gameApiClient, resourceType, resourceId, amount);
        }
    }

    /// <summary>
    /// Creates custom data specifically for a health potion inventory item
    /// </summary>
    /// <param name="potionAmount">The number of potions to add</param>
    /// <returns>Dictionary containing properly structured custom data</returns>
    private Dictionary<string, object> CreateHealthPotionCustomData(int potionAmount)
    {
        return new Dictionary<string, object>
        {
            { "amount", potionAmount },
            { "potency", 25 },                // Healing amount
            { "cooldownTime", 30 },           // Seconds before it can be used again
            { "effectDuration", 5 }           // How long the healing effect lasts
        };
    }

    /// <summary>
    /// Updates inventory items created from a purchase with specific custom data
    /// </summary>
    private async Task UpdatePurchaseRewardWithCustomData(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        List<InventoryExchangeItem> rewardItems,
        string itemId,
        Dictionary<string, object> customData)
    {
        try
        {
            // Find the specific reward item we want to update
            foreach (var item in rewardItems)
            {
                if (item.Id == itemId &&
                    item.PlayersInventoryItemIds != null &&
                    item.PlayersInventoryItemIds.Count > 0)
                {
                    // Update each rewarded item with the custom data
                    foreach (var playerItemId in item.PlayersInventoryItemIds)
                    {
                        var updateRequest = new InventoryRequestUpdate(instanceData: customData);

                        await gameApiClient.EconomyInventory.UpdateInventoryItemAsync(
                            context,
                            context.AccessToken,
                            context.ProjectId,
                            context.PlayerId!,
                            playerItemId,
                            updateRequest);

                        m_Logger.LogInformation($"Updated inventory item {playerItemId} with custom data");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, $"Failed to update reward with custom data for {itemId}");
            // Don't throw - we don't want to fail the purchase
        }
    }
}
