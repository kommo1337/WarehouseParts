using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WarehouseParts.Data;

public class AppDbContext : DbContext
{
    private static readonly ValueConverter<OperationType, string> OperationTypeConverter = new(
        v => v == OperationType.Receipt ? "Приход" : "Расход",
        s => s == "Приход" ? OperationType.Receipt : OperationType.Issue);

    private static readonly ValueConverter<UnitType, string> UnitTypeConverter = new(
        v => v == UnitType.Piece ? "Шт" : "Компл",
        s => s == "Шт" ? UnitType.Piece : UnitType.Kit);

    private static readonly ValueConverter<NumberType, string> NumberTypeConverter = new(
        v => v == NumberType.Oem ? "OEM" : (v == NumberType.Article ? "Артикул" : "Аналог"),
        s => s == "OEM" ? NumberType.Oem : (s == "Артикул" ? NumberType.Article : NumberType.Analog));

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
        if (Database.GetDbConnection() is SqliteConnection connection)
        {
            connection.CreateFunction<string?, string, bool>("wp_contains_ci", ContainsIgnoreCase, isDeterministic: true);
            connection.CreateFunction<string, string, bool>("wp_equals_ci", EqualsIgnoreCase, isDeterministic: true);
            connection.CreateFunction<string?, string?, string?, string?, string, bool>(
                "wp_contains_any_ci",
                ContainsAnyIgnoreCase,
                isDeterministic: true);
        }
    }

    public DbSet<Item> Items => Set<Item>();

    public DbSet<PartNumber> PartNumbers => Set<PartNumber>();

    public DbSet<Operation> Operations => Set<Operation>();

    [DbFunction("wp_contains_ci")]
    public static bool ContainsIgnoreCase(string? value, string query)
        => value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    [DbFunction("wp_equals_ci")]
    public static bool EqualsIgnoreCase(string value, string query)
        => string.Equals(value, query, StringComparison.OrdinalIgnoreCase);

    [DbFunction("wp_contains_any_ci")]
    public static bool ContainsAnyIgnoreCase(
        string? first,
        string? second,
        string? third,
        string? fourth,
        string query)
        => ContainsIgnoreCase(first, query)
            || ContainsIgnoreCase(second, query)
            || ContainsIgnoreCase(third, query)
            || ContainsIgnoreCase(fourth, query);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Unit).HasConversion(UnitTypeConverter);
        });

        modelBuilder.Entity<PartNumber>(entity =>
        {
            entity.HasIndex(e => new { e.NumberType, e.NumberNormalized });
            entity.HasIndex(e => e.NumberNormalized);
            entity.Property(e => e.NumberRaw).IsRequired();
            entity.Property(e => e.NumberNormalized).IsRequired();
            entity.Property(e => e.NumberType).HasConversion(NumberTypeConverter);
        });

        modelBuilder.Entity<Operation>(entity =>
        {
            entity.Property(e => e.OperationType).HasConversion(OperationTypeConverter);
        });
    }
}
