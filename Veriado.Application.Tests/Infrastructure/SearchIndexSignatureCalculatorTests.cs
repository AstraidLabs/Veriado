using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Veriado.Appl.Search;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Search;

namespace Veriado.Application.Tests.Infrastructure;

public sealed class SearchIndexSignatureCalculatorTests
{
    [Fact]
    public void Compute_ReturnsNormalizedTitleAndTokenHash()
    {
        // Arrange
        var analyzerOptions = new AnalyzerOptions
        {
            DefaultProfile = "cs",
            Profiles = new Dictionary<string, AnalyzerProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["cs"] = new()
                {
                    Name = "cs",
                    EnableStemming = false,
                    KeepNumbers = true,
                    SplitFilenames = true,
                    Stopwords = Array.Empty<string>(),
                    CustomFilters = Array.Empty<string>()
                }
            }
        };

        var analyzerFactory = new AnalyzerFactory(Options.Create(analyzerOptions));
        var searchOptions = new SearchOptions
        {
            Analyzer = analyzerOptions
        };

        var calculator = new SearchIndexSignatureCalculator(Options.Create(searchOptions), analyzerFactory);

        var created = UtcTimestamp.From(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var bytes = Encoding.UTF8.GetBytes("Hello metadata");
        var fileSystemId = Guid.NewGuid();
        var file = FileEntity.CreateNew(
            FileName.From("Výkaz"),
            FileExtension.From("pdf"),
            MimeType.From("application/pdf"),
            "Čeněk",
            fileSystemId,
            StorageProvider.Local.ToString(),
            fileSystemId.ToString("D"),
            FileHash.Compute(bytes),
            ByteSize.From(bytes.LongLength),
            ContentVersion.Initial,
            created);

        file.SetTitle("Přehled Dat", created);
        file.SetAuthor("Čeněk", created);

        var analyzer = analyzerFactory.Create();

        // Act
        var signature = calculator.Compute(file);
        var document = file.ToSearchDocument();

        // Assert
        var expectedTitle = analyzer.Normalize(document.Title);
        Assert.Equal(expectedTitle, signature.NormalizedTitle);

        var tokens = new List<string>();
        void Collect(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            foreach (var token in analyzer.Tokenize(source))
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    tokens.Add(token);
                }
            }
        }

        Collect(document.Title);
        Collect(document.Author);
        Collect(document.Mime);
        Collect(document.MetadataText);

        Assert.NotEmpty(tokens);
        var payload = string.Join('\n', tokens);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        Assert.Equal(expectedHash, signature.TokenHash);
    }
}
