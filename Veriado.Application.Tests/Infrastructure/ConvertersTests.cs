using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Veriado.Domain.ValueObjects;
using Xunit;

namespace Veriado.Application.Tests.Infrastructure;

public sealed class ConvertersTests
{
    private static readonly Type ConvertersType = Type.GetType(
        "Veriado.Infrastructure.Persistence.Configurations.Converters, Veriado.Infrastructure",
        throwOnError: true)!;

    [Fact]
    public void StoragePathToString_RoundTrips()
    {
        var converter = (ValueConverter<StoragePath, string>)ConvertersType
            .GetField("StoragePathToString", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        var path = StoragePath.From("  /tmp/data.bin  ");
        var stored = converter.ConvertToProvider(path);
        Assert.Equal(path.Value, stored);

        var restored = converter.ConvertFromProvider(stored);
        Assert.Equal(path, restored);
    }

    [Fact]
    public void ContentVersionToInt_RoundTrips()
    {
        var converter = (ValueConverter<ContentVersion, int>)ConvertersType
            .GetField("ContentVersionToInt", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        var version = ContentVersion.From(5);
        var stored = converter.ConvertToProvider(version);
        Assert.Equal(version.Value, stored);

        var restored = converter.ConvertFromProvider(stored);
        Assert.Equal(version, restored);
    }
}
