using System;
using System.Linq;
using Core.Debugging;
using Newtonsoft.Json;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.GeneratedBindings;
using Unity.Services.CloudCode.GeneratedBindings.UGSTutorialCloud;
using UnityEngine;
// Namespace for the custom debugger

namespace Custom
{
    /// <summary>
    /// Manages player data synchronization with Cloud Code, including player name, initial data fetching, and economy updates.
    /// </summary>
    public class PlayerDataManager : DebuggableMonoBehaviour
    {
        [Header("Dependencies")]
        [Tooltip("Reference to the LoginManager to listen for sign-in events.")]
        public LoginManager loginManager;

        [Tooltip("Reference to the PlayerEconomyManager to update economy data after login.")]
        [SerializeField] private PlayerEconomyManager playerEconomyManager;

        [Header("Player Data")]
        [Tooltip("The current player's display name.")]
        public string playerName;

        // Local cache of the player's data
        private PlayerData _playerDataLocal;

        // Generated bindings to communicate with Cloud Code
        private PlayerDataServiceBindings _bindings;

        /// <summary>
        /// Event triggered when player data is successfully updated from the cloud.
        /// </summary>
        public event Action<PlayerData> PlayerDataUpdated;

        protected override void Awake()
        {
            base.Awake();

            // Safety checks to ensure dependencies are assigned in the Inspector
            if (loginManager == null) LogError("CRITICAL: LoginManager reference is missing!");
            if (playerEconomyManager == null) LogError("CRITICAL: PlayerEconomyManager reference is missing!");
        }

        private void Start()
        {
            if (loginManager != null)
            {
                loginManager.PlayerSignedIn += InitializePlayer;
            }

            // Initialize Cloud Code bindings
            _bindings = new PlayerDataServiceBindings(CloudCodeService.Instance);
        }

        /// <summary>
        /// Fetches player data and economy data from Cloud Code upon successful sign-in.
        /// </summary>
        private async void InitializePlayer()
        {
            try
            {
                Log("Initializing player data from Cloud Code...");

                // Call the Cloud Code function to get player data
                var playerDataResponse = await _bindings.HandlePlayerSignIn();

                if (playerDataResponse == null)
                {
                    LogError("Cloud Code returned a null response.");
                    return;
                }

                // Update local data and notify listeners
                _playerDataLocal = playerDataResponse.PlayerData;
                PlayerDataUpdated?.Invoke(_playerDataLocal);

                // Update Economy
                if (playerEconomyManager != null)
                {
                    playerEconomyManager.HandleEconomyUpdate(playerDataResponse.EconomyData);
                }
                else
                {
                    LogWarning("Skipping Economy Update: PlayerEconomyManager reference is missing.");
                }

                LogResponse(playerDataResponse);
            }
            catch (CloudCodeException ex)
            {
                LogError($"Cloud Code Error during initialization: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error initializing player: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs the detailed response from the player sign-in Cloud Code function.
        /// </summary>
        /// <param name="response">The data response object.</param>
        private void LogResponse(PlayerDataResponse response)
        {
            var economyJson = JsonConvert.SerializeObject(response.EconomyData, Formatting.Indented);

            Log(
                $"===== Player Signed In Response =====\n" +
                $"Name: {response.PlayerData.DisplayName}\n" +
                $"Is New Player: {response.IsNewPlayer}\n" +
                $"XP: {response.PlayerData.Experience}\n" +
                $"Economy Snapshot: {economyJson}\n" +
                $"===================================="
            );
        }

        /// <summary>
        /// Validates and sends a request to Cloud Code to update the player's display name.
        /// </summary>
        public async void SaveNewPlayerName()
        {
            try
            {
                if (!IsPlayerNameValid(playerName))
                {
                    LogWarning($"Invalid name attempt: '{playerName}'. Name must be 4-16 alphanumeric characters.");
                    return;
                }

                Log($"Attempting to save new name: {playerName}...");

                playerName = await _bindings.HandleNewPlayerNameEntry(playerName);

                Log($"SUCCESS: New player name saved to cloud: {playerName}");
            }
            catch (CloudCodeException ex)
            {
                LogError($"Cloud Code failed to save name: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error saving name: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates the player name format locally before sending to the server.
        /// </summary>
        /// <param name="name">The name to validate.</param>
        /// <returns>True if valid (4-16 chars, alphanumeric), otherwise false.</returns>
        private static bool IsPlayerNameValid(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            return name.Length is >= 4 and <= 16 &&
                name.All(char.IsLetterOrDigit);
        }

        private void OnDisable()
        {
            if (loginManager != null)
            {
                loginManager.PlayerSignedIn -= InitializePlayer;
            }
        }
    }
}
