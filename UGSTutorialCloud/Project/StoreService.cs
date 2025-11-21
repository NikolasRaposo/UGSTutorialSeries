using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.Economy.Model;
namespace UGSTutorialCloud;

public class StoreService
{
    private const string k_HealthPotionPurchaseId = "HEALTH_POTION_VIRTUAL_PURCHASE";
    
    private PlayerEconomyService m_PlayerEconomyService;
    
    private static ILogger<StoreService> m_Logger = null!;
    
    public StoreService(ILogger<StoreService> logger, PlayerEconomyService playerEconomy)
    {
        m_Logger = logger;
        m_PlayerEconomyService = playerEconomy;
    }
    [CloudCodeFunction("VirtualPurchaseHealthPotion")]
    public async Task<PlayerEconomyData> VirtualPurchaseHealthPotion(IExecutionContext context, IGameApiClient gameApiClient)
    {
        try
        {
            await ProcessVirtualPurchase(context, gameApiClient, k_HealthPotionPurchaseId);
            await m_PlayerEconomyService.CleanUpNullOrZeroAmountItems(context, gameApiClient, PlayerEconomyService.k_HealthPotionKey);
            await m_PlayerEconomyService.AddOrUpdateInventoryItemAmount(context, gameApiClient, PlayerEconomyService.k_HealthPotionKey, 1);
            return await m_PlayerEconomyService.GetPlayerEconomyData(context, gameApiClient);
        }
        catch (ApiException ex)
        {
            m_Logger.LogError(ex, $"Falha em comprar pocao: {context.PlayerId}");
            throw new Exception($"Falha em comprar pocao: {ex.Message}", ex);
        }
    }
    private async Task ProcessVirtualPurchase(IExecutionContext context, IGameApiClient gameApiClient, string virtualPurchaseID)
    {
        try
        {
            var purchaseRequest = new PlayerPurchaseVirtualRequest(virtualPurchaseID);
            var purchaseResponse = await gameApiClient.EconomyPurchases.MakeVirtualPurchaseAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId ?? throw new InvalidOperationException("PlayerId e nulo"),
                purchaseRequest);
            if (purchaseResponse == null || purchaseResponse.Data == null || purchaseResponse.Data.Rewards == null)
            {
                m_Logger.LogWarning($"Estrutura de Resposta de compra invalida para {virtualPurchaseID}");
                return;
            }
            List<InventoryExchangeItem> rewardItems = purchaseResponse.Data.Rewards.Inventory;
            m_Logger.LogInformation("Compra Virtual: "+JsonConvert.SerializeObject(rewardItems));
        }
        catch (ApiException ex)
        {
            m_Logger.LogError(ex, $"Falha em processar a compra da pocao: {context.PlayerId}");
            throw;
        }
    }
}
