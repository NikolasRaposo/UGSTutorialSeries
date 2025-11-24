using System;
using Newtonsoft.Json;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.GeneratedBindings;
using Unity.Services.LevelPlay;
using UnityEngine;
namespace Original
{
    /// <summary>
    /// Manages rewarded video ads integration with Unity LevelPlay SDK and handles rewards through Cloud Code validation.
    /// </summary>
    public class RewardedAdsManager : MonoBehaviour
    {
        // App Configuration - LevelPlay Dashboard

        private const string k_AndroidAppKey = "245b2ebdd";
        private const string k_AppleAppKey = "";

        // Cooldown between rewarded ad completions: provides UX pacing, ensures another ad is ready if user wants
        private const float k_RewardCooldownSeconds = 3;

        // Dependencies

        [SerializeField]
        private PlayerEconomyManager m_PlayerEconomyManager;
        private AdRewardsServiceBindings m_Bindings;

        // Ad Unit/Placement 

        [SerializeField]
        private string m_AdUnitId = "peg6u3m0bwjvuh36";
        [SerializeField]
        private string m_PlacementName = "Main_Menu";

        // Runtime State

        private bool m_IsInitialized;
        private LevelPlayRewardedAd m_RewardedAd;

        // Anti-cheat tracking

        private string m_LastAdToken;
        private DateTime m_LastAdCompletionTime;

        // Convenience events for UI updates

        public event Action<bool> AdSuccessfullyCompleted;
        public event Action<bool> AdAvailable;

        private void Start()
        {
            RegisterSDKEvents();

            m_Bindings = new AdRewardsServiceBindings(CloudCodeService.Instance);

            // Authentication is required for validating and granting rewards server-side
            AuthenticationService.Instance.SignedIn += InitializeRewardedAds;

            if (AuthenticationService.Instance.IsSignedIn && !m_IsInitialized)
            {
                InitializeRewardedAds();
            }
        }

    #region SDK Initialization

        private void RegisterSDKEvents()
        {
            LevelPlay.OnInitSuccess += SdkInitializationCompleted;
            LevelPlay.OnInitFailed += SdkInitializationFailed;
        }

        /// <summary>
        /// Initializes the ad SDK with the appropriate platform app key.
        /// </summary>
        private void InitializeRewardedAds()
        {
            string appKey = GetPlatformAppKey();

            string userId = AuthenticationService.Instance.PlayerId;

            LevelPlay.SetMetaData("is_test_suite", "enable");
            LevelPlay.Init(appKey, userId);
            LevelPlay.SetPauseGame(true);
        }

        /// <summary>
        /// Gets the appropriate app key based on the current platform.
        /// </summary>
        /// <returns>The platform-specific app key </returns>
        private string GetPlatformAppKey()
        {
#if UNITY_ANDROID
            return k_AndroidAppKey;
#elif UNITY_IPHONE
        return k_AppleAppKey;
#else
        Debug.LogWarning("Unexpected platform for ads");
        return "unexpected_platform";
#endif
        }

        /// <summary>
        /// Callback when the LevelPlay SDK is initialized successfully.
        /// Creates the rewarded ad object, registers to ad events, and loads the first ad.
        /// </summary>
        private void SdkInitializationCompleted(LevelPlayConfiguration configuration)
        {
            if (m_IsInitialized) return;

            m_IsInitialized = true;
            Debug.Log("LevelPlay SDK initialized successfully");

#if DEVELOPMENT_BUILD
        // TODO Remove ValidateIntegration once logs confirm networks are VERIFIED and you have your device's Advertising ID setup as a test device
        // LevelPlay.ValidateIntegration();

        LaunchTestSuite();
        Debug.Log("Launching test suite");
#endif

            CreateRewardedAd();

            // Set listeners before loading rewarded ad
            // Each rewarded ad should have its own listener implementation
            RegisterToAdEvents();

            LoadRewardedAd();
        }

        /// <summary>
        /// Opens the test suite for debugging ad integration.
        /// Only used in editor or development builds.
        /// </summary>
        private void LaunchTestSuite()
        {
            LevelPlay.LaunchTestSuite();
        }

        /// <summary>
        /// Callback when the LevelPlay SDK initialization fails.
        /// </summary>
        private void SdkInitializationFailed(LevelPlayInitError error)
        {
            Debug.LogError($"LevelPlay SDK initialization failed: {error.ErrorMessage}");
        }

    #endregion

    #region Ad Management

        private void CreateRewardedAd()
        {
            m_RewardedAd = new LevelPlayRewardedAd(m_AdUnitId);
        }

        /// <summary>
        /// Registers to all the rewarded ad events.
        /// </summary>
        private void RegisterToAdEvents()
        {
            // Load events
            m_RewardedAd.OnAdLoaded += HandleAdLoadedSuccessfully;
            m_RewardedAd.OnAdLoadFailed += HandleAdLoadFailed;

            // Display events
            m_RewardedAd.OnAdDisplayed += HandleAdDisplayed;
            m_RewardedAd.OnAdDisplayFailed += HandleAdFailedToDisplay;

            // Reward event
            m_RewardedAd.OnAdRewarded += ProcessAdReward;

            // Completion events
            m_RewardedAd.OnAdClosed += HandleAdClosed;
        
            // Optional
            m_RewardedAd.OnAdClicked += HandleAdClicked;
            m_RewardedAd.OnAdInfoChanged += HandleAdInfoChanged;
        }

