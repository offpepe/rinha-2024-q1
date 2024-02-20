using Microsoft.AspNetCore.Mvc;

namespace Rinha2024.Dotnet.Extensions;

public static class ControllerSetup
{
    public static void SetControllers(this WebApplication app)
    {
        app.MapGet("/ping", () => "pong");
        app.MapGet("/clientes/{id:int}/extrato",
            async (int id, [FromServices] Database db) =>
            {
                var balance = await db.GetExtract(id);
                return !balance.HasValue ? Results.NotFound() : Results.Ok(balance.Value);
            });
        app.MapPost("/clientes/{id:int}/transacoes", async (int id,
            [FromServices] Database db,
            [FromBody] CreateTransactionDto dto) =>
        {
            if (!ValidateTransaction(dto)) return Results.UnprocessableEntity();
            var values = await db.DoTransaction(id, dto);
            if (values == null) return Results.NotFound();
            return values[1] < 0 ? Results.UnprocessableEntity() : Results.Ok(new ValidateTransactionDto(values[1], values[0]));
        });
    }

    private static bool ValidateTransaction(CreateTransactionDto dto)
    {
        if (string.IsNullOrEmpty(dto.Descricao)) return false;
        if (dto.Descricao.Length > 10) return false;
        if (dto.Tipo != 'd' && dto.Tipo != 'c') return false;
        return true;
    }
}