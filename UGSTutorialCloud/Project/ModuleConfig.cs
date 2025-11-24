using Microsoft.Extensions.DependencyInjection;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace UGSTutorialCloud;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        config.Dependencies.AddSingleton(GameApiClient.Create());

        config.Dependencies.AddSingleton<PlayerEconomyService>();
        config.Dependencies.AddSingleton<PlayerDataService>();
    }
}
