using System;
using System.Collections.Generic;
using System.IO;
using Veriado.Domain.ValueObjects;
using Xunit;

namespace Veriado.Application.Tests.Storage;

public sealed class StoragePathTests : IDisposable
{
    private readonly string _root;

    public StoragePathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "veriado-tests", Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void From_WithRelativePathInsideRoot_ReturnsNormalizedStoragePath()
    {
        var relative = Path.Combine("folder", "child", "file.txt");

        var storagePath = StoragePath.From(_root, relative);

        Assert.Equal("folder/child/file.txt", storagePath.Value);
    }

    [Theory]
    [MemberData(nameof(GetEscapingPaths))]
    public void From_WithEscapingRelativePath_ThrowsViolation(string relative)
    {
        Assert.Throws<StoragePathViolationException>(() => StoragePath.From(_root, relative));
    }

    public static IEnumerable<object[]> GetEscapingPaths()
    {
        yield return new object[] { Path.Combine("..", "outside.txt") };
        yield return new object[] { Path.Combine(".", "..", "outside.txt") };
        yield return new object[] { ".." };
        yield return new object[] { "../outside.txt" };
        yield return new object[] { "..\\outside.txt" };
        yield return new object[] { @".\..\outside.txt" };
        yield return new object[] { "./../outside.txt" };
        yield return new object[] { @"\\server\share\file.txt" };
        yield return new object[] { @"//server/share/file.txt" };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }
}
