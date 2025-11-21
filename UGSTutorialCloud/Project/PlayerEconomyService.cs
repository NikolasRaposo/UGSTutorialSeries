using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

    public async Task<int> GetPlayerGold(IExecutionContext context, IGameApiClient gameApiClient)
    {
        return await GetCurrencyAmount(context, gameApiClient, k_GoldCurrencyKey);
    }
    public async Task<int> GetHealthPotionAmount(IExecutionContext context, IGameApiClient gameApiClient)
    {
        return await GetInventoryItemAmount(context, gameApiClient, k_HealthPotionKey);
    }
    [CloudCodeFunction("InitializeInventory")]
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
    
    [CloudCodeFunction("GetPlayerEconomyData")]
    public async Task<PlayerEconomyData> GetPlayerEconomyData(IExecutionContext context, IGameApiClient gameApiClient)
    {
        try
        {
            // Cria um objeto de informacao de economia
            var economyData = new PlayerEconomyData();
            // Pega o ouro do jogador e adiciona a informacao de economia
            int goldAmount = await GetPlayerGold(context, gameApiClient);
            economyData.Currencies[k_GoldCurrencyKey] = goldAmount;
            // Adicione qualquer outra moeda aqui...
            
            // Pega o inventario do jogador e adiciona a informacao de economia
            economyData.ItemInventory = await GetPlayerInventoryItemAmountMap(context, gameApiClient);
            return economyData;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Falha em sincronizar a informacao de economia do jogador: {PlayerId}", context.PlayerId);
            throw new Exception($"Falha em sincronizar a economia: {ex.Message}", ex);
        }
    }

    public async Task<PlayerEconomyData> InitializeNewPlayerEconomy(IExecutionContext context, IGameApiClient gameApiClient)
    {
        await InitializeInventory(context, gameApiClient);
        return await GetPlayerEconomyData(context, gameApiClient);
    }

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
            
            // Chama a API para pegar o inventario do jogador
            var playerInventory = await gameApiClient.EconomyInventory.GetPlayerInventoryAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId!,
                inventoryItemIds: ids,
                limit: limit);

            return playerInventory.Data.Results;
        }
        catch (Exception ex)
        {
            m_Logger.LogError($"Falha em pegar o inventario do jogador '{context.PlayerId}'. Erro: {ex.Message}'");
            throw new Exception($"Failed to get inventory: {ex.Message}", ex);
        }
    }

    private async Task<Dictionary<string, int>> GetPlayerInventoryItemAmountMap(IExecutionContext context, IGameApiClient gameApiClient, params string[]? inventoryItemIds)
    {
        var items = await GetPlayerInventory(context, gameApiClient, inventoryItemIds: inventoryItemIds);
        return items
            .Where(item => !string.IsNullOrEmpty(item.InventoryItemId))
            .ToDictionary(
                item => item.InventoryItemId!, 
                item => GetInventoryItemCustomData<int?>(item, "amount") ?? 1
                );
    }

    private T? GetInventoryItemCustomData<T>(InventoryResponse item, string key)
    {
        if (item?.InstanceData == null) return default;
        try
        {
            // Converte para JObject se ja nao tiver feito
            var jObject = item.InstanceData as Newtonsoft.Json.Linq.JObject
                ?? Newtonsoft.Json.Linq.JObject.Parse(item.InstanceData?.ToString() ?? "{}");
            // Pega o valor usando uma sintaxe de indexacao
            var token = jObject[key];
            if (token != null)
            {
                // Converte para o tipo requisitado
                return token.ToObject<T>();
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning($"Falha em pegar {key} do item {item.InventoryItemId}: {ex.Message}");
        }
        return default;
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

    
}
