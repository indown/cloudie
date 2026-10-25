using Windows.Networking.Connectivity;

namespace Cloudie.App.Services;

public static class NetworkStatus
{
    /// <summary>True on a connection Windows marks as metered (mobile data, a capped or roaming plan): no background downloads then.</summary>
    public static bool IsMetered()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null) return false;
            var cost = profile.GetConnectionCost();
            return cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable || cost.Roaming || cost.OverDataLimit || cost.ApproachingDataLimit;
        }
        catch
        {
            return false;
        }
    }
}
