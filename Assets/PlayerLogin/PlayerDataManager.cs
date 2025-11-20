using System;
using System.Linq;
using UnityEngine;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.GeneratedBindings;

namespace PlayerLogin
{
    public class PlayerDataManager: MonoBehaviour
    {
        private PlayerDataServiceBindings m_Bindings;
        public LoginManager LoginManager;
        public string PlayerName;

        void Start()
        {
            LoginManager.PlayerSignedIn += InitializePlayer;
            m_Bindings = new PlayerDataServiceBindings(CloudCodeService.Instance);
        }
        private async void InitializePlayer()
        {
            try
            {
                var resultFromCloud = await m_Bindings.SayHello(PlayerName);
                Debug.Log($"{resultFromCloud}");
            }
            
            catch (CloudCodeException ex)
            {
                Debug.LogException(ex);
            }
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
