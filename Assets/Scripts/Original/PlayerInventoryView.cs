using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Original
{
    public class PlayerInventoryView : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI m_GoldText;
        [SerializeField]
        private TextMeshProUGUI m_HealthPotionText;
        [SerializeField]
        private Button m_PotionPurchaseButton;
        [SerializeField]
        private Button m_GoldPurchaseButton;

        public void UpdateGold(int gold)
        {
            m_GoldText.text = gold.ToString();
        }

        public void UpdateHealthPotions(int healthPotion)
        {
            m_HealthPotionText.text = healthPotion.ToString();
        }

        public void TogglePurchaseButtons(bool isEnabled)
        {
            m_PotionPurchaseButton.enabled = isEnabled;
            m_GoldPurchaseButton.enabled = isEnabled;

            m_PotionPurchaseButton.interactable = isEnabled;
            m_GoldPurchaseButton.interactable = isEnabled;
        }
    }
}