        private void LoadRewardedAd()
        {
            if (m_RewardedAd != null)
            {
                // When we call LoadAd(), we're saying "try the whole waterfall"
                m_RewardedAd.LoadAd();
            }
        }

        /// <summary>
        /// Shows a rewarded video ad with the specified placement name.
        /// </summary>
        private void ShowRewardedAd()
        {
            if (string.IsNullOrEmpty(m_PlacementName))
            {
                Debug.LogWarning("Placement name is empty, showing ad unit without placement");
                m_RewardedAd.ShowAd();
            }
            else
            {
                m_RewardedAd.ShowAd(m_PlacementName);
            }
        }

    #endregion

    #region Public Interface
        /// <summary>
        /// User-facing method to show a rewarded ad when a button is clicked.
        /// Checks availability before showing the ad.
        /// </summary>
        public void ClickShowAdReward()
        {
            if (CanShowAd())
            {
                ShowRewardedAd();
            }
            else
            {
                Debug.LogWarning($"Cannot show ad.");
            }
        }

        /// <summary>
        /// Checks if a rewarded ad can currently be shown.
        /// </summary>
        /// <returns>True if ad can be shown, false otherwise</returns>
        public bool CanShowAd()
        {
            if (!m_IsInitialized)
            {
                Debug.LogWarning("SDK not initialized");
                return false;
            }

            if (m_RewardedAd == null)
            {
                Debug.LogWarning("Rewarded ad object not created");
                return false;
            }

            bool isAdReady = m_RewardedAd.IsAdReady();
            bool isCooldownExpired = HasCooldownExpired();
            bool isPlacementCapped = LevelPlayRewardedAd.IsPlacementCapped(m_PlacementName);

            if (!isAdReady)
                Debug.LogWarning("Ad not ready - still loading or no inventory available");

            if (isPlacementCapped)
                Debug.LogWarning("Placement has reached its capping limit");

            return isAdReady && isCooldownExpired && !isPlacementCapped;
        }
    #endregion

    #region Helper Methods

        /// <summary>
        /// Gets remaining cooldown time in seconds, or 0 if cooldown expired
        /// </summary>
        public float GetRemainingCooldownSeconds()
        {
            if (m_LastAdCompletionTime == default)
                return 0f;

            TimeSpan timeSinceLastAd = DateTime.UtcNow - m_LastAdCompletionTime;
            float remaining = k_RewardCooldownSeconds - (float)timeSinceLastAd.TotalSeconds;
            return Math.Max(0f, remaining);
        }

        /// <summary>
        /// Simple cooldown unrelated to capping/pacing.
        /// </summary>
        /// <returns>True for cooldown expired, false if still in cooldown</returns>
        private bool HasCooldownExpired()
        {
            float remaining = GetRemainingCooldownSeconds();

            if (remaining > 0f)
            {
                Debug.Log($"Ad still on cooldown for {remaining:F1} seconds");
                return false;
            }

            return true;
        }

    #endregion

    #region Ad Event Callbacks

        // Load events

        private void HandleAdLoadedSuccessfully(LevelPlayAdInfo adInfo)
        {
            AdAvailable?.Invoke(true);
            Debug.Log($"Rewarded ad loaded: {adInfo.AdNetwork}");
        }

        private void HandleAdLoadFailed(LevelPlayAdError error)
        {
            Debug.LogError($"Rewarded ad failed to load: {error.ErrorMessage} (Code: {error.ErrorCode})");

            // Different retry strategies based on actual LevelPlay error codes
            // Note: Some error codes may not apply to format - https://developers.is.com/ironsource-mobile/air/supersonic-sdk-error-codes/
            switch (error.ErrorCode)
            {
                case 509: // Tried waterfall, all networks say "no inventory"
                    Debug.LogWarning("No ads to show, will retry");
                    Invoke(nameof(LoadRewardedAd), 5f); // Longer delay for no inventory
                    break;

                case 520:
                    Debug.LogWarning("No internet connection");
                    AdAvailable?.Invoke(false);
                    // Could implement connectivity-based retry logic here
                    break;

                case 524:
                    Debug.LogWarning("Placement is capped, will not retry loading");
                    AdAvailable?.Invoke(false);
                    // Don't retry - placement is capped
                    break;

                case 526:
                    Debug.LogWarning("Ad unit has reached daily cap, will not retry");
                    AdAvailable?.Invoke(false);
                    // Don't retry - daily cap reached
                    break;

                // 1022: Cannot show an Rewarded Video (RV) while another RV is showing
                // 1023: Show RV called when there are no available ads to show, check IsAdReady before calling ShowAd

                default:
                    Debug.LogWarning($"Unknown error code {error.ErrorCode}, retrying with standard delay");
                    Invoke(nameof(LoadRewardedAd), 2f); // Standard retry
                    break;
            }
        }

