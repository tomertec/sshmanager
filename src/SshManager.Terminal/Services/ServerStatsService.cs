using System.Globalization;
using Microsoft.Extensions.Logging;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for collecting server resource statistics via SSH commands.
/// </summary>
public class ServerStatsService : IServerStatsService
{
    private readonly ILogger<ServerStatsService> _logger;

    // Combined command to get CPU, memory, disk usage, and uptime in one call (Linux)
    private const string StatsCommand = """
        echo "$(grep 'cpu ' /proc/stat | awk '{usage=($2+$4)*100/($2+$4+$5)} END {print usage}'),$(free | awk '/Mem:/{printf "%.1f", $3/$2*100}'),$(df / | awk 'NR==2{print $5}' | tr -d '%'),$(awk '{print $1}' /proc/uptime)"
        """;

    public ServerStatsService(ILogger<ServerStatsService> logger)
    {
        _logger = logger;
    }

    public async Task<ServerStats> GetStatsAsync(ISshConnection connection, CancellationToken ct = default)
    {
        try
        {
            var output = await connection.RunCommandAsync(StatsCommand, TimeSpan.FromSeconds(3));

            if (string.IsNullOrEmpty(output))
            {
                _logger.LogDebug("Stats command returned no output");
                return new ServerStats(null, null, null, null);
            }

            var parts = output.Split(',');
            if (parts.Length < 4)
            {
                _logger.LogDebug("Stats command returned unexpected format: {Output}", output);
                return new ServerStats(null, null, null, null);
            }

            double? cpu = TryParseDouble(parts[0]);
            double? mem = TryParseDouble(parts[1]);
            double? disk = TryParseDouble(parts[2]);
            TimeSpan? uptime = TryParseUptime(parts[3]);

            _logger.LogDebug("Server stats: CPU={Cpu}%, MEM={Mem}%, DISK={Disk}%, Uptime={Uptime}", cpu, mem, disk, uptime);

            return new ServerStats(cpu, mem, disk, uptime);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get server stats");
            return new ServerStats(null, null, null, null);
        }
    }

    private static double? TryParseDouble(string value)
    {
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return null;
    }

    private static TimeSpan? TryParseUptime(string value)
    {
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }
        return null;
    }
}
