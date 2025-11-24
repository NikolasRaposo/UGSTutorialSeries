// iOS support package - only include on iOS
#if UNITY_IOS
using Unity.Advertisement.IosSupport;
#endif

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Facebook.Unity;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;

namespace Original
{
    public class LoginManager : MonoBehaviour
    {
        private string m_GooglePlayGamesToken;

        public Action PlayerSignedIn;

        private void Awake()
        {
#if UNITY_IOS
        // Prompts player's consent for tracking on iOS
        ATTrackingStatusBinding.RequestAuthorizationTracking();
#endif

#if UNITY_ANDROID
            PlayGamesPlatform.DebugLogEnabled = true;
            // Initialize PlayGamesPlatform
            PlayGamesPlatform.Activate();
            LoginGooglePlayGames();
#endif
            InitializeFacebook();

            if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                UnitySignInSubscription();
            }
            else
            {
                UnityServices.Initialized += UnitySignInSubscription;
            }
        }

        private void UnitySignInSubscription()
        {
            UnityServices.Initialized -= UnitySignInSubscription;
            PlayerAccountService.Instance.SignedIn += SignInOrLinkWithUnity;
        }

        private async void Start()
        {

#if UNITY_IOS
        await AuthenticatePlayerWithGameCenter();
#endif

            if (!AuthenticationService.Instance.SessionTokenExists)
            {
                Debug.Log("Session Token not found");
                return;
            }


            Debug.Log("Returning player signing in...");
            await SignInAnonymouslyAsync();
        }
    
        public void SignOut()
        {
            Debug.Log("Signing out...");
        
            AuthenticationService.Instance.SignOut();
            PlayerAccountService.Instance.SignOut();
        
            // You may also want to clear the session token here
            ClearSessionToken();
        }

        public void ClearSessionToken()
        {
            Debug.Log("Clearing session token...");

            AuthenticationService.Instance.ClearSessionToken();
        }

        public void DeleteAccount()
        {
            Debug.Log("Deleting account...");

            AuthenticationService.Instance.DeleteAccountAsync();
        }

    #region Anonymous

        public async void StartAnonymousSignIn()
        {
            await SignInAnonymouslyAsync();
        }

