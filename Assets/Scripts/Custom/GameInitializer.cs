using System;
using Core.Debugging;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;
namespace Custom
{
    /// <summary>
    /// Acts as the entry point for the game's backend services.
    /// Responsible for initializing Unity Services and configuring Analytics.
    /// </summary>
    public class GameInitializer : DebuggableMonoBehaviour
    {
        [Header("Service Settings")]
        [Tooltip("If set to true, Analytics data collection will start automatically after successful initialization.")]
        [SerializeField] private bool autoStartAnalytics = true;

        /// <summary>
        /// Handles the asynchronous initialization of Unity Services.
        /// </summary>
        protected override async void Awake()
        {
            // Initialize the base class (DebuggableMonoBehaviour) first to setup logging.
            base.Awake();

            try
            {
                Log("Starting game service initialization sequence...");

                // Check if services are not initialized to prevent redundant calls
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    Log("UnityServices status is 'Uninitialized'. Beginning initialization...");

                    // Track time for performance debugging
                    float startTime = Time.realtimeSinceStartup;

                    // Await the initialization of core services
                    await UnityServices.InitializeAsync();

                    float duration = Time.realtimeSinceStartup - startTime;
                    Log($"UnityServices initialized successfully! (Time taken: {duration:F3} seconds)");
                }
                else
                {
                    LogWarning($"UnityServices were already initialized. Current State: {UnityServices.State}");
                }

                // Handle Analytics startup based on Inspector settings
                if (autoStartAnalytics)
                {
                    AnalyticsService.Instance.StartDataCollection();
                    Log($"Analytics collection started. Session ID: {AnalyticsService.Instance.SessionID}");
                }
                else
                {
                    LogWarning("Analytics collection is disabled via Inspector settings.");
                }
            }
            catch (Exception ex)
            {
                // Log the error using the formatted base class method before throwing
                LogError($"CRITICAL FAILURE in GameInitializer: {ex.Message}");
                throw;
            }
        }
    }
}
