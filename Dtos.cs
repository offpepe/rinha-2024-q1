namespace Rinha2024.Dotnet;

public readonly record struct CreateTransactionDto(int Valor, char Tipo, string Descricao);
public readonly record struct TransactionDto(int valor, char tipo, string descricao, string realizada_em);
public readonly record struct ExtractDto(SaldoDto saldo, IEnumerable<TransactionDto> ultimas_transacoes);
public readonly record struct ValidateTransactionDto(int limite, int saldo);
public readonly record struct SaldoDto(int total, int limite)
{
    public DateTime data_extrato { get; } = DateTime.Now;
}