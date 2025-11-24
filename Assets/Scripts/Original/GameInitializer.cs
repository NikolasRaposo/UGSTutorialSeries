using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;
namespace Original
{
    public class GameInitializer : MonoBehaviour
    {
        private async void Awake()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                Debug.Log("Services Initializing");
                await UnityServices.InitializeAsync();
            }

            AnalyticsService.Instance.StartDataCollection();
        }
    }
}