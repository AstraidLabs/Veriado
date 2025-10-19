using System;
using System.Collections.Generic;
using System.IO;
using Veriado.Domain.ValueObjects;
using Xunit;

namespace Veriado.Application.Tests.Storage;

public sealed class StoragePathTests
{
    public static IEnumerable<object[]> EscapingPaths()
    {
        yield return new object[] { ".." };
        yield return new object[] { Path.Combine("..", "outside.txt") };
        yield return new object[] { Path.Combine("..", "..", "escape", "file.bin") };
        yield return new object[] { $".{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}outside" };

        var absoluteRoot = Path.GetPathRoot(Environment.CurrentDirectory);
        if (!string.IsNullOrEmpty(absoluteRoot))
        {
            yield return new object[] { Path.Combine(absoluteRoot, "absolute.txt") };
        }

        yield return new object[] { @"\\server\share\file.dat" };
        yield return new object[] { "//server/share/file.dat" };
    }

    [Theory]
    [MemberData(nameof(EscapingPaths))]
    public void From_WhenRelativePathEscapesRoot_Throws(string relative)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<StoragePathViolationException>(() => StoragePath.From(root, relative));
    }

    [Theory]
    [InlineData("files/data.txt")]
    [InlineData("files/sub/file.bin")]
    [InlineData("folder/inner/asset.json")]
    public void From_WhenRelativePathIsValid_ReturnsNormalizedPath(string relative)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var path = StoragePath.From(root, relative);
        var rootFull = Path.GetFullPath(root);
        var expectedRelative = Path.GetRelativePath(rootFull, Path.GetFullPath(Path.Combine(rootFull, relative)));

        Assert.Equal(StoragePath.From(expectedRelative), path);
    }
}
