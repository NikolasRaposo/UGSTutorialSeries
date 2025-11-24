using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Debugging;
using Facebook.Unity;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Manages the authentication flow for the game, including Anonymous, Unity Player Account, and Facebook logins.
/// </summary>
public class LoginManager : DebuggableMonoBehaviour
{
    /// <summary>
    /// Event triggered when the player successfully signs in via any method.
    /// </summary>
    public Action PlayerSignedIn;

    /// <summary>
    /// Initializes the singleton logic, sets up Facebook SDK, and subscribes to Unity Services initialization events.
    /// </summary>
    protected override void Awake()
    {
        base.Awake(); // Initializes the custom logger

        InitializeFacebook();

        // Subscribe to Unity Services initialization to setup sign-in listeners
        if (UnityServices.State == ServicesInitializationState.Initialized)
        {
            UnitySignInSubscription();
        }
        else
        {
            UnityServices.Initialized += UnitySignInSubscription;
        }
    }

    /// <summary>
    /// Subscribes to the PlayerAccountService SignedIn event to handle post-login logic (Unity Accounts).
    /// </summary>
    private void UnitySignInSubscription()
    {
        PlayerAccountService.Instance.SignedIn += SignInOrLinkWithUnity;
    }

    /// <summary>
    /// Entry point that checks for an existing session token to attempt an automatic login without user intervention.
    /// </summary>
    private async void Start()
    {
        try
        {
            // Check if there is a stored session token to attempt auto-login
            if (!AuthenticationService.Instance.SessionTokenExists)
            {
                Log("Session token not found. Waiting for user input.");
                return;
            }

            Log("Session token found. Attempting auto-login...");
            await SignInAnonymouslyAsync();
        }
        catch (Exception e)
        {
            LogError($"Error during auto-login sequence: {e.Message}");
        }
    }

    /// <summary>
    /// Public entry point to start anonymous sign-in (e.g., via UI Button).
    /// </summary>
    public async void StartAnonymousSignIn()
    {
        try
        {
            await SignInAnonymouslyAsync();
        }
        catch (Exception e)
        {
            LogError($"Error during Sign-In Anonymously Async: {e.Message}");
        }
    }

