using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace UGSTutorialCloud;

public class PlayerDataService
{
    public const string k_PlayerDataKey = "PLAYER_DATA";
    public const string k_PlayerNameKey = "PLAYER_NAME";

    private PlayerEconomyService m_PlayerEconomyService;

    private static ILogger<PlayerDataService>? m_Logger;

    public PlayerDataService(ILogger<PlayerDataService> logger, PlayerEconomyService playerEconomy)
    {
        m_Logger = logger;
        m_PlayerEconomyService = playerEconomy;
    }

    #region Cloud Code Functions

    [CloudCodeFunction("HandlePlayerSignIn")]
    public async Task<PlayerDataResponse> HandlePlayerSignIn(IExecutionContext context, IGameApiClient gameApiClient)
    {
        var (playerExists, playerData) = await TryGetPlayerData(context, gameApiClient);

        if (!playerExists || playerData == null)
        {
            // New player!
            return await InitializeNewPlayer(context, gameApiClient);
        }

        // Returning player
        PlayerEconomyData economyData = await m_PlayerEconomyService.GetPlayerEconomyData(context, gameApiClient);

        return new PlayerDataResponse
        {
            PlayerData = playerData,
            EconomyData = economyData,
            IsNewPlayer = false,
        };
    }

    [CloudCodeFunction("SayHello")]
    public string Hello(string name)
    {
        return $"Hello, {name}!";
    }

    [CloudCodeFunction("HandleNewPlayerNameEntry")]
    public async Task<string> HandleNewPlayerNameEntry(IExecutionContext context, IGameApiClient gameApiClient, string newName)
    {
        if (!IsPlayerNameValid(newName))
        {
            throw new ArgumentException("Name is not valid");
        }

        if (IsPlayerNameValid(newName))
        {
            PlayerData? playerData = await GetPlayerDataOrThrow(context, gameApiClient);

            if (playerData == null)
            {
                throw new InvalidOperationException($"Player data not found for player: {context.PlayerId}");
            }

            playerData.DisplayName = newName;
            await SaveData(context, gameApiClient, k_PlayerDataKey, playerData);

            return newName;
        }

        throw new ArgumentException("Name is not valid");
    }

    #endregion

    #region Player Data Initialization and Management

    private async Task<PlayerDataResponse> InitializeNewPlayer(IExecutionContext context, IGameApiClient gameApiClient)
    {
        PlayerData newPlayerData = new PlayerData
        {
            DisplayName = "New Player",
            Experience = 0,
        };

        PlayerEconomyData newEconomyData;

        try
        {
            // Save new player data
            await SaveData(context, gameApiClient, k_PlayerDataKey, newPlayerData);

            // Initialize new player inventory
            newEconomyData = await m_PlayerEconomyService.InitializeNewPlayerEconomy(context, gameApiClient);

            m_Logger!.LogInformation($"New player initialized: {context.PlayerId}");
        }
        catch (Exception ex)
        {
            m_Logger!.LogError(ex, $"Failed to initialize new player: {context.PlayerId}");
            throw new Exception("Failed to initialize new player", ex);
        }

        return new PlayerDataResponse
        {
            PlayerData = newPlayerData,
            EconomyData = newEconomyData,
            IsNewPlayer = true
        };
    }

    private async Task<PlayerData> GetPlayerDataOrThrow(IExecutionContext context, IGameApiClient gameApiClient)
    {
        var (exists, playerData) = await TryGetPlayerData(context, gameApiClient);

        if (!exists || playerData == null)
        {
            throw new InvalidOperationException($"Player data not found for player: {context.PlayerId}");
        }

        return playerData;
    }

    private async Task<(bool playerExists, PlayerData? playerData)> TryGetPlayerData(IExecutionContext context, IGameApiClient gameApiClient)
    {
        try
        {
            // OLD - throws exception on missing data
            // var playerDataObject = await GetData(context, gameApiClient, k_PlayerDataKey);

            // NEW - gracefully handles missing data  
            var (success, playerDataJson) = await TryGetData(context, gameApiClient, k_PlayerDataKey);

            if (playerDataJson == null)
            {
                return (false, null);
            }

            var playerData = JsonConvert.DeserializeObject<PlayerData>($"{playerDataJson}");
            return (playerData != null, playerData);
        }
        catch (Exception ex)
        {
            m_Logger!.LogError(ex, $"Error deserializing player data for player: {context.PlayerId}");
            return (false, null);
        }
    }

