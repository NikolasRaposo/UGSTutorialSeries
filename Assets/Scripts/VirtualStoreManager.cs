using System;
using Newtonsoft.Json;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.GeneratedBindings;
using Unity.Services.Economy;
using UnityEngine;

public class VirtualStoreManager : MonoBehaviour
{
    [SerializeField] PlayerEconomyManager m_PlayerEconomyManager;
    [Header("Purchase IDs")]
    [SerializeField] private string m_HealthPotionPurchaseId = "HEALTH_POTION_VIRTUAL_PURCHASE";

    private int m_CurrentPotionCost;
    private const int k_DefaultPotionPurchaseCost = 20;
    private StoreServiceBindings m_Bindings;
    private void OnEnable()
    {
        m_PlayerEconomyManager.EconomyConfigSynced += InitializeVirtualStore;
    }
    private void Start()
    {
        m_Bindings = new StoreServiceBindings(CloudCodeService.Instance);
        m_CurrentPotionCost = k_DefaultPotionPurchaseCost;
    }

    private void InitializeVirtualStore()
    {
        try
        {
            LogVirtualPurchasesFromConfig();
            InitializePurchaseCosts();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Falha em sincronizar a configuracao de economia:  {ex.Message}");
        }
    }
    private void LogVirtualPurchasesFromConfig()
    {
        var virtualPurchases = EconomyService.Instance.Configuration.GetVirtualPurchases();
        string virtualPurchasesJson = JsonConvert.SerializeObject(virtualPurchases,  Formatting.Indented);
        Debug.Log($"Compra virtual da configuracao de economia: {virtualPurchasesJson}");
    }
    
    private void InitializePurchaseCosts()
    {
        try
        {
            var purchaseDefinition = EconomyService.Instance.Configuration.GetVirtualPurchase(m_HealthPotionPurchaseId);
            if (purchaseDefinition == null)
            {
                Debug.LogWarning($"Compra virtual {m_HealthPotionPurchaseId} nao encontrada. Usando custo padrao");
                return;
            }
            foreach (var cost in purchaseDefinition.Costs)
            {
                if (cost.Item.GetReferencedConfigurationItem().Id == PlayerEconomyManager.k_GoldCurrencyKey)
                {
                    m_CurrentPotionCost = cost.Amount;
                    Debug.Log($"Custo de Pocao de Cura definido para {m_CurrentPotionCost} de gold");
                    return;
                }
            }
            Debug.LogWarning($"Nao foi encontrado custo em gold para a compra {m_HealthPotionPurchaseId}. Usando valores padrao");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Erro ao inicializar os custos de compra: {ex.Message}. Usando valores padrao");
        }
        
    }

    public async void PurchaseHealthPotion()
    {
        if (!CanAffordVirtualPurchase(m_CurrentPotionCost))
        {
            Debug.LogWarning($"Sem Gold suficiente! Precisa de {m_CurrentPotionCost}, tem {m_PlayerEconomyManager.Gold}.");
            return;
        }
        try
        {
            var economyData = await m_Bindings.VirtualPurchaseHealthPotion();
            Debug.Log($"Comprado com Sucesso - Produto: {m_HealthPotionPurchaseId}");
            m_PlayerEconomyManager.HandleEconomyUpdate(economyData);
        }
        catch (CloudCodeException ex)
        {
            Debug.LogException(ex);
        }
    }
    private bool CanAffordVirtualPurchase(int cost)
    {
        var gold = m_PlayerEconomyManager.Gold;
        return gold >= cost;
    }

    private void OnDisable()
    {
        m_PlayerEconomyManager.EconomyConfigSynced -= InitializeVirtualStore;
    }


}