    /// <summary>
    /// Internal logic to perform asynchronous anonymous sign-in.
    /// Checks if the user is already signed in to prevent exceptions and notifies listeners upon success.
    /// </summary>
    private async Task SignInAnonymouslyAsync()
    {
        // Safety Check: Prevent "Invalid state" exception
        if (AuthenticationService.Instance.IsSignedIn)
        {
            LogWarning("Player is already signed in. Skipping anonymous login request.");
            PlayerSignedIn?.Invoke();
            return;
        }

        try
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Log("Anonymous sign-in successful!");

            // Log Player ID for debugging purposes
            Log($"Player ID: {AuthenticationService.Instance.PlayerId}");

            PlayerSignedIn?.Invoke();
        }
        catch (AuthenticationException ex)
        {
            // TODO: Map ErrorCode to a user-friendly UI message
            LogError($"Authentication Error: {ex.Message} (Code: {ex.ErrorCode})");
        }
        catch (RequestFailedException ex)
        {
            // TODO: Map ErrorCode to a user-friendly UI message
            LogError($"Request Failed: {ex.Message} (Code: {ex.ErrorCode})");
        }
    }

    /// <summary>
    /// Initiates the sign-in flow using Unity Player Accounts (Browser/Portal based).
    /// Handles the redirection to the system browser for authentication.
    /// </summary>
    public async void StartUnitySignInAsync()
    {
        try
        {
            // If already signed into Player Account Service, proceed to Auth Service logic
            if (PlayerAccountService.Instance.IsSignedIn)
            {
                SignInOrLinkWithUnity();
                return;
            }

            try
            {
                // Opens the Unity login portal
                await PlayerAccountService.Instance.StartSignInAsync();
            }
            catch (RequestFailedException ex)
            {
                LogError($"Failed to start Unity Player Account sign-in: {ex.Message}");
            }
        }
        catch (Exception e)
        {
            LogError($"Unexpected error in Unity Sign-In: {e.Message}");
        }
    }

    /// <summary>
    /// Decides whether to sign in fresh or link the current anonymous session to the Unity Account.
    /// This logic prevents creating a new account if the user was already playing anonymously.
    /// </summary>
    private async void SignInOrLinkWithUnity()
    {
        try
        {
            // 1. Player is NOT authenticated at all: Sign in using the Unity Token
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Log("Signing in with Unity Player Account...");
                await AuthenticationService.Instance.SignInWithUnityAsync(PlayerAccountService.Instance.AccessToken);
                Log("Successfully signed in with Unity Player Account.");
                return;
            }

            // 2. Player IS authenticated (e.g., Anonymous) but has no Unity ID: Link accounts
            if (!HasUnityID())
            {
                Log("Linking current anonymous session to Unity Account...");
                await LinkWithUnityAsync(PlayerAccountService.Instance.AccessToken);
                Log("Anonymous account successfully linked!");
                return;
            }

            // 3. Player is already authenticated and has a Unity ID
            Log("Player is already fully signed in with a Unity Player Account.");
        }
        catch (RequestFailedException ex)
        {
            LogError($"Unity Sign-in/Link failed: {ex.Message}");
        }
        catch (Exception e)
        {
            LogError($"Unexpected error in SignInOrLinkWithUnity: {e.Message}");
        }
    }

    /// <summary>
    /// Checks if the current player info contains a valid Unity ID.
    /// </summary>
    /// <returns>True if the player has a Unity ID, otherwise False.</returns>
    private static bool HasUnityID()
    {
        return AuthenticationService.Instance.PlayerInfo.GetUnityId() != null;
    }

    /// <summary>
    /// Attempts to link the current session with the provided Unity Access Token.
    /// Handles cases where the account is already linked.
    /// </summary>
    /// <param name="accessToken">The access token received from PlayerAccountService.</param>
    private async Task LinkWithUnityAsync(string accessToken)
    {
        try
        {
            await AuthenticationService.Instance.LinkWithUnityAsync(accessToken);
            Log("Account linking successful.");
        }
        catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
        {
            // This happens if the Unity Account is already linked to ANOTHER player ID.
            LogError("This Unity account is already linked to another player. Please sign in instead.");
        }
        catch (AuthenticationException ex)
        {
            LogError($"Authentication Link Error: {ex.Message}");
        }
        catch (RequestFailedException ex)
        {
            LogError($"Request Link Failed: {ex.Message}");
        }
    }

    #region Facebook Integration
    /// <summary>
    /// Initializes the Facebook SDK if it is not already initialized.
    /// Activates the app if initialization has already occurred.
    /// </summary>
    private void InitializeFacebook()
    {
        if (!FB.IsInitialized)
        {
            // Initialize Facebook SDK
            FB.Init(InitCallback, OnHideUnity);
        }
        else
        {
            // Already initialized, send activation event
            FB.ActivateApp();
        }
    }

    /// <summary>
    /// Callback executed when Facebook SDK initialization completes.
    /// Logs diagnostic information about the App ID and Client Token.
    /// </summary>
    private void InitCallback()
    {
        if (FB.IsInitialized)
        {
            Log($"[DIAGNOSTICS] App ID loaded: '{FB.AppId}'");
            Log($"[DIAGNOSTICS] Client Token loaded: '{FB.ClientToken}'");

            FB.ActivateApp();
        }
        else
        {
            LogError("Failed to initialize Facebook SDK.");
        }
    }

    /// <summary>
    /// Callback used by Facebook SDK to handle game focus state.
    /// Pauses the game when the Facebook overlay is displayed.
    /// </summary>
    /// <param name="isGameShown">True if the game is visible, False if the Facebook overlay is shown.</param>
    private static void OnHideUnity(bool isGameShown)
    {
        Time.timeScale = !isGameShown ? 0 : 1;
    }

    /// <summary>
    /// Starts the Facebook login flow asking for public profile and email permissions.
    /// Decides whether to Sign In or Link based on current authentication state.
    /// </summary>
    public void StartFacebookSignIn()
    {
        var perms = new List<string>() { "public_profile", "email" };

        FB.LogInWithReadPermissions(perms, async result => {
            if (FB.IsLoggedIn)
            {
                // AccessToken class will hold the session details
                var facebookAccessToken = AccessToken.CurrentAccessToken.TokenString;

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
                LogError("Facebook Login failed or incomplete.");

                // Check if cancelled by user
                if (result.Cancelled)
                {
                    LogWarning("User cancelled the Facebook login dialog.");
                }

                // Check for technical errors
                if (!string.IsNullOrEmpty(result.Error))
                {
                    LogError($"Facebook Error: {result.Error}");
                }

                // Deep diagnostic log
                Log($"Raw Facebook Result: {result.RawResult}");
            }
        });
    }

    /// <summary>
    /// Authenticates the user with Unity Authentication using the Facebook access token.
    /// </summary>
    /// <param name="accessToken">The token retrieved from Facebook SDK.</param>
    private async Task SignInWithFacebookAsync(string accessToken)
    {
        try
        {
            await AuthenticationService.Instance.SignInWithFacebookAsync(accessToken);
            Log("Successfully signed in with Facebook!");
            PlayerSignedIn?.Invoke();
        }
        catch (RequestFailedException ex)
        {
            LogError($"Facebook Sign-In Request Failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Links the current anonymous session with the Facebook account.
    /// Handles potential conflict if the Facebook account is already linked elsewhere.
    /// </summary>
    /// <param name="accessToken">The token retrieved from Facebook SDK.</param>
    private async Task LinkWithFacebookAsync(string accessToken)
    {
        try
        {
            await AuthenticationService.Instance.LinkWithFacebookAsync(accessToken);
            Log("Successfully linked with Facebook!");
        }
        catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
        {
            LogError("This Facebook account is already linked to another user.");
        }
        catch (Exception ex)
        {
            LogError($"Error linking Facebook: {ex.Message}");
        }
    }
    #endregion

    /// <summary>
    /// Cleans up event subscriptions when the object is destroyed to prevent memory leaks.
    /// </summary>
    private void OnDestroy()
    {
        PlayerAccountService.Instance.SignedIn -= SignInOrLinkWithUnity;
        UnityServices.Initialized -= UnitySignInSubscription;
    }
}
