namespace Veriado.Appl.UseCases.Queries.FileGrid;

/// <summary>
/// Validates <see cref="FileGridQueryDto"/> instances.
/// </summary>
public sealed class FileGridQueryValidator : AbstractValidator<FileGridQueryDto>
{
    private static readonly HashSet<string> AllowedSortFields = new(
        new[]
        {
            "name",
            "mime",
            "extension",
            "size",
            "createdutc",
            "modifiedutc",
            "version",
            "validuntil",
            "author",
            "score",
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="FileGridQueryValidator"/> class.
    /// </summary>
    public FileGridQueryValidator(FileGridQueryOptions options)
    {
        RuleFor(dto => dto.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(dto => dto.PageSize)
            .InclusiveBetween(1, options.MaxPageSize);

        RuleFor(dto => dto.Text)
            .Must(text => !string.IsNullOrWhiteSpace(text))
            .When(dto => dto.Fuzzy && string.IsNullOrWhiteSpace(dto.SavedQueryKey))
            .WithMessage("Text is required when fuzzy search is enabled.");

        RuleFor(dto => dto.Extension)
            .Must(extension => extension is null || extension.Length <= 16)
            .WithMessage("Extension must be at most 16 characters long.");

        RuleFor(dto => dto.ExtensionMatchMode)
            .IsInEnum();

        RuleFor(dto => dto.Mime)
            .Must(mime => mime is null || mime.Contains('/'))
            .WithMessage("Mime must contain a '/' separator.");

        RuleFor(dto => dto.SizeMax)
            .Must((dto, max) => !max.HasValue || !dto.SizeMin.HasValue || dto.SizeMin.Value <= max.Value)
            .WithMessage("SizeMax must be greater than or equal to SizeMin.");

        RuleFor(dto => dto.CreatedToUtc)
            .Must((dto, to) => !to.HasValue || !dto.CreatedFromUtc.HasValue || dto.CreatedFromUtc.Value <= to.Value)
            .WithMessage("CreatedToUtc must be greater than or equal to CreatedFromUtc.");

        RuleFor(dto => dto.ModifiedToUtc)
            .Must((dto, to) => !to.HasValue || !dto.ModifiedFromUtc.HasValue || dto.ModifiedFromUtc.Value <= to.Value)
            .WithMessage("ModifiedToUtc must be greater than or equal to ModifiedFromUtc.");

        RuleFor(dto => dto.ExpiringInDays)
            .Must(value => !value.HasValue || value.Value >= 0)
            .WithMessage("ExpiringInDays must be non-negative.");

        RuleForEach(dto => dto.Sort)
            .SetValidator(new SortSpecValidator());
    }

    private sealed class SortSpecValidator : AbstractValidator<FileSortSpecDto>
    {
        public SortSpecValidator()
        {
            RuleFor(spec => spec.Field)
                .NotEmpty()
                .Must(field => AllowedSortFields.Contains(field))
                .WithMessage("Unsupported sort field.");
        }
    }
}
