using System.Data;
using System.Threading.Channels;
using Npgsql;

namespace Rinha2024.Dotnet;

public class Database(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private static readonly int ReadPoolSize = int.TryParse(Environment.GetEnvironmentVariable("READ_POOL_SIZE"), out var value) ? value : 1500;
    private static readonly int WritePoolSize = int.TryParse(Environment.GetEnvironmentVariable("WRITE_POOL_SIZE"), out var value) ? value : 3000;

    private readonly Pool _transactionPool =
        new(
            CreateCommands(
                new NpgsqlCommand(QUERY_TRANSACTIONS) {Parameters = {new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer}}}, ReadPoolSize), ReadPoolSize);
    private readonly Pool _debitPool =
        new(CreateCommands(new NpgsqlCommand("CREATE_TRANSACTION_DEBIT")
        {
            CommandType = CommandType.StoredProcedure,
            Parameters =
            {
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer},
                new NpgsqlParameter<char> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char},
                new NpgsqlParameter<string> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Direction = ParameterDirection.Output},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Direction = ParameterDirection.Output},
            }
        }, WritePoolSize), WritePoolSize);
    private readonly Pool _creditPool =
        new(CreateCommands(new NpgsqlCommand("CREATE_TRANSACTION_CREDIT")
        {
            CommandType = CommandType.StoredProcedure,
            Parameters =
            {
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer},
                new NpgsqlParameter<char> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char},
                new NpgsqlParameter<string> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Direction = ParameterDirection.Output},
                new NpgsqlParameter<int> {NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Direction = ParameterDirection.Output},
            }
        }, WritePoolSize), WritePoolSize);

    public async Task<int[]?> DoTransaction(int id, CreateTransactionDto dto)
    {
        await using var poolItem = dto.Tipo == 'd' ? await _debitPool.RentAsync() : await _creditPool.RentAsync();
        var cmd = poolItem.Value;
        cmd.Parameters[0].Value = id;
        cmd.Parameters[1].Value = dto.Valor;
        cmd.Parameters[2].Value = dto.Tipo;
        cmd.Parameters[3].Value = dto.Descricao;
        await using var connection = await dataSource.OpenConnectionAsync();
        cmd.Connection = connection;
        await cmd.ExecuteNonQueryAsync();
        var balance = (int) (cmd.Parameters[4].Value ?? 0);
        var limit = (int) (cmd.Parameters[5].Value ?? 0); 
        if (limit == 0) return null;
        return [balance, limit];
    }
    
    public async Task<ExtractDto?> GetExtract(int id)
    {
        await using var poolItem = await _transactionPool.RentAsync();
        var cmd = poolItem.Value;
        cmd.Parameters[0].Value = id;
        await using var connection = await dataSource.OpenConnectionAsync();
        cmd.Connection = connection;
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!reader.HasRows) return null;
        await reader.ReadAsync();
        var balance = new SaldoDto(reader.GetInt32(4), reader.GetInt32(5));
        var hasTransactions = await reader.IsDBNullAsync(0);
        if (hasTransactions) return new ExtractDto(balance, []);
        var transactions = new List<TransactionDto>()
        {
            new ()
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
        await Parallel.ForAsync(0, 50, async (_,_) =>
        {
            await GetExtract(1);
            await DoTransaction(1, new CreateTransactionDto(1000, 'c', "blablabla"));
            await DoTransaction(1, new CreateTransactionDto(1000, 'd', "blablabla"));
        });
        await using var cmd = new NpgsqlCommand(RESET_DB, await dataSource.OpenConnectionAsync());
        await cmd.ExecuteNonQueryAsync();
    }
    

    private static IEnumerable<NpgsqlCommand> CreateCommands(NpgsqlCommand cmd, int qtd)
    {
        for (var i = 0; i < qtd; i++)
        {
            yield return cmd.Clone();
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
        await _transactionPool.DisposeAsync();
        await _creditPool.DisposeAsync();
    }

    private sealed class Pool : IAsyncDisposable
    {

        private readonly Channel<NpgsqlCommand> queue = Channel.CreateUnbounded<NpgsqlCommand>(
            new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = true,
                SingleReader = false,
                SingleWriter = false
            });

        public Pool(IEnumerable<NpgsqlCommand> items, int poolSize)
        {
            for (var i = 0; i < poolSize; i++)
            {
                if (!queue.Writer.TryWrite(items.ElementAt(i)))
                    throw new ApplicationException("Failed to enqueue starting item on Pool.");
            }
        }

        public async ValueTask<PoolItem> RentAsync()
        {
            NpgsqlCommand? item = null;
            try
            {
                item = await queue.Reader.ReadAsync();
                var poolItem = new PoolItem(item, ReturnPoolItemAsync);
                return poolItem;
            }
            catch
            {
                if (item != null)
                    await queue.Writer.WriteAsync(item);
                throw;
            }
        }

        private async ValueTask<List<NpgsqlCommand>> ReturnAllAsync()
        {
            var items = new List<NpgsqlCommand>();
            await foreach (var item in queue.Reader.ReadAllAsync())
                items.Add(item);
            return items;
        }

        private async ValueTask ReturnPoolItemAsync(PoolItem poolItem)
        {
            poolItem.Value.Connection = null;
            await queue.Writer.WriteAsync(poolItem.Value);
        }

        public async ValueTask DisposeAsync()
        {
            var items = await ReturnAllAsync();
            await Parallel.ForEachAsync(items, (item, _) => item.DisposeAsync());
        }
    }

    public readonly struct PoolItem(NpgsqlCommand value, Func<PoolItem, ValueTask> returnPoolItemAsync)
    {
        public NpgsqlCommand Value { get; } = value;

        public ValueTask DisposeAsync() => returnPoolItemAsync(this);
    }
}