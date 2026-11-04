using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;

namespace WarehouseParts.Services;

/// <summary>
/// Инвентаризация: сравнение учетного и фактического остатка. Введенные фактические
/// остатки хранятся только в памяти сеанса; корректировка создается обычной операцией
/// журнала только по явному действию пользователя.
/// </summary>
public class InventoryService
{
    private readonly AppDbContext _db;
    private readonly OperationsService _operations;
    private List<InventoryRow>? _rows;

    public InventoryService(AppDbContext db, OperationsService operations)
    {
        _db = db;
        _operations = operations;
    }

    /// <summary>Все позиции каталога с учетными остатками, сортировка по внутреннему коду.</summary>
    public async Task<IReadOnlyList<InventoryRow>> GetRowsAsync(CancellationToken cancellationToken = default)
    {
        if (_rows is not null)
        {
            return _rows;
        }

        var items = await _db.Items.AsNoTracking()
            .OrderBy(i => i.Code)
            .ToListAsync(cancellationToken);
        var stocks = await _operations.GetStocksAsync(items.Select(i => i.Id), cancellationToken);

        _rows = items.Select(item => new InventoryRow
        {
            ItemId = item.Id,
            Code = item.Code,
            Name = item.Name,
            Stock = stocks.TryGetValue(item.Id, out var stock) ? stock : 0
        }).ToList();

        return _rows;
    }

    /// <summary>Устанавливает фактический остаток строки. Null очищает введенное значение.</summary>
    public void SetFact(int itemId, int? fact)
    {
        if (fact < 0)
        {
            throw new DomainValidationException("Фактический остаток не может быть отрицательным");
        }

        var row = _rows?.FirstOrDefault(r => r.ItemId == itemId)
            ?? throw new DomainValidationException("Позиция не найдена в списке инвентаризации");
        row.Fact = fact;
    }

    /// <summary>Есть непримененные ненулевые расхождения (предупреждение при уходе с экрана).</summary>
    public bool HasUnappliedDiscrepancies
        => _rows?.Any(r => r.Discrepancy.GetValueOrDefault() != 0) == true;

    /// <summary>
    /// Перечитывает каталог и учетные остатки, сохраняя введенные фактические остатки
    /// текущего сеанса по позициям.
    /// </summary>
    public async Task<IReadOnlyList<InventoryRow>> ReloadRowsAsync(CancellationToken cancellationToken = default)
    {
        var facts = _rows?.ToDictionary(r => r.ItemId, r => r.Fact) ?? new Dictionary<int, int?>();

        var items = await _db.Items.AsNoTracking()
            .OrderBy(i => i.Code)
            .ToListAsync(cancellationToken);
        var stocks = await _operations.GetStocksAsync(items.Select(i => i.Id), cancellationToken);

        _rows = items.Select(item => new InventoryRow
        {
            ItemId = item.Id,
            Code = item.Code,
            Name = item.Name,
            Stock = stocks.TryGetValue(item.Id, out var stock) ? stock : 0,
            Fact = facts.TryGetValue(item.Id, out var fact) ? fact : null
        }).ToList();

        return _rows;
    }

    /// <summary>
    /// Применяет корректировку строки: создает обычную операцию журнала
    /// «Приход N» / «Расход N» с комментарием «Инвентаризация ДД.ММ.ГГГГ».
    /// Операция проходит стандартные проверки журнала. После успеха учетный остаток
    /// строки обновляется и расхождение становится нулевым.
    /// </summary>
    public async Task<Operation> ApplyAsync(int itemId, DateTime date, CancellationToken cancellationToken = default)
    {
        var rows = _rows ?? throw new InvalidOperationException("Список инвентаризации не загружен");
        var row = rows.FirstOrDefault(r => r.ItemId == itemId)
            ?? throw new DomainValidationException("Позиция не найдена в списке инвентаризации");

        var discrepancy = row.Discrepancy
            ?? throw new DomainValidationException("Фактический остаток не заполнен");
        if (discrepancy == 0)
        {
            throw new DomainValidationException("Расхождение равно нулю, корректировка не требуется");
        }

        var type = discrepancy > 0 ? OperationType.Receipt : OperationType.Issue;
        var comment = $"Инвентаризация {date:dd.MM.yyyy}";
        var operation = await _operations.AddAsync(type, itemId, Math.Abs(discrepancy), comment, cancellationToken);

        row.Stock = row.Fact!.Value;
        return operation;
    }

    /// <summary>
    /// Сбрасывает состояние инвентаризации: при следующем открытии экрана список
    /// будет загружен заново, введённые фактические остатки сеанса не сохраняются.
    /// </summary>
    public void ClearSession()
    {
        _rows = null;
    }
}
