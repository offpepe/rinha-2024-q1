using Microsoft.AspNetCore.Mvc;
using Rinha2024.Dotnet.DTOs;

namespace Rinha2024.Dotnet.Extensions;

public static class ControllerSetup
{
    public static void SetControllers(this WebApplication app)
    {
        app.MapGet("/ping", () => "pong");
        app.MapGet("/clientes/{id:int}/extrato",
            async (int id, [FromServices] Service service) => await service.GetExtract(id));
        app.MapPost("/clientes/{id:int}/transacoes", async (int id, [FromServices] Service service, [FromBody] CreateTransactionDto dto) =>
        {
            var result = await service.ValidateTransactionAsync(id, dto.Valor, dto.Tipo);
            await service.CreateTransaction(id, dto);
            return Results.Ok(result);
        });
    }
}