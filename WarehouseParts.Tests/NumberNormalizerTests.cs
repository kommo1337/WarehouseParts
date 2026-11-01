using WarehouseParts.Services;

namespace WarehouseParts.Tests;

public class NumberNormalizerTests
{
    [Theory]
    [InlineData("06a-115 561b", "06A115561B")]
    [InlineData("W 712/52", "W71252")]
    [InlineData("W712.52", "W71252")]
    [InlineData("oem 1", "OEM1")]
    [InlineData("AN-2", "AN2")]
    [InlineData("ан-2", "АН2")]
    [InlineData("-- // ..", "")]
    public void Normalize_ОставляетТолькоБуквыИЦифры(string raw, string expected)
    {
        Assert.Equal(expected, NumberNormalizer.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Normalize_ПустойВвод_ПустойРезультат(string? raw)
    {
        Assert.Equal("", NumberNormalizer.Normalize(raw));
    }
}
