using System;
using System.Collections.Generic;

namespace Veriado.WinUI.ViewModels.Validation;

/// <summary>
/// Represents a collection of validation errors grouped by property name.
/// </summary>
public sealed class ValidationResult
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors
    {
        get
        {
            var snapshot = new Dictionary<string, IReadOnlyList<string>>(_errors.Count, StringComparer.Ordinal);
            foreach (var pair in _errors)
            {
                snapshot[pair.Key] = pair.Value.AsReadOnly();
            }

            return snapshot;
        }
    }

    public void AddError(string propertyName, string message)
    {
        propertyName = string.IsNullOrEmpty(propertyName) ? string.Empty : propertyName;

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!_errors.TryGetValue(propertyName, out var list))
        {
            list = new List<string>();
            _errors[propertyName] = list;
        }

        foreach (var existing in list)
        {
            if (string.Equals(existing, message, StringComparison.Ordinal))
            {
                return;
            }
        }

        list.Add(message);
    }

    public IEnumerable<string> GetErrors(string propertyName)
    {
        propertyName = string.IsNullOrEmpty(propertyName) ? string.Empty : propertyName;
        return _errors.TryGetValue(propertyName, out var list) ? list : Array.Empty<string>();
    }
}
