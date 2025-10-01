namespace Veriado.Infrastructure.Search;

public interface ISearchIndexSignatureCalculator
{
    SearchIndexSignature Compute(FileEntity file);
}
