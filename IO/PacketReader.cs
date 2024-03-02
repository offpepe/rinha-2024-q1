using System.Net.Sockets;
using System.Text;

namespace Rinha2024.Dotnet.IO;

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
    
    
    public static async Task<(int[], TransactionDto[])> ReadMessageWithTransactionAsync(this NetworkStream stream)
    {
        var receivedBuffer = new byte[400];
        var position = 0;
        
        var result = new int[2];
        _ = await stream.ReadAsync(receivedBuffer);
        for (var i = 0; i < 2; i++)
        {
            position = i * 4;
            result[i] = BitConverter.ToInt32(receivedBuffer, position);
        }
        position += 4;
        var size = BitConverter.ToInt32(receivedBuffer, position);
        if (size == 0) return (result, []);
        var transactions = new TransactionDto[size];
        position += 4;
        for (var i = 0; i < size; i++)
        {
            var value = BitConverter.ToInt32(receivedBuffer, position);
            position += 4;
            var type = BitConverter.ToChar(receivedBuffer, position);
            position += 2;
            var descriptionSize = BitConverter.ToInt32(receivedBuffer, position);
            position += 4;
            var builder = new StringBuilder(descriptionSize);
            for (var j = 0; j < descriptionSize; j++)
            {
                builder.Append(BitConverter.ToChar(receivedBuffer, position));
                position += 2;
            }
            var description = builder.ToString().Replace("_", string.Empty);
            var creationDate = DateTime.FromBinary(BitConverter.ToInt64(receivedBuffer, position));
            position += 8;
            transactions[i] = new TransactionDto(value, type, description, creationDate);
        }
        return (result, transactions);
    }
    
}