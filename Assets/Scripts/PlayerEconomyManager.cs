using System.Collections.Generic;
using Unity.Services.CloudCode.GeneratedBindings.UGSTutorialCloud;
using UnityEngine;
using System;
using Unity.Services.Authentication;
using Unity.Services.Economy;

public class PlayerEconomyManager : MonoBehaviour
{
    public PlayerEconomyData EconomyDataLocal { get; private set; } = new PlayerEconomyData {
        Currencies = new Dictionary<string, int>(),
        ItemInventory = new Dictionary<string, int>()
    };
    
    public const string k_GoldCurrencyKey = "GOLD";

    public int Gold
    {
        get => GetCurrencyAmount(k_GoldCurrencyKey);
    }
    
    public event Action<PlayerEconomyData> PlayerEconomyUpdated;
    public event Action EconomyConfigSynced;

    private void Start()
    {
        AuthenticationService.Instance.SignedIn += SyncEconomyConfig;
    }
    private async void SyncEconomyConfig()
    {
        try
        {
            await EconomyService.Instance.Configuration.SyncConfigurationAsync();
            Debug.Log("Configuracao do Economy Sincronizado");
            EconomyConfigSynced?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    public void HandleEconomyUpdate(PlayerEconomyData economyData)
    {
        EconomyDataLocal = economyData;
        PlayerEconomyUpdated?.Invoke(EconomyDataLocal);
    }

    private int GetCurrencyAmount(string currencyKey)
    {
        if (EconomyDataLocal.Currencies.TryGetValue(currencyKey, out int amount))
        {
            return amount;
        }
        return 0;
    }

    private void OnDisable()
    {
        AuthenticationService.Instance.SignedIn -= SyncEconomyConfig;
    }

}
