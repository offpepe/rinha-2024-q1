﻿using System.Net.Sockets;

namespace Rinha2024.VirtualDb;

public static class PacketReader
{
    public static async Task<int[]> ReadMessageAsync(this NetworkStream stream)
    {
        var receivedBuffer = new byte[8];
        var result = new int[2];
        _ = await stream.ReadAsync(receivedBuffer);
        for (var i = 0; i < 2; i++)
        {
            result[i] = BitConverter.ToInt32(receivedBuffer, i * 4);
        }
        return result;
    }
    
    public static int[] ReadMessage(this NetworkStream stream)
    {
        var receivedBuffer = new byte[8];
        var result = new int[2];
        _ = stream.Read(receivedBuffer);
        for (var i = 0; i < 2; i++)
        {
            result[i] = BitConverter.ToInt32(receivedBuffer, i * 4);
        }
        return result;
    }
}