using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.GeneratedBindings;
using Unity.Services.CloudCode.GeneratedBindings.UGSTutorialCloud;
using Unity.Services.Economy;
using Unity.Services.Economy.Model;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;
namespace Original
{
    public class IAPStoreManagerV5 : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField]
        PlayerEconomyManager m_PlayerEconomyManager;

        private const string k_GoldPurchase100Id = "GOLD_PURCHASE_100";

        private bool m_IsPurchaseInProgress;

        private StoreServiceBindings m_Bindings;

        // v5 controller (one stop for fetching, purchasing, confirming)
        private StoreController m_StoreController;

        // Optional Google validator (Apple is StoreKit2 internal in v5)
        private CrossPlatformValidator m_GoogleValidator;

        public event Action<string> SuccessfullyPurchased;
        public event Action<string> PurchaseFailed;

        private void Start()
        {
            // Wait for Economy config before wiring IAP, so products are defined from the server config.
            m_PlayerEconomyManager.EconomyConfigSynced += InitializeIAPAsync;

            m_Bindings = new StoreServiceBindings(CloudCodeService.Instance);
        }

        private void OnDestroy()
        {
            m_PlayerEconomyManager.EconomyConfigSynced -= InitializeIAPAsync;
            UnsubscribeIAPEvents();
        }


        private async void InitializeIAPAsync()
        {
            // Get Controller
            m_StoreController = UnityIAPServices.StoreController();

            // Subscribe to events
            SubscribeIAPEvents();

            try
            {
                // When this call completes, you may assume that IAP has connected to your current app store.
                await m_StoreController.Connect();
                Debug.Log("[IAP] Connected to store.");

                // Build and fetch products from Economy config
                var productDefs = BuildProductDefinitionsFromEconomy();
                if (productDefs.Count == 0)
                {
                    Debug.LogWarning("[IAP] No real-money products found in Economy config.");
                    return;
                }

                // Requests information from the store with the product info (ids, type)
                // The store responds with current prices and localized descriptions
                // Unity IAP fires the OnProductsFetched event with the data
                m_StoreController.FetchProducts(productDefs);
            }
            catch (Exception e)
            {
                Debug.LogError($"[IAP] Connect failed: {e.Message}");
            }

            InitializeReceiptValidatorsIfNeeded();
        }

        private void SubscribeIAPEvents()
        {
            if (m_StoreController == null) return;

            // Product fetch lifecycle
            m_StoreController.OnProductsFetched += OnProductsFetched;
            m_StoreController.OnProductsFetchFailed += OnProductsFetchFailed;

            m_StoreController.OnPurchasesFetched += OnPurchasesFetched;
            m_StoreController.OnPurchasesFetchFailed += OnPurchasesFetchFailed;

            // Purchase lifecycle
            m_StoreController.OnPurchasePending += OnPurchasePending;       
            m_StoreController.OnPurchaseConfirmed += OnPurchaseConfirmed;   // fires after ConfirmPurchase
            m_StoreController.OnPurchaseFailed += OnPurchaseFailed;         


            // Disconnection
            m_StoreController.OnStoreDisconnected += OnStoreDisconnected;
        }

        private void UnsubscribeIAPEvents()
        {
            if (m_StoreController == null) return;

            m_StoreController.OnProductsFetched -= OnProductsFetched;
            m_StoreController.OnProductsFetchFailed -= OnProductsFetchFailed;

            m_StoreController.OnPurchasesFetched -= OnPurchasesFetched;
            m_StoreController.OnPurchasesFetchFailed -= OnPurchasesFetchFailed;

            m_StoreController.OnPurchasePending -= OnPurchasePending;
            m_StoreController.OnPurchaseConfirmed -= OnPurchaseConfirmed;
            m_StoreController.OnPurchaseFailed -= OnPurchaseFailed;

            m_StoreController.OnStoreDisconnected -= OnStoreDisconnected;
        }

        /// <summary>
        /// Build IAP v5 ProductDefinitions from the Economy RealMoneyPurchaseDefinition list.
        /// In v5 you typically pass your cross-store ID as the ProductDefinition id,
        /// and supply the platform-specific ID via the storeSpecificId if you want (optional).
        /// </summary>
        private List<ProductDefinition> BuildProductDefinitionsFromEconomy()
        {
            var productDefinitions = new List<ProductDefinition>();
            var realMoney = EconomyService.Instance.Configuration.GetRealMoneyPurchases();
            LogRealPurchasesFromConfig(realMoney);

            foreach (var purchase in realMoney)
            {
                // If you need per-store IDs, you can branch on Application.platform or store name.
                // For simplicity, just use the Economy ID; you can add a platform-specific
                // storeSpecificId overload later if required.
                var def = new ProductDefinition(id: purchase.Id, type: ProductType.Consumable);
                productDefinitions.Add(def);
            }

            Debug.Log($"[IAP] Prepared {productDefinitions.Count} ProductDefinitions for fetch.");
            return productDefinitions;
        }

        private void LogRealPurchasesFromConfig(List<RealMoneyPurchaseDefinition> realMoneyPurchases)
        {
            Debug.Log($"Real purchases from economy config:\n{JsonConvert.SerializeObject(realMoneyPurchases, Formatting.Indented)}");
        }

        // Alternative: Using CatalogProvider instead of Economy configuration
        private void BuildAndFetchProductsWithCatalog()
        {
            // Load catalog from Assets/Resources/IAPProductCatalog.json
            var catalog = ProductCatalog.LoadDefaultCatalog();
            if (catalog == null || catalog.allProducts == null || catalog.allProducts.Count == 0)
            {
                Debug.LogWarning("[IAP] No products in IAPProductCatalog.json.");
                return;
            }

            var productDefinitions = new List<ProductDefinition>();
            foreach (var item in catalog.allProducts)
            {
                // Convert each catalog item into a ProductDefinition
                productDefinitions.Add(
                    new ProductDefinition(
                        item.id,  // The ID from your IAP Catalog
                        item.type // Consumable / NonConsumable / Subscription
                    )
                );
            }

            // Fetch products from store using Unity IAP Services
            m_StoreController.FetchProducts(productDefinitions);
        }

        /// <summary>
        /// Initializes local receipt validation for supported platforms to verify purchase authenticity.
        /// 
        /// CrossPlatformValidator performs cryptographic validation of purchase receipts using:
        /// - For Google Play: Validates signatures using the obfuscated public key from GooglePlayTangle
        /// - For Apple: In v5, StoreKit 2 handles validation internally, so no validator needed
        /// 
        /// Receipt validation helps prevent:
        /// - Modified/forged receipts from fraudulent purchases
        /// - Receipt replay attacks from other apps or products
        /// - Unauthorized access to premium content
        /// 
        /// Note: This is local validation which provides basic fraud protection. 
        /// Server-side validation (like our Cloud Code approach) is recommended
        /// as local validation can potentially be bypassed by determined attackers.
        /// </summary>
        private void InitializeReceiptValidatorsIfNeeded()
        {
#if !UNITY_EDITOR
        // In v5, Apple receipts are handled by StoreKit2. Keep validator for Google only.
        if (Application.platform == RuntimePlatform.Android)
        {
            try
            {
                // v5 sample ctor for CrossPlatformValidator on Google
                // m_GoogleValidator = new CrossPlatformValidator(GooglePlayTangle.Data(), Application.identifier);
                // Debug.Log("[IAP] Google receipt validator initialized.");
                Debug.LogWarning($"[IAP] Google Tangle validator skipped (Missing Key");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[IAP] Validator init skipped/failed: {e.Message}");
            }
        }
#endif
        }

    #region Product/Purchase fetch callbacks

        private void OnProductsFetched(List<Product> products)
        {
            m_StoreController.FetchPurchases();

            LogProductsFetched(products);
        }

        private void LogProductsFetched(List<Product> products)
        {
            Debug.Log($"[IAP] Products fetched: {products.Count}");
            foreach (var p in products)
            {
                Debug.Log($"[IAP] {p.definition.id} | {p.metadata.localizedTitle} | {p.metadata.localizedPriceString}");
            }
        }

        private void OnProductsFetchFailed(ProductFetchFailed failure)
        {
            Debug.LogError($"[IAP] Product fetch failed: {failure.FailureReason}");
        }

        void OnPurchasesFetched(Orders orders)
        {
            // Process purchases, e.g. check for entitlements from completed orders  
        }

        private void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            Debug.LogError($"[IAP] Purchases fetch failed: {failure.FailureReason}");
        }

        private void OnStoreDisconnected(StoreConnectionFailureDescription desc)
        {
            Debug.LogError($"[IAP] Store disconnected: {desc.Message}");
        }

    #endregion

        // Purchase methods:

        public void PurchaseGold()
        {
            if (m_IsPurchaseInProgress)
            {
                Debug.LogWarning("[IAP] Purchase already in progress.");
                return;
            }
            m_IsPurchaseInProgress = true;
            // In v5, you can purchase by id directly via the controller
            m_StoreController.PurchaseProduct(k_GoldPurchase100Id);
        }

        /// <summary>
        /// Called when the store reports a pending order. Validate, grant via Cloud Code,
        /// then confirm to complete the transaction.
        /// </summary>
        private async void OnPurchasePending(PendingOrder pending)
        {
            try
            {
                Debug.Log($"Full receipt JSON: {pending.Info.Receipt}");

                // v5: products live in the order’s cart (usually 1 item, but don’t assume)
                var firstItem = pending.CartOrdered.Items().FirstOrDefault();
                var pid = firstItem?.Product?.definition?.id;
            
                if (string.IsNullOrEmpty(pid))
                {
                    Debug.LogError("[IAP] Pending order has no product id.");
                    PurchaseFailed?.Invoke("No product id in pending order");
                    return;
                }

                var product = m_StoreController?.GetProductById(pid);
                if (product == null)
                {
                    Debug.LogError($"[IAP] Product not found in controller: {pid}");
                    PurchaseFailed?.Invoke($"Product not found: {pid}");
                    return;
                }

                Debug.Log($"[IAP] Pending purchase: {product.definition.id}");

                var receipt = pending.Info.Receipt;

                // Optional Google validation (Apple handled internally in v5)
                if (!ValidateIfGoogle(receipt))
                {
                    Debug.LogError("[IAP] Google receipt validation failed.");
                    PurchaseFailed?.Invoke("Invalid receipt for " + product.definition.id);
                    return;
                }

                // Cloud Code validation + grant
                PlayerEconomyData updated = await m_Bindings.ProcessRealMoneyPurchase(
                    product.definition.id,
                    receipt,
                    (double)product.metadata.localizedPrice, // Cloud Code bindings don't support decimals
                    product.metadata.isoCurrencyCode);

                if (updated == null)
                {
                    Debug.LogError("[IAP] Cloud Code returned null economy data.");
                    PurchaseFailed?.Invoke("Server processing failed for " + product.definition.id);
                    return;
                }

                m_PlayerEconomyManager.HandleEconomyUpdate(updated);

                // Confirm the order to finalize with the store
                m_StoreController.ConfirmPurchase(pending);

                Debug.Log($"[IAP] Confirmed purchase: {product.definition.id}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[IAP] Error processing pending order: {e.Message}");
                PurchaseFailed?.Invoke("Purchase failed" + e.Message);
            }
        }

        private bool ValidateIfGoogle(string receipt)
        {
            // --- MODIFICAÇÃO TEMPORÁRIA (START) ---
            // Retorna verdadeiro imediatamente para ignorar a validação
            // Isso permite testar o fluxo sem ter gerado o GooglePlayTangle ainda
            Debug.LogWarning("[IAP] FAKE VALIDATION: Ignorando validação local do Google (Modo de Teste)");
            return true;
            // --- MODIFICAÇÃO TEMPORÁRIA (END) ---

            /* <-- COMENTE O CÓDIGO ORIGINAL ABAIXO
            if (m_GoogleValidator == null) return true; // nothing to validate on non-Google

            try
            {
                var result = m_GoogleValidator.Validate(receipt);
                foreach (var r in result)
                    Debug.Log($"[IAP] Receipt OK: {r.productID} @ {r.purchaseDate} | Tx: {r.transactionID}");
                return true;
            }
            catch (IAPSecurityException e)
            {
                Debug.LogError($"[IAP] Receipt invalid: {e.Message}");
                return false;
            }
            */
        }

        private void OnPurchaseConfirmed(Order order)
        {
            m_IsPurchaseInProgress = false;

            if (order is FailedOrder failedOrder)
            {
                Debug.LogWarning($"[IAP] Confirmation failed: {failedOrder.FailureReason}");
                return;
            }

            var purchasedProduct = order.CartOrdered.Items().FirstOrDefault()?.Product;

            Debug.Log($"[IAP] Purchase confirmed: {purchasedProduct?.definition.id} | Tx: {order.Info?.TransactionID}");
            SuccessfullyPurchased?.Invoke($"Purchase confirmed: { purchasedProduct?.definition.id}");
        }

        private void OnPurchaseFailed(FailedOrder failed)
        {
            m_IsPurchaseInProgress = false;

            Debug.LogError($"[IAP] Purchase failed: {failed.FailureReason.ToString()}");
            PurchaseFailed?.Invoke($"Purchase failed: {failed.FailureReason.ToString()}");
        }
    }
}