        private async Task SignInAnonymouslyAsync()
        {
            try
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("Sign in anonymously succeeded!");

                // Shows how to get the playerID
                Debug.Log($"PlayerID: {AuthenticationService.Instance.PlayerId}");

                PlayerSignedIn.Invoke();
            }
            catch (AuthenticationException ex)
            {
                // Compare error code to AuthenticationErrorCodes
                // Notify the player with the proper error message
                Debug.LogException(ex);
            }
            catch (RequestFailedException ex)
            {
                // Compare error code to CommonErrorCodes
                // Notify the player with the proper error message
                Debug.LogException(ex);
            }
        }
    #endregion Anonymous

    #region Unity Player Accounts

        public async void StartUnitySignInAsync()
        {
            if (PlayerAccountService.Instance.IsSignedIn)
            {
                SignInOrLinkWithUnity();
                return;
            }

            try
            {
                await PlayerAccountService.Instance.StartSignInAsync();
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);
            }
        }

        async void SignInOrLinkWithUnity()
        {
            try
            {
                // 1. Player is not yet authenticated, signing up with Unity
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    Debug.Log("Signing up with Unity Player Account...");
                    await AuthenticationService.Instance.SignInWithUnityAsync(PlayerAccountService.Instance.AccessToken);
                    Debug.Log("Successfully signed up with Unity Player Account");
                    PlayerSignedIn.Invoke();
                    return;
                }

                // 2. Player is authenticated, but does not yet have a Unity ID, so let's link
                if (!HasUnityID())
                {
                    Debug.Log("Linking anonymous account to Unity...");
                    await LinkWithUnityAsync(PlayerAccountService.Instance.AccessToken);
                    Debug.Log("Successfully linked anonymous account!");
                    return;
                }

                // 3. Player has authentication and a Unity ID
                Debug.Log("Player is already signed in to their Unity Player Account");
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);
            }
        }

        private bool HasUnityID()
        {
            return AuthenticationService.Instance.PlayerInfo.GetUnityId() != null;
        }

        private async Task LinkWithUnityAsync(string accessToken)
        {
            try
            {
                await AuthenticationService.Instance.LinkWithUnityAsync(accessToken);
                Debug.Log("Link is successful.");
            }
            catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                // Prompt the player with an error message.
                Debug.LogError("This user is already linked with another account. Log in instead.");
            }

            catch (AuthenticationException ex)
            {
                // Compare error code to AuthenticationErrorCodes
                // Notify the player with the proper error message
                Debug.LogException(ex);
            }
            catch (RequestFailedException ex)
            {
                // Compare error code to CommonErrorCodes
                // Notify the player with the proper error message
                Debug.LogException(ex);
            }
        }

        public async void UnlinkUnity()
        {
            try
            {
                Debug.Log("Unlinking Unity Player Account...");

                await AuthenticationService.Instance.UnlinkUnityAsync();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    #endregion

    #region Facebook

        private void InitializeFacebook()
        {
            if (!FB.IsInitialized)
            {
                // Initialize the Facebook SDK
                FB.Init(InitCallback, OnHideUnity);
            }  
            else
            {
                // Already initialized, signal an app activation App Event
                FB.ActivateApp();
            }
        }

        private void OnHideUnity(bool isGameShown)
        {
            if (!isGameShown)
            {
                // Pause the game - we will need to hide
                Time.timeScale = 0;
            }
            else
            {
                // Resume the game - we're getting focus again
                Time.timeScale = 1;
            }
        }

        private void InitCallback()
        {
            if (FB.IsInitialized)
            {
                // Signal an app activation App Event
                FB.ActivateApp();
                // Continue with Facebook SDK
                // ...
            }
            else
            {
                Debug.Log("Failed to Initialize the Facebook SDK");
            }
        }

        public void StartFacebookSignIn()
        {
#if UNITY_IOS
        // On iOS, use the specialized method that handles ATT
        SignInWithAttHandling();
#else
            var perms = new List<string>() { "public_profile", "email" };
            FB.LogInWithReadPermissions(perms, async result =>
            {
                if (FB.IsLoggedIn)
                {
                    // AccessToken class will have session details
                    var facebookAccessToken = Facebook.Unity.AccessToken.CurrentAccessToken.TokenString;

                    if (!AuthenticationService.Instance.IsSignedIn)
                    {
                        await SignInWithFacebookAsync(facebookAccessToken);
                    }
                    else
                    {
                        await LinkWithFacebookAsync(facebookAccessToken);
                    }
                }
                else
                {
                    Debug.Log("User cancelled login");
                }
            });
#endif
        }

        // iOS-specific implementation with ATT handling
#if UNITY_IOS
    public void SignInWithAttHandling()
    {
        FB.LogInWithReadPermissions(null, async result =>
        {
            if (FB.IsLoggedIn)
            {
                // Get the appropriate token based on ATT status
                string accessToken = GetFacebookTokenForIOS(result);
                
                // Handle either sign-in or linking based on current auth state
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await SignInWithFacebookAsync(accessToken);
                    PlayerSignedIn.Invoke();
                }
                else
                {
                    await LinkWithFacebookAsync(accessToken);
                }

            }
            else
            {
                Debug.Log("[Facebook Login] User cancelled login from within Facebook flow");
            }
        });
    }


    private string GetFacebookTokenForIOS(ILoginResult result)
    {
        // Check ATT status to determine which token to use
        // The states include AUTHORIZED, RESTRICTED, NOT_DETERMINED, and DENIED.
        if (ATTrackingStatusBinding.GetAuthorizationTrackingStatus() ==
            ATTrackingStatusBinding.AuthorizationTrackingStatus.AUTHORIZED)
        {
            Debug.Log("[Facebook Login] Using standard access token");
            return AccessToken.CurrentAccessToken.TokenString;
        }
        else
        {
            Debug.Log("[Facebook Login] Using Limited Login authentication token");
            // Make sure authentication token is available
            if (result.AuthenticationToken != null)
            {
                return result.AuthenticationToken.TokenString;
            }
            else
            {
                Debug.LogError("[Facebook Login] Authentication token is null");
                return null;
            }
        }
    }
#endif
     
        /// <summary>
        /// When the player triggers the Facebook login by signing in or by creating a new player profile,
        /// and you have received the Facebook access token, call the following API to authenticate the player
        /// </summary>
        /// <param name="accessToken">The facebook user access token.</param>
        private async Task SignInWithFacebookAsync(string accessToken)
        {
            try
            {
                await AuthenticationService.Instance.SignInWithFacebookAsync(accessToken);
                Debug.Log("Signed in with Facebook!");
                PlayerSignedIn.Invoke();
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);
            }
        }

        private async Task LinkWithFacebookAsync(string accessToken)
        {
            try
            {
                await AuthenticationService.Instance.LinkWithFacebookAsync(accessToken);
                Debug.Log("Linked with Facebook!");
            }
            catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                Debug.LogException(ex);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    
    #endregion

    #region Google Play Games

#if UNITY_ANDROID

        public void LoginGooglePlayGames()
        {
            PlayGamesPlatform.Instance.Authenticate((status) =>
            {
                if (status == SignInStatus.Success)
                {
                    Debug.Log("Login with Google Play games successful.");

                    PlayGamesPlatform.Instance.RequestServerSideAccess(true, code =>
                    {
                        Debug.Log("Authorization code: " + code);
                        m_GooglePlayGamesToken = code;
                        // This token serves as an example to be used for SignInWithGooglePlayGames
                    });
                }
                else
                {
                    Debug.Log($"Google Play Games login unsuccessful, status: {status}");
                }
            });
        }

        public void StartSignInWithGooglePlayGames()
        {
            if (!PlayGamesPlatform.Instance.IsAuthenticated())
            {
                Debug.LogWarning("Not yet authenticated with Google Play Games -- attempting login again");
                LoginGooglePlayGames();
                return;
            }

            SignInOrLinkWithGooglePlayGames();
        }

        private async void SignInOrLinkWithGooglePlayGames()
        {
            if (string.IsNullOrEmpty(m_GooglePlayGamesToken))
            {
                Debug.LogWarning("Authorization code is null or empty!");
                return;
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await SignInWithGooglePlayGamesAsync(m_GooglePlayGamesToken);
            }
            else
            {
                await LinkWithGooglePlayGamesAsync(m_GooglePlayGamesToken);
            }
        }

        private async Task SignInWithGooglePlayGamesAsync(string authCode)
        {
            try
            {
                await AuthenticationService.Instance.SignInWithGooglePlayGamesAsync(authCode);
                Debug.Log("Sign in with Google Play Games is successful.");
                PlayerSignedIn.Invoke();
            }

            catch (AuthenticationException ex)
            {
                // Compare error code to AuthenticationErrorCodes
                // Notify the player with the proper error message
                Debug.LogException(ex);
            }

            catch (RequestFailedException ex)
            {
                // Compare error code to CommonErrorCodes
                // Notify the player with the proper error message
                Debug.LogException(ex);
            }
        }

        private async Task LinkWithGooglePlayGamesAsync(string authCode)
        {
            try
            {
                await AuthenticationService.Instance.LinkWithGooglePlayGamesAsync(authCode);
                Debug.Log("Link is successful.");
            }
            catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                // Prompt the player with an error message.
                Debug.LogWarning("This user is already linked with another account. Log in instead.");
            }

            catch (AuthenticationException ex)
            {
                // Compare error code to AuthenticationErrorCodes
                // Notify the player with the proper error message
                Debug.LogException(ex);
            }
            catch (RequestFailedException ex)
            {
                // Compare error code to CommonErrorCodes
                // Notify the player with the proper error message
                Debug.LogException(ex);
            }
        }
