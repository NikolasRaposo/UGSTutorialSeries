using System;
using System.Collections.Generic;
using Core.Debugging;
using Unity.Services.Authentication;
using Unity.Services.CloudCode.GeneratedBindings.UGSTutorialCloud;
using Unity.Services.Economy;

/// <summary>
/// Manages the local representation of the player's economy (Currencies and Inventory).
/// Handles synchronization with Unity Economy Service and updates from Cloud Code.
/// </summary>
public class PlayerEconomyManager : DebuggableMonoBehaviour
{
    /// <summary>
    /// Stores the local cache of the player's economy data.
    /// </summary>
    private PlayerEconomyData EconomyDataLocal
    {
        get;
        set;
    } = new PlayerEconomyData {
        Currencies = new Dictionary<string, int>(),
        ItemInventory = new Dictionary<string, int>()
    };

    /// <summary>
    /// Constant key for the Gold currency ID defined in the Unity Dashboard.
    /// </summary>
    public const string GoldCurrencyKey = "GOLD";

    /// <summary>
    /// Gets the current amount of Gold from the local cache.
    /// </summary>
    public int Gold
    {
        get
        {
            return GetCurrencyAmount(GoldCurrencyKey);
        }
    }

    /// <summary>
    /// Event triggered when the local economy data is updated (e.g., after a purchase or login).
    /// </summary>
    public event Action<PlayerEconomyData> PlayerEconomyUpdated;

    /// <summary>
    /// Event triggered when the economy configuration (definitions) is synced with the server.
    /// </summary>
    public event Action EconomyConfigSynced;

    private void Start()
    {
        // Subscribe to the sign-in event to sync configuration automatically
        AuthenticationService.Instance.SignedIn += SyncEconomyConfig;
    }

    /// <summary>
    /// Synchronizes the Economy Configuration (Currency and Item definitions) from the server.
    /// This is required to know the valid items and costs before accessing the store.
    /// </summary>
    private async void SyncEconomyConfig()
    {
        try
        {
            Log("Syncing economy configuration...");

            await EconomyService.Instance.Configuration.SyncConfigurationAsync();

            Log("Economy configuration synced successfully.");
            EconomyConfigSynced?.Invoke();
        }
        catch (EconomyException ex)
        {
            LogError($"Economy Service Error: {ex.Message} (Code: {ex.ErrorCode})");
        }
        catch (Exception ex)
        {
            LogError($"Unexpected error syncing economy config: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the local economy state with data received from Cloud Code or other sources.
    /// </summary>
    /// <param name="economyData">The new economy data object.</param>
    public void HandleEconomyUpdate(PlayerEconomyData economyData)
    {
        if (economyData == null)
        {
            LogWarning("Attempted to update economy with null data.");
            return;
        }

        EconomyDataLocal = economyData;

        Log($"Local economy updated. Gold: {Gold}");
        PlayerEconomyUpdated?.Invoke(EconomyDataLocal);
    }

    /// <summary>
    /// Helper method to retrieve the balance of a specific currency safely.
    /// </summary>
    /// <param name="currencyKey">The ID of the currency.</param>
    /// <returns>The amount owned, or 0 if not found.</returns>
    private int GetCurrencyAmount(string currencyKey)
    {
        return EconomyDataLocal.Currencies.GetValueOrDefault(currencyKey, 0);
    }

    private void OnDisable()
    {
        AuthenticationService.Instance.SignedIn -= SyncEconomyConfig;
    }
}
