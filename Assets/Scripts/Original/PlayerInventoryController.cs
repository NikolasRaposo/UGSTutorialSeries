using Unity.Services.CloudCode.GeneratedBindings.UGSTutorialCloud;
using UnityEngine;
namespace Original
{
    public class PlayerInventoryController : MonoBehaviour
    {
        [SerializeField]
        private PlayerEconomyManager m_PlayerEconomyManager;
        [SerializeField]
        private IAPStoreManagerV5 m_StoreManager;
        [SerializeField]
        private VirtualStoreManager m_VirtualStoreManager;
        [SerializeField]
        private PlayerInventoryView m_PlayerInventoryView;

        private void Start()
        {
            if (m_PlayerEconomyManager == null)
            {
                Debug.LogError("UI Controller needs Economy Manager");
            }

            m_PlayerInventoryView.UpdateGold(m_PlayerEconomyManager.Gold);

            m_PlayerEconomyManager.PlayerEconomyUpdated += UpdateEconomyUI;
            m_PlayerEconomyManager.PlayerEconomyUpdated += ReenableButtons;
        }

        private void UpdateEconomyUI(PlayerEconomyData economyData)
        {
            economyData.Currencies.TryGetValue("GOLD", out int gold);
            m_PlayerInventoryView.UpdateGold(gold);

            economyData.ItemInventory.TryGetValue("HEALTH_POTION", out int healthPotions);
            m_PlayerInventoryView.UpdateHealthPotions(healthPotions);
        }

        private void ReenableButtons(PlayerEconomyData data)
        {
            m_PlayerInventoryView.TogglePurchaseButtons(true);
        }

        private void OnDestroy()
        {
            m_PlayerEconomyManager.PlayerEconomyUpdated -= UpdateEconomyUI;
            m_PlayerEconomyManager.PlayerEconomyUpdated -= ReenableButtons;
        }
    }
}
