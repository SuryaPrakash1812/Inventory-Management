using System.Net.NetworkInformation;

namespace InventoryManagement.Sync.Connectivity;

/// <summary>
/// Combines two signals: the OS's own network-availability events (instant,
/// free, no network call needed) as the primary trigger, and an infrequent
/// backstop ping to the API's health endpoint (network-up does not
/// necessarily mean the API itself is reachable) to catch the cases OS
/// events miss - e.g. the network adapter is up but the API server or DNS
/// is unreachable. The backstop interval is deliberately long (see
/// PingIntervalSeconds) - this is explicitly NOT meant to be a tight
/// polling loop.
/// </summary>
public sealed class ConnectivityService : IConnectivityService, IDisposable
{
    private const int PingIntervalSeconds = 30;

    private readonly HttpClient _httpClient;
    private readonly Timer _backstopTimer;
    private volatile bool _isOnline;

    public event EventHandler<bool>? ConnectivityChanged;

    public bool IsOnline => _isOnline;

    public ConnectivityService(HttpClient httpClient)
    {
        _httpClient = httpClient;

        // Seed an initial guess from the OS immediately, rather than
        // starting "offline" until the first check completes - avoids a
        // brief false-offline flash at startup on a machine that is
        // actually online.
        _isOnline = NetworkInterface.GetIsNetworkAvailable();

        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        _backstopTimer = new Timer(
            _ => _ = CheckNowAsync(),
            state: null,
            dueTime: TimeSpan.FromSeconds(PingIntervalSeconds),
            period: TimeSpan.FromSeconds(PingIntervalSeconds));
    }

    public async Task<bool> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        bool reachable;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await _httpClient.GetAsync("/api/health", cts.Token);
            reachable = response.IsSuccessStatusCode;
        }
        catch
        {
            // Any failure (timeout, DNS, connection refused, TLS, etc.)
            // means "not currently reachable" - the specific reason does
            // not change the routing decision, only whether to attempt an
            // online operation at all.
            reachable = false;
        }

        SetOnline(reachable);
        return reachable;
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable)
        {
            // The OS says the network itself just went away - trust that
            // immediately without waiting for a ping to time out.
            SetOnline(false);
            return;
        }

        // Network adapter coming up does not guarantee the API is actually
        // reachable (DNS, the API process, or PostgreSQL behind it could
        // still be down) - verify with a real check rather than assuming.
        _ = CheckNowAsync();
    }

    private void SetOnline(bool online)
    {
        if (_isOnline == online)
        {
            return;
        }

        _isOnline = online;
        ConnectivityChanged?.Invoke(this, online);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _backstopTimer.Dispose();
    }
}