    private bool IsPlayerNameValid(string name)
    {
        if (name.Length is < 4 or > 16)
        {
            return false;
        }

        // Check for special characters using LINQ
        if (!name.All(c => char.IsLetterOrDigit(c)))
        {
            return false;
        }

        return true;
    }

    #endregion

    #region Cloud Save Data Access

    public async Task SaveData(IExecutionContext context, IGameApiClient gameApiClient, string key, object value)
    {
        try
        {
            await gameApiClient.CloudSaveData.SetItemAsync(
                context, 
                context.AccessToken, 
                context.ProjectId,
                context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"), 
                new SetItemBody(key, value));
        }
        catch (ApiException ex)
        {
            m_Logger!.LogError("Failed to save data. Error: {Error}", ex.Message);
            throw new Exception($"Failed to save data for playerId {context.PlayerId}. Error: {ex.Message}");
        }
    }

    public async Task SaveData(IExecutionContext context, IGameApiClient gameApiClient, string key, string value)
    {
        try
        {
            await gameApiClient.CloudSaveData.SetItemAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"),
                new SetItemBody(key, value));
        }
        catch (ApiException ex)
        {
            m_Logger!.LogError("Failed to save data. Error: {Error}", ex.Message);
            throw new Exception($"Failed to save data for playerId {context.PlayerId}. Error: {ex.Message}");
        }
    }

    public async Task<object> GetData(IExecutionContext context, IGameApiClient gameApiClient, string key)
    {
        try
        {
            var result = await gameApiClient.CloudSaveData.GetItemsAsync(
                context, 
                context.AccessToken,
                context.ProjectId, 
                context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"), 
                new List<string> { key });

            // Possible fix for no data:
            // if (result.Data.Results.Count == 0) return null;

            return result.Data.Results.First().Value;
        }
        catch (ApiException ex)
        {
            m_Logger!.LogError("Failed to get data. Error: {Error}", ex.Message);
            throw new Exception($"Failed to get data for playerId '{context.PlayerId}'. Error: {ex.Message}");
        }
    }
    
    // Note: Below for single key
    // Example: var (success, token) = await playerDataService.TryGetData(context, gameApiClient, "LAST_AD_TOKEN");
    
    /// <summary>
    /// Retrieves a single value from Cloud Save.
    /// </summary>
    /// <param name="context">Execution context</param>
    /// <param name="gameApiClient">Game API client</param>
    /// <param name="key">The key to retrieve</param>
    /// <returns>Success status and the retrieved value as string</returns>
    public async Task<(bool success, string? value)> TryGetData(IExecutionContext context, IGameApiClient gameApiClient, string key)
    {
        try
        {
            var response = await gameApiClient.CloudSaveData.GetItemsAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"),
                new List<string> { key });

            var retrievedItem = response.Data.Results.FirstOrDefault();
            if (retrievedItem != null)
            {
                return (true, Convert.ToString(retrievedItem.Value));
            }

            return (false, null);
        }
        catch (Exception ex)
        {
            m_Logger!.LogError(ex, "Error retrieving data from CloudSave for player {PlayerId}", context.PlayerId);
            return (false, null);
        }
    }
    
    // Note: TryGetData for multiple keys (more efficient)
    // Example: var (success, data) = await playerDataService.TryGetData(context, gameApiClient, 
    // new[] { "CURRENT_LEVEL", "HIGH_SCORE", "HIGH_SCORE_REWARD" });
    
    /// <summary>
    /// Retrieves multiple values from Cloud Save in a single call.
    /// This is more efficient than multiple single-key calls.
    /// </summary>
    /// <param name="context">Execution context</param>
    /// <param name="gameApiClient">Game API client</param>
    /// <param name="keys">The keys to retrieve</param>
    /// <returns>Success status and dictionary of retrieved key-value pairs</returns>
    public async Task<(bool success, Dictionary<string, string?> data)> TryGetData(
        IExecutionContext context, IGameApiClient gameApiClient, List<string> keys)
    {
        try
        {
            var response = await gameApiClient.CloudSaveData.GetItemsAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId ?? throw new InvalidOperationException("PlayerId is null"),
                keys);

            var retrievedData = new Dictionary<string, string?>();
            foreach (var item in response.Data.Results)
            {
                retrievedData[item.Key] = Convert.ToString(item.Value);
            }

            return (true, retrievedData);
        }
        catch (Exception ex)
        {
            m_Logger!.LogError(ex, "Error retrieving batch data from CloudSave for player {PlayerId}, keys: {Keys}", 
                context.PlayerId, string.Join(", ", keys));
            return (false, new Dictionary<string, string?>());
        }
    }
    #endregion
}