using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Veriado.Presentation.ViewModels.Files;

/// <summary>
/// Represents the sort configuration applied to the files grid.
/// </summary>
public sealed partial class SortStateViewModel : ViewModelBase
{
    public SortStateViewModel(IMessenger messenger)
        : base(messenger)
    {
        Options = new List<FileSortOption>
        {
            new("Naposledy upraveno", FileSortField.LastModified),
            new("Vytvořeno", FileSortField.Created),
            new("Název", FileSortField.Name),
            new("Velikost", FileSortField.Size),
        };

        selectedOption = Options[0];
        isDescending = true;
    }

    /// <summary>
    /// Gets the available sort options.
    /// </summary>
    public IReadOnlyList<FileSortOption> Options { get; }

    [ObservableProperty]
    private FileSortOption selectedOption;

    [ObservableProperty]
    private bool isDescending;
}

/// <summary>
/// Represents a single sort option exposed by the view model.
/// </summary>
/// <param name="DisplayName">The user facing display name.</param>
/// <param name="Field">The sort field.</param>
public sealed record FileSortOption(string DisplayName, FileSortField Field);

/// <summary>
/// Enumerates available sort fields.
/// </summary>
public enum FileSortField
{
    LastModified,
    Created,
    Name,
    Size,
}
