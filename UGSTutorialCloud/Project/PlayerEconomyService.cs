using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.Economy.Model;

namespace UGSTutorialCloud;

public class PlayerEconomyService
{
    public const string k_GoldCurrencyKey = "GOLD";
    public const string k_HealthPotionKey = "HEALTH_POTION";
    private const int k_StartingHealthPotions = 3;

    private readonly ILogger<PlayerEconomyService> m_Logger;

    public PlayerEconomyService(ILogger<PlayerEconomyService> logger)
    {
        m_Logger = logger;
    }

    #region Cloud Code Functions

    [CloudCodeFunction("GetPlayerEconomyData")]
    public async Task<PlayerEconomyData> GetPlayerEconomyData(IExecutionContext context, IGameApiClient gameApiClient)
    {
        try
        {
            await CleanUpNullOrZeroAmountItems(context, gameApiClient, k_HealthPotionKey);

            // Create economy data object
            var economyData = new PlayerEconomyData();

            // Get player gold and add to economy data
            int goldAmount = await GetPlayerGold(context, gameApiClient);
            economyData.Currencies[k_GoldCurrencyKey] = goldAmount;

            // Add any other currencies here...

            // Get player inventory and add to economy data
            economyData.ItemInventory = await GetPlayerInventoryItemAmountMap(context, gameApiClient);

            return economyData;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to get economy data: {PlayerId}", context.PlayerId);
            throw new Exception($"Failed to get economy: {ex.Message}", ex);
        }
    }

    #endregion

    #region Initialization /  Setup

    public async Task<PlayerEconomyData> InitializeNewPlayerEconomy(IExecutionContext context, IGameApiClient gameApiClient)
    {
        await InitializeInventory(context, gameApiClient);
        return await GetPlayerEconomyData(context, gameApiClient);
    }

    private async Task InitializeInventory(IExecutionContext context, IGameApiClient gameApiClient)
    {
        var startingItems = new Dictionary<string, int>
        {
            { k_HealthPotionKey, k_StartingHealthPotions },
        };

        foreach (var item in startingItems)
        {
            try
            {
                await AddNewInventoryItem(context, gameApiClient, item.Key, item.Value);
            }

            catch (Exception ex)
            {
                m_Logger.LogError(ex, $"Failed to grant initial inventory item {item.Key}");
            }
        }
    }

    #endregion

