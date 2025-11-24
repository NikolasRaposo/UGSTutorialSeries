using System;
using Core.Debugging;
using Newtonsoft.Json;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.GeneratedBindings;
using Unity.Services.Economy;
using UnityEngine;
namespace Custom
{
    /// <summary>
    /// Manages virtual purchases by interfacing with Unity Economy Service and Cloud Code.
    /// Handles validation of funds and execution of purchase transactions.
    /// </summary>
    public class VirtualStoreManager : DebuggableMonoBehaviour
    {
        [Header("Dependencies")]
        [Tooltip("Reference to the Economy Manager to check balance and update local data.")]
        [SerializeField] private PlayerEconomyManager playerEconomyManager;

        [Header("Purchase Configuration")]
        [Tooltip("The ID of the Virtual Purchase as defined in the Unity Dashboard.")]
        [SerializeField] private string healthPotionPurchaseId = "HEALTH_POTION_VIRTUAL_PURCHASE";

        // Internal state for costs
        private int _currentPotionCost;
        private const int DefaultPotionPurchaseCost = 20;

        // Cloud Code Bindings
        private StoreServiceBindings _bindings;

        protected override void Awake()
        {
            base.Awake();

            if (playerEconomyManager == null)
            {
                LogError("CRITICAL: PlayerEconomyManager reference is missing in VirtualStoreManager.");
            }
        }

        private void Start()
        {
            _bindings = new StoreServiceBindings(CloudCodeService.Instance);
            _currentPotionCost = DefaultPotionPurchaseCost;
        }

        private void OnEnable()
        {
            if (playerEconomyManager != null)
            {
                playerEconomyManager.EconomyConfigSynced += InitializeVirtualStore;
            }
        }

        /// <summary>
        /// Initializes the store data once the economy configuration has been synced from the server.
        /// </summary>
        private void InitializeVirtualStore()
        {
            try
            {
                Log("Initializing Virtual Store...");
                LogVirtualPurchasesFromConfig();
                InitializePurchaseCosts();
            }
            catch (Exception ex)
            {
                LogError($"Failed to sync economy configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs all available virtual purchases defined in the Economy Configuration for debugging.
        /// </summary>
        private void LogVirtualPurchasesFromConfig()
        {
            var virtualPurchases = EconomyService.Instance.Configuration.GetVirtualPurchases();
            var virtualPurchasesJson = JsonConvert.SerializeObject(virtualPurchases, Formatting.Indented);

            Log($"Loaded Virtual Purchases Config: {virtualPurchasesJson}");
        }

        /// <summary>
        /// Looks up the specific cost for the Health Potion from the Economy Configuration.
        /// Falls back to default values if the configuration is missing or incorrect.
        /// </summary>
        private void InitializePurchaseCosts()
        {
            try
            {
                var purchaseDefinition = EconomyService.Instance.Configuration.GetVirtualPurchase(healthPotionPurchaseId);

                if (purchaseDefinition == null)
                {
                    LogWarning($"Virtual Purchase ID '{healthPotionPurchaseId}' not found in config. Using default cost: {DefaultPotionPurchaseCost}");
                    return;
                }

                foreach (var cost in purchaseDefinition.Costs)
                {
                    // Check if the cost is in Gold (using the constant from PlayerEconomyManager)
                    if (cost.Item.GetReferencedConfigurationItem().Id == PlayerEconomyManager.GoldCurrencyKey)
                    {
                        _currentPotionCost = cost.Amount;
                        Log($"Health Potion cost updated from config: {_currentPotionCost} Gold");
                        return;
                    }
                }

                LogWarning($"No Gold cost found for purchase '{healthPotionPurchaseId}'. Using default cost.");
            }
            catch (Exception ex)
            {
                LogError($"Error initializing purchase costs: {ex.Message}. Using default values.");
            }
        }

        /// <summary>
        /// Attempts to purchase a health potion via Cloud Code.
        /// Checks local balance first to avoid unnecessary network calls.
        /// </summary>
        public async void PurchaseHealthPotion()
        {
            try
            {
                // Pre-validation: Check if player can afford it locally
                if (!CanAffordVirtualPurchase(_currentPotionCost))
                {
                    LogWarning($"Insufficient Funds! Required: {_currentPotionCost}, Available: {playerEconomyManager.Gold}.");
                    return;
                }

                try
                {
                    Log($"Attempting to purchase '{healthPotionPurchaseId}' via Cloud Code...");

                    // Execute purchase on the server
                    var economyData = await _bindings.VirtualPurchaseHealthPotion();

                    Log("Purchase Successful!");

                    // Update local economy with the result from the server
                    playerEconomyManager.HandleEconomyUpdate(economyData);
                }
                catch (CloudCodeException ex)
                {
                    LogError($"Cloud Code Purchase Failed: {ex.Message} (Code: {ex.ErrorCode})");
                }
            }
            catch (Exception e)
            {
                LogError($"Unexpected error during purchase: {e.Message}");
            }
        }

        /// <summary>
        /// Helper to check if the player has enough gold.
        /// </summary>
        /// <param name="cost">The amount of gold required.</param>
        /// <returns>True if balance is sufficient, otherwise false.</returns>
        private bool CanAffordVirtualPurchase(int cost)
        {
            if (playerEconomyManager == null) return false;

            var gold = playerEconomyManager.Gold;
            return gold >= cost;
        }

        private void OnDisable()
        {
            if (playerEconomyManager != null)
            {
                playerEconomyManager.EconomyConfigSynced -= InitializeVirtualStore;
            }
        }
    }
}
