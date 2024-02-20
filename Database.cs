using System.Data;
using System.Threading.Channels;
using Npgsql;

namespace Rinha2024.Dotnet;

public class Database(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private bool _isDisposed;

    private static readonly int ReadPoolSize = int.TryParse(Environment.GetEnvironmentVariable("READ_POOL_SIZE"), out var value) ? value : 1500;
    private static readonly int WritePoolSize = int.TryParse(Environment.GetEnvironmentVariable("WRITE_POOL_SIZE"), out var value) ? value : 3000;

    private readonly CommandPool _readCommandPool =
        new(
            CreateCommands(
                new NpgsqlCommand(QUERY_TRANSACTIONS)
                {
                    Parameters = {new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer}},
                    Connection = dataSource.OpenConnection(),
                },
                ReadPoolSize, dataSource),
            ReadPoolSize);
    
    private readonly CommandPool _debitCommandPool =
        new(CreateCommands(new NpgsqlCommand("CREATE_TRANSACTION_DEBIT")
        {
            CommandType = CommandType.StoredProcedure,
            Connection = dataSource.OpenConnection(),
            Parameters =
            {
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer},
                new NpgsqlParameter<char> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char},
                new NpgsqlParameter<string> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Direction = ParameterDirection.Output},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Direction = ParameterDirection.Output},
            }
        }, WritePoolSize, dataSource), WritePoolSize);
    private readonly CommandPool _creditCommandPool =
        new(CreateCommands(new NpgsqlCommand("CREATE_TRANSACTION_CREDIT")
        {
            CommandType = CommandType.StoredProcedure,
            Connection = dataSource.OpenConnection(),
            Parameters =
            {
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer},
                new NpgsqlParameter<char> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char},
                new NpgsqlParameter<string> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Direction = ParameterDirection.Output},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Direction = ParameterDirection.Output},
            }
        }, WritePoolSize, dataSource), WritePoolSize);

    public async Task<int[]?> DoTransaction(int id, CreateTransactionDto dto)
    {
        await using var poolItem = dto.Tipo == 'd' ? await _debitCommandPool.RentAsync() : await _creditCommandPool.RentAsync();
        var cmd = poolItem.Value;
        cmd.Parameters[0].Value = id;
        cmd.Parameters[1].Value = dto.Valor;
        cmd.Parameters[2].Value = dto.Tipo;
        cmd.Parameters[3].Value = dto.Descricao;
        await cmd.ExecuteNonQueryAsync();
        var balance = (int) (cmd.Parameters[4].Value ?? 0);
        var limit = (int) (cmd.Parameters[5].Value ?? 0); 
        if (limit == 0) return null;
        return [balance, limit];
    }
    
    public async Task<ExtractDto?> GetExtract(int id)
    {
        await using var poolItem = await _readCommandPool.RentAsync();
        var cmd = poolItem.Value;
        cmd.Parameters[0].Value = id;
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!reader.HasRows) return null;
        await reader.ReadAsync();
        var balance = new SaldoDto(reader.GetInt32(4), reader.GetInt32(5));
        var hasTransactions = await reader.IsDBNullAsync(0);
        if (hasTransactions) return new ExtractDto(balance, []);
        var transactions = new List<TransactionDto>()
        {
            new()
            {
                valor = reader.GetInt32(0),
                tipo = reader.GetChar(1),
                descricao = reader.GetString(2),
                realizada_em = reader.GetString(3)
            }
        };
        while (await reader.ReadAsync())
        {
            transactions.Add(new TransactionDto()
            {
                valor = reader.GetInt32(0),
                tipo = reader.GetChar(1),
                descricao = reader.GetString(2),
                realizada_em = reader.GetString(3)
            });
        }

        return new ExtractDto(balance, transactions);
    }

    public async Task Stretching()
    {
        for (var i = 0; i < 50; i++)
        {
            await GetExtract(1);
            await DoTransaction(1, new CreateTransactionDto(1000, 'c', "blablabla"));
            await DoTransaction(1, new CreateTransactionDto(1000, 'd', "blablabla"));
        }
        await using var cmd = new NpgsqlCommand(RESET_DB, await dataSource.OpenConnectionAsync());
        await cmd.ExecuteNonQueryAsync();
    }
    

    private static IEnumerable<NpgsqlCommand> CreateCommands(NpgsqlCommand cmd, int qtd, NpgsqlDataSource dataSource)
    {
        for (var i = 0; i < qtd; i++)
        {
            var clone = cmd.Clone();
            clone.Connection = dataSource.OpenConnection();
            yield return clone;
        }

        yield return cmd;
    }

    private const string RESET_DB = @"
    BEGIN;
    DELETE FROM transacoes WHERE id IS NOT NULL;
    UPDATE clientes SET saldo = 0 WHERE id is not null;
    COMMIT;
";
    
    private const string QUERY_BALANCE = @"
    SELECT 
        c.saldo,
        c.limite
    FROM clientes c
    WHERE c.id = $1;
";

    private const string QUERY_TRANSACTIONS = @"
    WITH trans AS (
        SELECT
            valor,
            tipo,
            descricao,
            realizada_em::text
        FROM transacoes
        WHERE cliente_id = $1
        ORDER BY id DESC
        LIMIT 10
    )
    SELECT
        t.*,
        c.saldo,
        c.limite
    FROM clientes c
    LEFT JOIN trans t ON true
    WHERE id = $1;";

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        await _readCommandPool.DisposeAsync();
        await _creditCommandPool.DisposeAsync();
    }

    private sealed class CommandPool : IAsyncDisposable
    {
        private readonly Channel<NpgsqlCommand> _conns = Channel.CreateUnbounded<NpgsqlCommand>(
            new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = true,
                SingleReader = false,
                SingleWriter = false
            });

        public CommandPool(IEnumerable<NpgsqlCommand> items, int poolSize)
        {
            for (var i = 0; i < poolSize; i++)
            {
                _ = _conns.Writer.TryWrite(items.ElementAt(i));
            }
        }

        public async ValueTask<Command> RentAsync()
        {
            NpgsqlCommand? item = null;
            Command command;
            try
            {
                item = await _conns.Reader.ReadAsync();
                command = new Command(item, ReturnPoolItemAsync);
            }
            catch
            {
                if (item != null)
                    await _conns.Writer.WriteAsync(item);
                throw;
            }
            return command;
        }

        private async ValueTask<List<NpgsqlCommand>> ReturnAllAsync()
        {
            var items = new List<NpgsqlCommand>();
            await foreach (var item in _conns.Reader.ReadAllAsync())
                items.Add(item);
            return items;
        }

        private async ValueTask ReturnPoolItemAsync(Command command)
        => await _conns.Writer.WriteAsync(command.Value);

        public async ValueTask DisposeAsync()
        {
            var items = await ReturnAllAsync();
            await Parallel.ForEachAsync(items, (item, _) => item.DisposeAsync());
        }
    }

    public readonly struct Command(NpgsqlCommand value, Func<Command, ValueTask> returnPoolItemAsync)
    {
        public NpgsqlCommand Value { get; } = value;

        public ValueTask DisposeAsync() => returnPoolItemAsync(this);
    }
}