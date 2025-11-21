using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.GeneratedBindings;
using Unity.Services.CloudCode.GeneratedBindings.UGSTutorialCloud;

namespace PlayerLogin
{
    public class PlayerDataManager: MonoBehaviour
    {
        public LoginManager LoginManager;
        [SerializeField] private PlayerEconomyManager m_PlayerEconomyManager;
        public string PlayerName;
        public PlayerData PlayerDataLocal;
        private PlayerDataServiceBindings m_Bindings;

        public event Action<PlayerData> PlayerDataUpdated;
        void Start()
        {
            LoginManager.PlayerSignedIn += InitializePlayer;
            m_Bindings = new PlayerDataServiceBindings(CloudCodeService.Instance);
        }
        private async void InitializePlayer()
        {
            try
            {
                var playerDataResponse = await m_Bindings.HandlePlayerSignIn();
                PlayerDataLocal = playerDataResponse.PlayerData;
                PlayerDataUpdated?.Invoke(PlayerDataLocal);
                
                m_PlayerEconomyManager.HandleEconomyUpdate(playerDataResponse.EconomyData);
                
                LogResponse(playerDataResponse);
            }
            
            catch (CloudCodeException ex)
            {
                Debug.LogException(ex);
            }
        }

        private void LogResponse(PlayerDataResponse response)
        {
            string economyJson = JsonConvert.SerializeObject(response.EconomyData, Formatting.Indented);
            
            Debug.Log(
                $"===== Jogador Inscrito Resposta =====\n" +
                $"Nome: {response.PlayerData.DisplayName}\n" +
                $"Novo Jogador: {response.IsNewPlayer}\n" +
                $"XP: {response.PlayerData.Experience}\n" +
                $"Economia: {economyJson}\n" +
                $"===================================="
                );
        }

        public async void SaveNewPlayerName()
        {
            if (!IsPlayerNameValid(PlayerName))
            {
                Debug.LogWarning("Name must be 4-16 characters and contain only letters and numbers");
                return;
            }
            try
            {
                PlayerName = await m_Bindings.HandleNewPlayerNameEntry(PlayerName);
                Debug.Log($"Novo nome do jogador salvo na nuvem: {PlayerName}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private bool IsPlayerNameValid(string name)
        {
            if (name.Length is < 4 or > 16)
            {
                return false;
            }
            // Verifica se existe caracteres especiais usando LINQ
            if (!name.All(c => char.IsLetterOrDigit(c)))
            {
                return false;
            }
            return true;
        }
        private void OnDisable()
        {
            LoginManager.PlayerSignedIn -= InitializePlayer;
        }
    }
}
