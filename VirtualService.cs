using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Rinha2024.Dotnet.IO;

namespace Rinha2024.Dotnet;

public class VirtualService
{
    private static readonly int WPort = int.TryParse(Environment.GetEnvironmentVariable("W_BASE_PORT"), out var basePort) ? basePort : 10000;
    private static readonly int RPort = int.TryParse(Environment.GetEnvironmentVariable("R_BASE_PORT"), out var basePort) ? basePort : 15000;
    private static readonly int ListenerNum = int.TryParse(Environment.GetEnvironmentVariable("LISTENERS"), out var listeners) ? listeners : 20;

    private readonly string _ipAddress;

    private readonly PortNavigator _wNavigator;
    private readonly PortNavigator _rNavigator;

    public VirtualService()
    {
        var host = Environment.GetEnvironmentVariable("VIRTUAL_DB") ?? "localhost";
        var entries = Dns.GetHostAddresses(host);
        _ipAddress = entries[0].ToString();
        var wPorts = new int[ListenerNum];
        var rPorts = new int[ListenerNum];
        for (var i = 0; i < ListenerNum; i++)
        {
            wPorts[i] = WPort + i;
            rPorts[i] = RPort + i;
        }
        _wNavigator = new PortNavigator(wPorts);
        _rNavigator = new PortNavigator(rPorts);
    }

    public async Task<(int[], TransactionDto[])> GetClient(int idx)
    {
        using var tcp = new TcpClient(_ipAddress, _rNavigator.GetPort());
        var buffer = await PacketBuilder.WriteMessage([0, idx]);
        await tcp.Client.SendAsync(buffer);
        using var stream = tcp.GetStream();
        return await stream.ReadMessageWithTransactionAsync();
    }

    public async Task<int[]> DoTransaction(int idx, char type, int value, string description)
    {
        using var tcp = new TcpClient(_ipAddress, _wNavigator.GetPort());
        if (type == 'd') value *= -1;
        var buffer = await PacketBuilder.WriteMessage([idx, value], description);
        await tcp.Client.SendAsync(buffer);
        using var stream = tcp.GetStream();
        return await stream.ReadMessageAsync();
    }
    
    private sealed class PortNavigator(int[] ports)
    {
        private int _actual = ports[0];
        private int _next = ports.Length > 1 ? ports[1] : ports[0];
        private int _nxtIdx = ports.Length > 1 ? 1 : 0;
        private readonly int _limit = ports.Last();
        private readonly object _lock = new();

        private void SetNext()
        {
            if (_next == _limit)
            {
                _next = ports[0];
                _nxtIdx = 1;
                return;
            }
            _next = ports[_nxtIdx++];
        }

        public int GetPort()
        {
            lock (_lock)
            {
                var value = _actual;
                _actual = _next;
                SetNext();
                return value;
            }
        }
    }
    
}