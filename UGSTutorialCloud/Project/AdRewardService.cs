using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace UGSTutorialCloud;

public class ParsedTokenData
{
    // DateTime.Ticks - avoids JSON serialization inconsistencies across libraries
    public long Timestamp { get; init; }
    public string InstanceId { get; init; } = "";
    public string InstanceName { get; init; } = "";
    public string AdNetwork { get; init; } = "";
    public string RewardName { get; init; } = "";
    public int RewardAmount { get; init; }
}

public class AdRewardsService
{
    private const int k_MaxTokenAgeMinutes = 30;
    private const int k_MaxFutureToleranceSeconds = 60;
    private const int k_MaxAdRewardAmount = 1000;
    private const string k_ExpectedAdRewardCurrencyID = "GOLD";
    private const string k_LastAdTokenKey = "LAST_AD_TOKEN";
    private const int k_MinimumAdIntervalSeconds = 10; // Minimum time between ads

    private readonly ILogger<AdRewardsService> m_Logger;
    private readonly PlayerEconomyService m_PlayerEconomyService;
    private readonly PlayerDataService m_PlayerDataService;

    public AdRewardsService(
        ILogger<AdRewardsService> logger,
        PlayerEconomyService playerEconomyService,
        PlayerDataService playerDataService)
    {
        m_Logger = logger;
        m_PlayerEconomyService = playerEconomyService;
        m_PlayerDataService = playerDataService;
    }

    /// <summary>
    /// Handles the granting of rewards when a player watches a video ad.
    /// Validates the ad token format, reward data, timing, and grants the reward.
    /// </summary>
    /// <param name="adToken">Unique token representing this ad view with embedded reward data</param>
    /// <returns>Updated player economy data after the reward is granted</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when validation fails</exception>
    /// <exception cref="InvalidOperationException">Thrown when player economy data cannot be retrieved</exception>
    [CloudCodeFunction("HandleGrantVideoAdReward")]
    public async Task<PlayerEconomyData> HandleGrantVideoAdReward(IExecutionContext context, IGameApiClient gameApiClient, string adToken)
    {
        try
        {
            // Parse the token into structured data
            ParsedTokenData tokenData = ParseToken(adToken);

            // Validate the token data structure and values
            ValidateTokenData(tokenData);

            // Validate token usage (checks against player's history)
            await ValidateTokenUsage(context, gameApiClient, adToken);

            // Grant reward
            await m_PlayerEconomyService.AddCurrency(context, gameApiClient, PlayerEconomyService.k_GoldCurrencyKey, tokenData.RewardAmount);

            // Store the token to prevent reuse
            await StoreLastAdToken(context, gameApiClient, adToken);

            m_Logger.LogInformation($"Successfully granted ad reward: {tokenData.RewardName} x{tokenData.RewardAmount} from {tokenData.AdNetwork}");

            return await m_PlayerEconomyService.GetPlayerEconomyData(context, gameApiClient)
                ?? throw new InvalidOperationException("Failed to get player economy data");
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error granting ad reward for player {PlayerId}", context.PlayerId);
            throw;
        }
    }

    /// <summary>
    /// Parses the ad token into a structured ParsedTokenData object.
    /// Expected format: JSON string containing timestamp (as ticks), instance info, ad network, and reward data
    /// </summary>
    /// <param name="adToken">The ad token to parse</param>
    /// <returns>ParsedTokenData containing the extracted values</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when token format is invalid</exception>
    private ParsedTokenData ParseToken(string adToken)
    {
        if (string.IsNullOrEmpty(adToken))
        {
            throw new UnauthorizedAccessException("Token is null or empty");
        }

        try
        {
            var tokenData = JsonConvert.DeserializeObject<ParsedTokenData>(adToken);

            if (tokenData == null)
            {
                throw new UnauthorizedAccessException("Failed to parse token");
            }

            return tokenData;
        }
        catch (JsonException ex)
        {
            m_Logger.LogError(ex, "Failed to parse token: {Token}", adToken);
            throw new UnauthorizedAccessException("Invalid token format");
        }
    }

