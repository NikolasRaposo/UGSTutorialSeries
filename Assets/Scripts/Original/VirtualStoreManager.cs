using Newtonsoft.Json;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.GeneratedBindings;
using Unity.Services.Economy;
using UnityEngine;
namespace Original
{
    public class VirtualStoreManager : MonoBehaviour
    {
        [SerializeField]
        PlayerEconomyManager m_PlayerEconomyManager;

        [Header("Purchase IDs")]
        [SerializeField]
        private string m_HealthPotionPurchaseId = "HEALTH_POTION_VIRTUAL_PURCHASE";

        private int m_CurrentPotionCost;
        private const int k_DefaultPotionPurchaseCost = 20;

        private StoreServiceBindings m_Bindings;

        private void Start()
        {
            m_PlayerEconomyManager.EconomyConfigSynced += InitializeVirtualStore;

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
                Debug.LogError($"Failed to sync Economy configuration: {ex.Message}");
            }
        }

        private void LogVirtualPurchasesFromConfig()
        {
            var virtualPurchases = EconomyService.Instance.Configuration.GetVirtualPurchases();
            string virtualPurchasesJson = JsonConvert.SerializeObject(virtualPurchases, Formatting.Indented);
            Debug.Log($"Virtual purchases from economy config: {virtualPurchasesJson}");
        }

        private void InitializePurchaseCosts()
        {
            try
            {
                // Get the virtual purchase definition using the ID
                var purchaseDefinition = EconomyService.Instance.Configuration.GetVirtualPurchase(m_HealthPotionPurchaseId);

                if (purchaseDefinition == null)
                {
                    Debug.LogWarning($"Virtual purchase {m_HealthPotionPurchaseId} not found. Using default cost.");
                    return;
                }

                // Look for gold cost in the purchase costs
                foreach (var cost in purchaseDefinition.Costs)
                {
                    if (cost.Item.GetReferencedConfigurationItem().Id == PlayerEconomyManager.k_GoldCurrencyKey)
                    {
                        m_CurrentPotionCost = cost.Amount;
                        Debug.Log($"Health Potion cost set to {m_CurrentPotionCost} gold");
                        return;
                    }
                }

                Debug.LogWarning($"Could not find gold cost for purchase {m_HealthPotionPurchaseId}. Using default value.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error initializing purchase costs: {ex.Message}. Using default values.");
            }
        }

        public async void PurchaseHealthPotion()
        {
            // Check if player can afford potion
            if (!CanAffordVirtualPurchase(m_CurrentPotionCost))
            {
                Debug.LogWarning($"Not enough gold! Need {m_CurrentPotionCost}, have {m_PlayerEconomyManager.Gold}");
                return;
            }

            try
            {
                // Process purchase through Cloud Code
                var economyData = await m_Bindings.VirtualPurchaseHealthPotion();

                Debug.Log($"SuccessfullyPurchased - Product: {m_HealthPotionPurchaseId}");
                // Update local economy data
                m_PlayerEconomyManager.HandleEconomyUpdate(economyData);
            }
            catch (CloudCodeException ex)
            {
                Debug.LogException(ex);
            }
        }

        public bool CanAffordVirtualPurchase(int cost)
        {
            var gold = m_PlayerEconomyManager.Gold;

            // Check if player has enough gold
            return gold >= cost;
        }

        private void OnDestroy()
        {
            m_PlayerEconomyManager.EconomyConfigSynced -= InitializeVirtualStore;
        }
    }
}

