using System;

namespace Veriado.WinUI.Models.Import;

public sealed class ImportErrorItem
{
    public ImportErrorItem(string fileName, string errorMessage, Guid? fileId)
    {
        FileName = fileName;
        ErrorMessage = errorMessage;
        FileId = fileId;
    }

    public string FileName { get; }

    public string ErrorMessage { get; }

    public Guid? FileId { get; }
}
