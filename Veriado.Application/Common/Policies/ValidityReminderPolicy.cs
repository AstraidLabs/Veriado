using System;

namespace Veriado.Application.Common.Policies;

/// <summary>
/// Describes how far in advance validity reminders should be issued for expiring documents.
/// </summary>
public sealed class ValidityReminderPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidityReminderPolicy"/> class.
    /// </summary>
    /// <param name="reminderLeadTime">The lead time before expiration at which documents should be surfaced.</param>
    public ValidityReminderPolicy(TimeSpan reminderLeadTime)
    {
        if (reminderLeadTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reminderLeadTime), reminderLeadTime, "Reminder lead time must be non-negative.");
        }

        ReminderLeadTime = reminderLeadTime;
    }

    /// <summary>
    /// Gets the lead time before expiration when documents should be surfaced.
    /// </summary>
    public TimeSpan ReminderLeadTime { get; }
}