#endif
    #endregion

    #region Apple Game Center
#if UNITY_IOS
    private string m_Signature;
    private string m_TeamPlayerID;
    private string m_Salt;
    private string m_PublicKeyUrl;
    private ulong m_Timestamp;

    // Retrieve user credentials
    public async Task AuthenticatePlayerWithGameCenter()
    {
        if (!GKLocalPlayer.Local.IsAuthenticated)
        {
            // Perform the authentication.
            var player = await GKLocalPlayer.Authenticate();
            Debug.Log($"GameKit Authentication: player {player}");

            // Grab the display name.
            var localPlayer = GKLocalPlayer.Local;
            Debug.Log($"Local Player: {localPlayer.DisplayName}");

            // Fetch the items.
            var fetchItemsResponse = await GKLocalPlayer.Local.FetchItems();

            m_Signature = Convert.ToBase64String(fetchItemsResponse.GetSignature());
            m_TeamPlayerID = localPlayer.TeamPlayerId;
            Debug.Log($"Team Player ID: {m_TeamPlayerID}");

            m_Salt = Convert.ToBase64String(fetchItemsResponse.GetSalt());
            m_PublicKeyUrl = fetchItemsResponse.PublicKeyUrl;
            m_Timestamp = fetchItemsResponse.Timestamp;

            Debug.Log($"GameKit Authentication: signature => {m_Signature}");
            Debug.Log($"GameKit Authentication: publickeyurl => {m_PublicKeyUrl}");
            Debug.Log($"GameKit Authentication: salt => {m_Salt}");
            Debug.Log($"GameKit Authentication: Timestamp => {m_Timestamp}");
        }
        else
        {
            Debug.Log("AppleGameCenter player already logged in.");
        }
    }


    public void StartSignInWithAppleGameCenter()
    {
        SignInOrLinkWithAppleGameCenter();
    }

    private async void SignInOrLinkWithAppleGameCenter()
    {
        // Note:
        // Check string.IsNullOrEmpty(AuthenticationService.Instance.PlayerInfo.GetAppleGameCenterId())
        // if you want to unlink account

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await SignInWithAppleGameCenterAsync(m_Signature, m_TeamPlayerID, m_PublicKeyUrl, m_Salt, m_Timestamp);
        }
        else
        {
            await LinkWithAppleGameCenterAsync(m_Signature, m_TeamPlayerID, m_PublicKeyUrl, m_Salt, m_Timestamp);
        }
    }

    async Task SignInWithAppleGameCenterAsync(string signature, string teamPlayerId, string publicKeyURL, string salt, ulong timestamp)
    {
        try
        {
            await AuthenticationService.Instance.SignInWithAppleGameCenterAsync(signature, teamPlayerId, publicKeyURL, salt, timestamp);
            Debug.Log("SignIn is successful.");
        }
        catch (AuthenticationException ex)
        {
            // Compare error code to AuthenticationErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
        catch (RequestFailedException ex)
        {
            // Compare error code to CommonErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
    }

    async Task LinkWithAppleGameCenterAsync(string signature, string teamPlayerId, string publicKeyURL, string salt, ulong timestamp)
    {
        try
        {
            await AuthenticationService.Instance.LinkWithAppleGameCenterAsync(signature, teamPlayerId, publicKeyURL, salt, timestamp);
            Debug.Log("Link is successful.");
        }
        catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
        {
            // Prompt the player with an error message.
            Debug.LogError("This user is already linked with another account. Log in instead.");
        }
        catch (AuthenticationException ex)
        {
            // Compare error code to AuthenticationErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
        catch (RequestFailedException ex)
        {
            // Compare error code to CommonErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
    }
#endif
    #endregion
    
        private void OnDestroy()
        {
            PlayerAccountService.Instance.SignedIn -= SignInOrLinkWithUnity;
        }
    }
}
