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

    [Theory]
    [MemberData(nameof(GetValidRelativePaths))]
    public void From_WithValidRelativePath_ReturnsNormalizedStoragePath(string relative, string expected)
    {
        var storagePath = StoragePath.From(_root, relative);

        Assert.Equal(expected, storagePath.Value);
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
        yield return new object[] { Path.GetFullPath(Path.Combine(Path.GetTempPath(), "..", "outside.txt")) };
    }

    public static IEnumerable<object[]> GetValidRelativePaths()
    {
        yield return new object[] { Path.Combine("folder", "child", "file.txt"), "folder/child/file.txt" };
        yield return new object[] { Path.Combine("folder", "..", "sibling", "file.txt"), "sibling/file.txt" };
        yield return new object[] { Path.Combine(".", "folder", "file.txt"), "folder/file.txt" };
        yield return new object[] { Path.Combine("folder", ".", "nested", "file.txt"), "folder/nested/file.txt" };
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
