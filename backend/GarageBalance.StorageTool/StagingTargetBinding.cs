using System.Net;
using System.Net.NetworkInformation;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.StorageTool;

// The maintenance proof and database must refer to the same confirmed VPS.
public static class StagingTargetBinding
{
    public static bool IsAllowed(string machine, IEnumerable<string> addresses, string? connectionHost, int port, string? database) =>
        machine.Equals("41765", StringComparison.OrdinalIgnoreCase) && addresses.Contains("31.192.110.221", StringComparer.Ordinal) &&
        database == "garagebalance_staging" && port == 5432 &&
        connectionHost is "127.0.0.1" or "::1" or "localhost" or "/var/run/postgresql";

    public static async Task VerifyAsync(GarageBalanceDbContext db, CancellationToken token)
    {
        if (db.Database.GetDbConnection().Database != "garagebalance_staging") return;
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        string[] addresses;
        try
        {
            addresses = NetworkInterface.GetAllNetworkInterfaces().SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Select(item => item.Address.ToString()).ToArray();
        }
        catch { throw Error(); }
        if (!IsAllowed(Environment.MachineName, addresses, connection.Host, connection.Port, connection.Database)) throw Error();
        if (!await IsConnectedServerLocalAsync(db, 5432, token)) throw Error();
    }

    public static async Task<bool> IsConnectedServerLocalAsync(GarageBalanceDbContext db, int expectedPort, CancellationToken token)
    {
        var server = await db.Database.SqlQuery<ServerIdentity>($"""
            SELECT current_setting('port')::int AS "Port", host(inet_server_addr()) AS "Address"
            """).SingleAsync(token);
        return server.Port == expectedPort && IsLocalServerAddress(server.Address);
    }

    public static bool IsLocalServerAddress(string? address) => address is null ||
        IPAddress.TryParse(address, out var ip) && IPAddress.IsLoopback(ip);

    private static AccountingCleanupException Error() => new("cleanup_staging_target_mismatch", "Целевой сервис и локальная база GarageBalance staging не подтверждены.");
    private sealed class ServerIdentity
    {
        public int Port { get; set; }
        public string? Address { get; set; }
    }
}