    /// <summary>
    /// Validates the parsed token data against business rules.
    /// </summary>
    /// <param name="data">The parsed token data to validate</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when validation fails</exception>
    private void ValidateTokenData(ParsedTokenData data)
    {
        // Convert ticks to DateTime
        DateTime timestamp;
        try
        {
            timestamp = new DateTime(data.Timestamp, DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new UnauthorizedAccessException("Invalid timestamp in token");
        }

        // Validate timestamp age
        DateTime now = DateTime.UtcNow;
        TimeSpan age = now - timestamp;

        if (age.TotalMinutes > k_MaxTokenAgeMinutes)
        {
            // Make exception methods less specific in production, "Token invalid"
            throw new UnauthorizedAccessException("Token is too old");
        }

        if (timestamp > now.AddSeconds(k_MaxFutureToleranceSeconds))
        {
            throw new UnauthorizedAccessException("Token timestamp is too far in the future");
        }

        if (string.IsNullOrEmpty(data.InstanceId))
        {
            throw new UnauthorizedAccessException("Instance ID cannot be empty");
        }

        if (data.RewardName != k_ExpectedAdRewardCurrencyID)
        {
            throw new UnauthorizedAccessException($"Invalid reward name. Expected '{k_ExpectedAdRewardCurrencyID}', got '{data.RewardName}'");
        }

        if (data.RewardAmount <= 0)
        {
            throw new UnauthorizedAccessException($"Invalid reward amount. Must be positive, got {data.RewardAmount}");
        }

        if (data.RewardAmount > k_MaxAdRewardAmount)
        {
            throw new UnauthorizedAccessException($"Invalid reward amount. Expected under {k_MaxAdRewardAmount}, got {data.RewardAmount}");
        }
    }

    /// <summary>
    /// Validates token usage against player's history to prevent abuse.
    /// Checks for duplicate tokens and enforces minimum time between rewards.
    /// </summary>
    /// <param name="context">Execution context</param>
    /// <param name="gameApiClient">Game API client</param>
    /// <param name="adToken">The ad token to validate</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when token has been used or timing is invalid</exception>
    private async Task ValidateTokenUsage(IExecutionContext context, IGameApiClient gameApiClient, string adToken)
    {
        try
        {
            // Try to get the player's last ad token
            var (success, previousTokenObj) = await m_PlayerDataService.TryGetData(context, gameApiClient, k_LastAdTokenKey);

            string? previousToken = null;
            if (success && previousTokenObj != null)
            {
                previousToken = $"{previousTokenObj}";
            }

            // Check for duplicate token usage
            if (HasTokenBeenUsed(adToken, previousToken!))
            {
                throw new UnauthorizedAccessException("Ad token is duplicate of last used. Reward denied.");
            }

            // Validate reward timing
            if (!HasSufficientAdIntervalElapsed(previousToken!))
            {
                throw new UnauthorizedAccessException("Ad rewarded too quickly. Reward denied.");
            }
        }

        catch (Exception ex) when(!(ex is UnauthorizedAccessException))
        {
            // Log the error but deny the reward for safety if there's an issue checking the token
            m_Logger.LogError(ex, "Error validating token usage");
            throw new UnauthorizedAccessException("Unable to validate token usage");
        }
    }

    /// <summary>
    /// Checks if the provided ad token has been used before to prevent duplicate rewards.
    /// </summary>
    /// <param name="adToken">Current ad token to check</param>
    /// <param name="previousToken">Previously stored token (if any)</param>
    /// <returns>True if the token has been used before, false if it's new/unique</returns>
    private bool HasTokenBeenUsed(string adToken, string previousToken)
    {
        if (!string.IsNullOrEmpty(previousToken) && adToken == previousToken)
        {
            m_Logger.LogWarning($"Duplicate ad token detected: {adToken}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Validates that sufficient time has passed since the last ad reward was granted.
    /// </summary>
    /// <param name="previousToken">Previously stored token containing timestamp</param>
    /// <returns>True if enough time has passed since the last reward, false otherwise</returns>
    private bool HasSufficientAdIntervalElapsed(string previousToken)
    {
        if (string.IsNullOrEmpty(previousToken))
        {
            // No Previous token exists - timing is valid
            return true;
        }

        try
        {
            ParsedTokenData previousTokenData = ParseToken(previousToken);

            // Convert ticks to DateTime before calculating time difference
            DateTime previousTimestamp = new DateTime(previousTokenData.Timestamp, DateTimeKind.Utc);
            TimeSpan timeBetweenAds = DateTime.UtcNow - previousTimestamp;

            if (timeBetweenAds.TotalSeconds < k_MinimumAdIntervalSeconds)
            {
                m_Logger.LogWarning("Ad rewarded too quickly. Time since last ad: {TimeBetweenAds} seconds",
                    timeBetweenAds.TotalSeconds);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // If we can't parse the previous token, we can't validate timing; better to allow the reward than block legitimate users
            m_Logger.LogError(ex, "Error parsing previous token for timing validation. Allowing reward");
            return true; // Fail-open strategy
        }
    }

    /// <summary>
    /// Stores the ad token and timestamp for future validation of ad rewards.
    /// </summary>
    /// <param name="adToken">The ad token to store</param>
    private async Task StoreLastAdToken(IExecutionContext context, IGameApiClient gameApiClient, string adToken)
    {
        // Store the token for future validation
        await m_PlayerDataService.SaveData(context, gameApiClient, k_LastAdTokenKey, adToken);
    }
}