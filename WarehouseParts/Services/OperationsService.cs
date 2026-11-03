using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;

namespace WarehouseParts.Services;

/// <summary>
/// Проведение и удаление операций журнала и расчет остатков.
/// Источник истины для остатка — таблица Operations.
/// </summary>
public class OperationsService
{
    public const string InsufficientStockMessage = "Нельзя удалить операцию: тогда последующие расходы по этой запчасти стали бы невозможны. Сначала удалите или скорректируйте их.";

    private readonly AppDbContext _db;

    public OperationsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Operation> AddAsync(OperationType type, int itemId, int quantity, string? comment, CancellationToken cancellationToken = default)
    {
        if (type != OperationType.Receipt && type != OperationType.Issue)
        {
            throw new DomainValidationException("Выберите операцию");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var itemExists = await _db.Items.AnyAsync(i => i.Id == itemId, cancellationToken);
            if (!itemExists)
            {
                throw new DomainValidationException("Укажите запчасть");
            }

            if (quantity <= 0)
            {
                throw new DomainValidationException("Количество должно быть целым числом больше нуля");
            }

            if (type == OperationType.Issue)
            {
                var stock = await GetStockAsync(itemId, cancellationToken);
                if (quantity > stock)
                {
                    throw new DomainValidationException($"Недостаточно остатка: доступно {stock}");
                }
            }

            var operation = new Operation
            {
                OperationType = type,
                ItemId = itemId,
                Quantity = quantity,
                CreatedAt = DateTime.Now,
                Comment = comment
            };
            _db.Operations.Add(operation);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return operation;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteAsync(int operationId, CancellationToken cancellationToken = default)
    {
        var operation = await _db.Operations
            .FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken)
            ?? throw new DomainValidationException("Операция не найдена");

        var itemOperations = await _db.Operations
            .AsNoTracking()
            .Where(o => o.ItemId == operation.ItemId)
            .OrderBy(o => o.Id)
            .Select(o => new { o.Id, o.OperationType, o.Quantity })
            .ToListAsync(cancellationToken);

        var balance = 0;
        foreach (var existing in itemOperations)
        {
            if (existing.Id == operationId)
            {
                continue;
            }

            balance += existing.OperationType == OperationType.Receipt
                ? existing.Quantity
                : -existing.Quantity;

            if (balance < 0)
            {
                throw new DomainValidationException(InsufficientStockMessage);
            }
        }

        _db.Operations.Remove(operation);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Текущий остаток позиции: сумма приходов минус сумма расходов.</summary>
    public async Task<int> GetStockAsync(int itemId, CancellationToken cancellationToken = default)
    {
        var stock = await _db.Operations
            .Where(o => o.ItemId == itemId)
            .Select(o => (int?)(o.OperationType == OperationType.Receipt ? o.Quantity : -o.Quantity))
            .SumAsync(cancellationToken) ?? 0;
        LogNegativeStock(itemId, stock);
        return stock;
    }

    /// <summary>Остатки по набору позиций одним запросом. Позиции без операций получают остаток 0.</summary>
    public async Task<IReadOnlyDictionary<int, int>> GetStocksAsync(IEnumerable<int> itemIds, CancellationToken cancellationToken = default)
    {
        var ids = itemIds.Distinct().ToList();
        var stocks = ids.ToDictionary(id => id, id => 0);
        if (ids.Count > 0)
        {
            var calculated = await _db.Operations
                .Where(o => ids.Contains(o.ItemId))
                .GroupBy(o => o.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Stock = g.Sum(o => o.OperationType == OperationType.Receipt ? o.Quantity : -o.Quantity)
                })
                .ToDictionaryAsync(x => x.ItemId, x => x.Stock, cancellationToken);

            foreach (var pair in calculated)
            {
                stocks[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in stocks)
        {
            LogNegativeStock(pair.Key, pair.Value);
        }

        return stocks;
    }

    /// <summary>Статус наличия позиции: «В наличии» при остатке больше нуля, иначе «Нет в наличии».</summary>
    public async Task<string> GetStockStatusAsync(int itemId, CancellationToken cancellationToken = default)
        => StockStatus.FromStock(await GetStockAsync(itemId, cancellationToken));

    /// <summary>Последние операции позиции для панели предпросмотра: новые первыми.</summary>
    public async Task<List<Operation>> GetRecentOperationsForItemAsync(
        int itemId,
        int count = 10,
        CancellationToken cancellationToken = default)
        => await _db.Operations.AsNoTracking()
            .Include(o => o.Item)
            .Where(o => o.ItemId == itemId)
            .OrderByDescending(o => o.Id)
            .Take(count)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Все операции журнала с расчетными остатками после операции.
    /// Порядок проведения определяется возрастанием Id; новые операции возвращаются первыми.
    /// </summary>
    public async Task<List<JournalRow>> GetJournalRowsAsync(CancellationToken cancellationToken = default)
    {
        var page = await GetJournalPageAsync(null, null, null, null, 0, int.MaxValue, cancellationToken);
        return page.Rows;
    }

    public async Task<(List<JournalRow> Rows, int Total)> GetJournalPageAsync(
        string? code,
        OperationType? operationType,
        DateTime? dateFrom,
        DateTime? dateTo,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skip));
        }

        if (take <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        var parameters = new List<KeyValuePair<string, object>>();
        var countWhere = BuildJournalFilter("o", "i.\"Code\"", code, operationType, dateFrom, dateTo, parameters);
        var pageWhere = BuildJournalFilter("j", "j.\"ItemCode\"", code, operationType, dateFrom, dateTo, null);
        parameters.Add(new KeyValuePair<string, object>("$take", take));
        parameters.Add(new KeyValuePair<string, object>("$skip", skip));

        var connection = _db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var countCommand = CreateCommand(
                connection,
                "SELECT COUNT(*) FROM \"Operations\" AS o INNER JOIN \"Items\" AS i ON i.\"Id\" = o.\"ItemId\" WHERE 1 = 1" + countWhere,
                parameters.Take(parameters.Count - 2));
            var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
            if (total == 0 || skip >= total)
            {
                return (new List<JournalRow>(), total);
            }

            var sql = new StringBuilder("""
                SELECT j."Id", j."OperationType", j."ItemId", j."Quantity", j."CreatedAt", j."Comment",
                       j."ItemCode", j."ItemName", j."Unit", j."BalanceAfter"
                FROM (
                    SELECT o."Id", o."OperationType", o."ItemId", o."Quantity", o."CreatedAt", o."Comment",
                           i."Code" AS "ItemCode", i."Name" AS "ItemName", i."Unit",
                           SUM(CASE WHEN o."OperationType" = 'Приход' THEN o."Quantity" ELSE -o."Quantity" END)
                               OVER (PARTITION BY o."ItemId" ORDER BY o."Id"
                                     ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS "BalanceAfter"
                    FROM "Operations" AS o
                    INNER JOIN "Items" AS i ON i."Id" = o."ItemId"
                ) AS j
                WHERE 1 = 1
                """);
            sql.Append(pageWhere);
            sql.Append(" ORDER BY j.\"Id\" DESC LIMIT $take OFFSET $skip;");

            await using var pageCommand = CreateCommand(connection, sql.ToString(), parameters);
            await using var reader = await pageCommand.ExecuteReaderAsync(cancellationToken);
            var rows = new List<JournalRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var item = new Item
                {
                    Id = reader.GetInt32(2),
                    Code = reader.GetString(6),
                    Name = reader.GetString(7),
                    Unit = DisplayTexts.ParseUnit(reader.GetString(8))
                };
                var operation = new Operation
                {
                    Id = reader.GetInt32(0),
                    OperationType = DisplayTexts.ParseOperation(reader.GetString(1)),
                    ItemId = reader.GetInt32(2),
                    Quantity = reader.GetInt32(3),
                    CreatedAt = reader.GetDateTime(4),
                    Comment = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Item = item
                };
                rows.Add(new JournalRow(operation, reader.GetInt32(9)));
            }

            return (rows, total);
        }
        finally
        {
            if (!wasOpen)
            {
                await _db.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary>Остаток после указанной операции (кумулятивный, по возрастанию Id). Null, если операция не найдена.</summary>
    public async Task<int?> GetBalanceAfterAsync(int operationId, CancellationToken cancellationToken = default)
    {
        var itemId = await _db.Operations
            .AsNoTracking()
            .Where(o => o.Id == operationId)
            .Select(o => (int?)o.ItemId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!itemId.HasValue)
        {
            return null;
        }

        return await _db.Operations
            .AsNoTracking()
            .Where(o => o.ItemId == itemId.Value && o.Id <= operationId)
            .Select(o => (int?)(o.OperationType == OperationType.Receipt ? o.Quantity : -o.Quantity))
            .SumAsync(cancellationToken) ?? 0;
    }

    private static string BuildJournalFilter(
        string operationAlias,
        string codeExpression,
        string? code,
        OperationType? operationType,
        DateTime? dateFrom,
        DateTime? dateTo,
        List<KeyValuePair<string, object>>? parameters)
    {
        var where = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(code))
        {
            where.Append(" AND wp_contains_ci(" + codeExpression + ", $code)");
            parameters?.Add(new KeyValuePair<string, object>("$code", code.Trim()));
        }

        if (operationType.HasValue)
        {
            where.Append(" AND " + operationAlias + ".\"OperationType\" = $operationType");
            parameters?.Add(new KeyValuePair<string, object>("$operationType", DisplayTexts.For(operationType.Value)));
        }

        if (dateFrom.HasValue)
        {
            where.Append(" AND " + operationAlias + ".\"CreatedAt\" >= $dateFrom");
            parameters?.Add(new KeyValuePair<string, object>("$dateFrom", dateFrom.Value.Date));
        }

        if (dateTo.HasValue)
        {
            where.Append(" AND " + operationAlias + ".\"CreatedAt\" < $dateTo");
            parameters?.Add(new KeyValuePair<string, object>("$dateTo", dateTo.Value.Date.AddDays(1)));
        }

        return where.ToString();
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        string sql,
        IEnumerable<KeyValuePair<string, object>> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var pair in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = pair.Key;
            parameter.Value = pair.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static void LogNegativeStock(int itemId, int stock)
    {
        if (stock < 0)
        {
            ErrorLog.Write($"Отрицательный остаток: позиция Id={itemId}, остаток={stock}");
        }
    }
}