        // Display events

        private void HandleAdDisplayed(LevelPlayAdInfo adInfo)
        {
            Debug.Log("Rewarded ad displayed");
        }

        private void HandleAdFailedToDisplay(LevelPlayAdInfo adInfo, LevelPlayAdError error)
        {
            Debug.LogWarning($"Rewarded ad failed to display: {error.ErrorMessage} Code: {error.ErrorCode}");
            AdSuccessfullyCompleted?.Invoke(false);

            // Could try loading for next attempt...
            // LoadRewardedAd();
        }

        // Reward Events

        private async void ProcessAdReward(LevelPlayAdInfo adInfo, LevelPlayReward reward)
        {
            try
            {
                Debug.Log($"Validating ad reward: {reward.Name} amount: {reward.Amount}");
            
                // Create a unique token for this ad view
                string adToken = GenerateAdToken(adInfo, reward);
                DateTime completionTime = DateTime.UtcNow;

                // Update tracking variables (for client-side validation if needed)
                m_LastAdToken = adToken;
                m_LastAdCompletionTime = completionTime;

                // Call cloud code to validate and grant reward
                // This prevents client-side reward manipulation
                var playerEconomyData = await m_Bindings.HandleGrantVideoAdReward(adToken);

                m_PlayerEconomyManager.HandleEconomyUpdate(playerEconomyData);

                // TODO: Pass reward info to event for UI display (coins earned popup)
                AdSuccessfullyCompleted?.Invoke(true);

                Debug.Log($"Ad reward granted successfully: {reward.Name} x{reward.Amount}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to validate ad reward: {e.Message}");
                AdSuccessfullyCompleted?.Invoke(false);
            }
        }

        /// <summary>
        /// Generates a unique validation token for this ad completion event.
        /// While not cryptographically secure, this token system prevents simple 
        /// replay attacks and enables server-side validation of ad completion timing.
        /// </summary>
        /// <param name="adInfo">Information about the rewarded ad that was shown</param>
        /// <returns>A unique token string for server validation</returns>
        private string GenerateAdToken(LevelPlayAdInfo adInfo, LevelPlayReward reward)
        {
            // Validate required data is present
            if (adInfo == null)
            {
                throw new ArgumentException("Ad info cannot be null for token generation");
            }

            if (reward == null)
            {
                throw new ArgumentException("Reward info cannot be null for token generation");
            }

            if (string.IsNullOrEmpty(adInfo.InstanceId))
            {
                throw new ArgumentException("Ad instance ID cannot be null or empty for token generation");
            }

            // Create token with ad info, reward data, and timestamp
            var tokenData = new {
                // Store as ticks for consistent serialization
                Timestamp = DateTime.UtcNow.Ticks,
                InstanceId = adInfo.InstanceId,
                InstanceName = adInfo.InstanceName,
                AdNetwork = adInfo.AdNetwork,
                RewardName = "GOLD", // Can use RewardName = reward.Name, but in editor reward is "editor_reward" and will fail validation
                RewardAmount = 100 // Use reward.Amount
            };
            string json = JsonConvert.SerializeObject(tokenData);
            Debug.Log($"Generated ad token: {json}");
            return json;
        }

        // Completion events

        private void HandleAdClosed(LevelPlayAdInfo adInfo)
        {
            Debug.Log("Rewarded ad closed");

            // Load another ad for next time
            LoadRewardedAd();
        }

        private void HandleAdClicked(LevelPlayAdInfo adInfo)
        {
            Debug.Log("Rewarded ad clicked");
        }

        private void HandleAdInfoChanged(LevelPlayAdInfo adInfo)
        {
            Debug.Log($"Updated ad info - Network: {adInfo.AdNetwork}, Instance: {adInfo.InstanceId}");
            // Could trigger UI updates here
        }

    #endregion

        // Cleanup

        private void OnDestroy()
        {
            RemoveEventHandlers();
        }

        private void RemoveEventHandlers()
        {
            AuthenticationService.Instance.SignedIn -= InitializeRewardedAds;

            // Remove SDK level events
            LevelPlay.OnInitSuccess -= SdkInitializationCompleted;
            LevelPlay.OnInitFailed -= SdkInitializationFailed;

            // Remove ad-specific events
            if (m_RewardedAd != null)
            {
                m_RewardedAd.OnAdLoaded -= HandleAdLoadedSuccessfully;
                m_RewardedAd.OnAdLoadFailed -= HandleAdLoadFailed;
                m_RewardedAd.OnAdDisplayed -= HandleAdDisplayed;
                m_RewardedAd.OnAdDisplayFailed -= HandleAdFailedToDisplay;
                m_RewardedAd.OnAdRewarded -= ProcessAdReward;
                m_RewardedAd.OnAdClosed -= HandleAdClosed;
                m_RewardedAd.OnAdClicked -= HandleAdClicked;
                m_RewardedAd.OnAdInfoChanged -= HandleAdInfoChanged;
            }
        }
    }
}