    public async Task AddCurrency(IExecutionContext context, IGameApiClient gameApiClient, string currencyId, int amount)
    {
        CurrencyModifyBalanceRequest request = new CurrencyModifyBalanceRequest(currencyId, amount);

        // Example implementation using Economy API
        await gameApiClient.EconomyCurrencies.IncrementPlayerCurrencyBalanceAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            context.PlayerId!,
            currencyId,
            request
        );
    }

    public async Task AddOrUpdateInventoryItemAmount(IExecutionContext context, IGameApiClient gameApiClient, string itemKey, int amountToAdd,
        Dictionary<string, object>? customData = null) // Optional parameter for other custom data
    {
        var inventoryItems = await GetPlayerInventory(context, gameApiClient, inventoryItemIds: new string[] { itemKey });
        InventoryResponse? existingItem = inventoryItems.FirstOrDefault(item => !string.IsNullOrEmpty(item.PlayersInventoryItemId));
        bool itemExistsInInventory = existingItem != null;

        // Determine amount to use
        int totalAmount = amountToAdd;
        if (itemExistsInInventory)
        {
            TryParseInventoryItemAmount(existingItem!, out int currentAmount); // Defaults to 0 if parsing fails

            totalAmount = currentAmount + amountToAdd;
        }

        // Prepare instance data with amount
        var instanceData = new Dictionary<string, object>
        {
            { "amount", totalAmount }
        };

        // Add any custom data provided
        if (customData != null)
        {
            foreach (var kvp in customData)
            {
                instanceData[kvp.Key] = kvp.Value;
            }
        }

        if (itemExistsInInventory)
        {
            // Update existing item with new data. Note: This approach replaces ALL instance data. You might want to retrieve and merge with existing data.
            var updateRequest = new InventoryRequestUpdate(instanceData: instanceData);
            await gameApiClient.EconomyInventory.UpdateInventoryItemAsync(context, context.AccessToken, context.ProjectId, context.PlayerId!,
                existingItem!.PlayersInventoryItemId!, updateRequest);
        }
        else
        {
            // Create new item with data
            await AddNewInventoryItem(context, gameApiClient, itemKey, instanceData);
        }
    }

    /// <summary>
    /// Deletes any inventory items of itemKey that have null custom data or an amount property <= 0
    /// </summary>
    public async Task CleanUpNullOrZeroAmountItems(IExecutionContext context, IGameApiClient gameApiClient, string itemKey)
    {
        try
        {
            var items = await GetPlayerInventory(context, gameApiClient, inventoryItemIds: new string[] { itemKey });

            var itemsToDelete = new List<string>();

            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.PlayersInventoryItemId)) continue;

                // Check for null instance data
                if (item.InstanceData == null)
                {
                    itemsToDelete.Add(item.PlayersInventoryItemId);
                    m_Logger.LogInformation($"Found {itemKey} with null instance data: {item.PlayersInventoryItemId}");
                    continue;
                }

                if (!TryParseInventoryItemAmount(item, out int amount))
                {
                    continue;
                }

                if (amount <= 0)
                {
                    itemsToDelete.Add(item.PlayersInventoryItemId);
                }
            }

            foreach (var itemId in itemsToDelete)
            {
                await DeleteInventoryItem(context, gameApiClient, itemId);
                m_Logger.LogInformation($"Deleted zero-amount {itemId}");
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, $"Failed to clean up zero-amount {itemKey} for player {context.PlayerId}");
            // Don't fail purchase — just log
        }
    }

    /// <summary>
    /// Updates an item's amount
    /// </summary>
    private async Task UpdateItemAmount(IExecutionContext context, IGameApiClient gameApiClient, string itemId, int amount)
    {
        var instanceData = new Dictionary<string, object>
        {
            { "amount", amount }
        };

        var updateRequest = new InventoryRequestUpdate(instanceData: instanceData);

        await gameApiClient.EconomyInventory.UpdateInventoryItemAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"),
            itemId,
            updateRequest
        );

        m_Logger.LogInformation($"Updated potion {itemId} with amount: {amount}");
    }

    /// <summary>
    /// Deletes a single inventory item
    /// </summary>
    public async Task DeleteInventoryItem(IExecutionContext context, IGameApiClient gameApiClient, string inventoryItemId)
    {
        await gameApiClient.EconomyInventory.DeleteInventoryItemAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"),
            inventoryItemId
        );
    }

    #region Public Helper Methods

    /// <summary>
    /// Grants a single reward resource to the player based on its type.
    /// </summary>
    /// <param name="resourceType">Type of the resource (CURRENCY or INVENTORY_ITEM).</param>
    /// <param name="resourceId">ID of the resource to grant.</param>
    /// <param name="amount">Amount of the resource to grant.</param>
    public async Task GrantResourceReward(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string resourceType,
        string resourceId,
        int amount)
    {
        switch (resourceType)
        {
            case "CURRENCY":
                await AddCurrency(context, gameApiClient, resourceId, amount);
                m_Logger.LogInformation($"Added currency: {resourceId}, Amount: {amount}");
                break;

            case "INVENTORY_ITEM":
                await AddOrUpdateInventoryItemAmount(context, gameApiClient, resourceId, amount);
                m_Logger.LogInformation($"Added inventory item: {resourceId}, Amount: {amount}");
                break;

            default:
                m_Logger.LogWarning($"Unknown resource type for reward: {resourceType}");
                break;
        }
    }

    public async Task AddNewInventoryItem(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string itemId,
        int amount)
    {
        var instanceData = new Dictionary<string, object>
        {
            { "amount", amount }
        };

        await AddNewInventoryItem(context, gameApiClient, itemId, instanceData);
    }

    public async Task AddNewInventoryItem(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        string itemId,
        Dictionary<string, object> instanceData)
    {

        var inventoryRequest = new AddInventoryRequest(itemId, instanceData: instanceData);

        try
        {
            await gameApiClient.EconomyInventory.AddInventoryItemAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"),
                inventoryRequest
            );
        }
        catch (ApiException ex)
        {
            m_Logger.LogError(
                $"Failed to add inventory item '{itemId}' for player '{context.PlayerId}'. Error: {ex.Message}");
            throw new Exception($"Failed to add inventory item '{itemId}': {ex.Message}", ex);
        }
    }

    #endregion

    #region Convienence Utility Functions

    private async Task<int> GetPlayerGold(IExecutionContext context, IGameApiClient gameApiClient)
    {
        return await GetCurrencyAmount(context, gameApiClient, k_GoldCurrencyKey);
    }

    private async Task<int> GetHealthPotionAmount(IExecutionContext context, IGameApiClient gameApiClient)
    {
        return await GetInventoryItemAmount(context, gameApiClient, k_HealthPotionKey);
    }

    #endregion

    #region Internal Data Access / Helpers

    /// <summary>
    /// Gets the player's inventory items.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="gameApiClient">The game API client.</param>
    /// <param name="limit">Maximum number of items to return (defaults to API default).</param>
    /// <param name="inventoryItemIds">Optional filter by inventory item ID.</param>
    /// <returns>A list of inventory response objects.</returns>
    private async Task<List<InventoryResponse>> GetPlayerInventory(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        int? limit = null,
        params string[]? inventoryItemIds)
    {
        try
        {
            List<string>? ids = inventoryItemIds?.Length > 0
                ? inventoryItemIds.ToList()
                : null;

            // Call the API to get player inventory
            var playerInventory = await gameApiClient.EconomyInventory.GetPlayerInventoryAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId!,
                inventoryItemIds: ids,
                limit: limit
            );

            return playerInventory.Data.Results;
        }

        catch (ApiException ex)
        {
            m_Logger.LogError($"Failed to get inventory for player '{context.PlayerId}'. Error: {ex.Message}");
            throw new Exception($"Failed to get inventory: {ex.Message}", ex);
        }
    }


    /// <summary>
    /// Gets the player's inventory items as a simplified dictionary of itemId to amount.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="gameApiClient">The game API client.</param>
    /// <param name="inventoryItemIds">Optional filter by inventory item ID.</param>
    /// <returns>A dictionary mapping item IDs to amounts.</returns>
    private async Task<Dictionary<string, int>> GetPlayerInventoryItemAmountMap(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        params string[]? inventoryItemIds)
    {
        var items = await GetPlayerInventory(context, gameApiClient, inventoryItemIds: inventoryItemIds);

        return items
            .Where(item => !string.IsNullOrEmpty(item.InventoryItemId))
            .ToDictionary(
                item => item.InventoryItemId!,
                item => GetInventoryItemCustomData<int?>(item, "amount") ?? 1
            );
    }

    private async Task<int> GetCurrencyAmount(IExecutionContext context, IGameApiClient gameApiClient, string key)
    {
        try
        {
            var playerCurrenciesData = await gameApiClient.EconomyCurrencies.GetPlayerCurrenciesAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId!
                );

            // Find the currency with the matching key
            CurrencyBalanceResponse? targetCurrency = 
                playerCurrenciesData.Data.Results.FirstOrDefault(currency => currency.CurrencyId == key);

            if (targetCurrency != null)
            {
                return (int)targetCurrency.Balance;
            }
            else
            {
                throw new Exception($"Currency '{key}' not found");
            }
        }
        catch (ApiException ex)
        {
            throw new Exception($"Failed to get currency '{key}' for player '{context.PlayerId}'. Error: {ex.Message}");
        }
    }

    private async Task<int> GetInventoryItemAmount(IExecutionContext context, IGameApiClient gameApiClient, string key)
    {
        try
        {
            var inventoryResponse = await gameApiClient.EconomyInventory.GetPlayerInventoryAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId!,
                inventoryItemIds: new List<string> { key }
                );

            InventoryResponse? item = inventoryResponse.Data.Results.FirstOrDefault();

            if (item == null)
            {
                // This could happen if the item hasn't been added to inventory yet
                m_Logger.LogInformation($"Inventory item {key} not found for player '{context.PlayerId}'");
                return 0; // Return 0 when item doesn't exist instead of throwing
            }

            if (!TryParseInventoryItemAmount(item, out int amount))
            {
                // TryParseInventoryItemAmount already logs the error
                return 0; // Return 0 when parsing fails instead of throwing
            }

            return amount;

        }
        catch (ApiException ex)
        {
            m_Logger.LogError(ex, $"Failed to get inventory item data for player '{context.PlayerId}'");
            throw new Exception($"Failed to get inventory item data for player '{context.PlayerId}'. Error: {ex.Message}");
        }
    }

    private bool TryParseInventoryItemAmount(InventoryResponse itemResponse, out int amount)
    {
        amount = 0; // Default value if parsing fails

        if (itemResponse.InstanceData == null)
        {
            m_Logger.LogWarning($"Item '{itemResponse.InventoryItemId}' instance data is null");
            return false;
        }

        try
        {
            string json = $"{itemResponse.InstanceData}";
            var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

            if (data != null && data.TryGetValue("amount", out var amountObj))
            {
                if (int.TryParse(amountObj.ToString(), out amount))
                {
                    return true;
                }

                m_Logger.LogWarning($"Amount value '{amountObj}' for '{itemResponse.InventoryItemId}' is not a valid integer");
                return false;
            }

            m_Logger.LogWarning($"Instance data for '{itemResponse.InventoryItemId}' doesn't contain an 'amount' property");
            return false;
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning($"Failed to parse inventory item amount for '{itemResponse.InventoryItemId}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Extracts a single field from an inventory item's instance data.
    /// </summary>
    /// <typeparam name="T">The type to convert the field value to</typeparam>
    /// <param name="item">The inventory item containing instance data</param>
    /// <param name="key">The field key to extract</param>
    /// <returns>The extracted field value, or default if not found or on error</returns>
    private T? GetInventoryItemCustomData<T>(InventoryResponse item, string key)
    {
        if (item?.InstanceData == null) return default;

        try
        {
            // Convert to JObject if it isn't already
            var jObject = item.InstanceData as Newtonsoft.Json.Linq.JObject
                ?? Newtonsoft.Json.Linq.JObject.Parse(item.InstanceData?.ToString() ?? "{}");

            // Get value using indexer syntax
            var token = jObject[key];
            if (token != null)
            {
                // Convert to requested type
                return token.ToObject<T>();
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning($"Failed to get '{key}' from item '{item.InventoryItemId}': {ex.Message}");
        }

        return default;
    }

    private Dictionary<string, object> GetAllCustomDataForInventoryItem(InventoryResponse item)
    {
        if (item?.InstanceData == null) return new Dictionary<string, object>();

        try
        {
            var jObject = item.InstanceData as Newtonsoft.Json.Linq.JObject
                ?? Newtonsoft.Json.Linq.JObject.Parse(item.InstanceData?.ToString() ?? "{}");

            // Convert JObject to Dictionary
            return jObject.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning($"Failed to get instance data from item '{item.InventoryItemId}': {ex.Message}");
            return new Dictionary<string, object>();
        }
    }

    // Helper method to determine the type of a resource from its ID
    public string GetResourceType(List<PlayerConfigurationResponseResultsInner> results, string resourceId)
    {
        foreach (var result in results)
        {
            // Check the actual instance type directly
            switch (result.ActualInstance)
            {
                case CurrencyResource currency when currency.Id == resourceId:
                    return "CURRENCY";
                case InventoryItemResource item when item.Id == resourceId:
                    return "INVENTORY_ITEM";
                case RealMoneyPurchaseResource purchase when purchase.Id == resourceId:
                    return "REAL_MONEY_PURCHASE";
                case VirtualPurchaseResource virtualPurchase when virtualPurchase.Id == resourceId:
                    return "VIRTUAL_PURCHASE";
            }
        }
        return "UNKNOWN";
    }

    #endregion
}