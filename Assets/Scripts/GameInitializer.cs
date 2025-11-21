using System;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;
namespace PlayerLogin
{
    public class GameInitializer : MonoBehaviour
    {
        private async void Awake()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                Debug.Log("Inicializando Servicos...");
                await UnityServices.InitializeAsync();
            }
            
            AnalyticsService.Instance.StartDataCollection();
            
        }
    }
}
