using System;
using Veriado.Contracts.Files;
using Veriado.Contracts.Localization;
using Xunit;

namespace Veriado.Application.Tests.Files;

public class ValidityComputationTests
{
    [Fact]
    public void ComputeCountdown_WhenTargetInFuture_ComputesPositiveMetrics()
    {
        var reference = new DateTimeOffset(2024, 1, 10, 8, 0, 0, TimeSpan.Zero);
        var validUntil = new DateTimeOffset(2024, 1, 25, 12, 0, 0, TimeSpan.FromHours(2));

        var countdown = ValidityComputation.ComputeCountdown(reference, validUntil);

        Assert.NotNull(countdown);
        Assert.Equal(15, countdown.Value.TotalDays);
        Assert.Equal(15, countdown.Value.DaysRemaining);
        Assert.Equal(0, countdown.Value.DaysAfterExpiration);
        Assert.Equal(2, countdown.Value.WeeksRemaining);
        Assert.Equal(0, countdown.Value.WeeksAfterExpiration);
        Assert.Equal(0, countdown.Value.MonthsRemaining);
        Assert.Equal(0, countdown.Value.MonthsAfterExpiration);
        Assert.Equal(0, countdown.Value.YearsRemaining);
        Assert.Equal(0, countdown.Value.YearsAfterExpiration);
    }

    [Fact]
    public void ComputeCountdown_WhenExpired_ComputesElapsedMetrics()
    {
        var reference = new DateTimeOffset(2024, 3, 5, 12, 0, 0, TimeSpan.Zero);
        var validUntil = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var countdown = ValidityComputation.ComputeCountdown(reference, validUntil);

        Assert.NotNull(countdown);
        Assert.Equal(-33, countdown.Value.TotalDays);
        Assert.Equal(0, countdown.Value.DaysRemaining);
        Assert.Equal(33, countdown.Value.DaysAfterExpiration);
        Assert.Equal(0, countdown.Value.WeeksRemaining);
        Assert.Equal(4, countdown.Value.WeeksAfterExpiration);
        Assert.Equal(0, countdown.Value.MonthsRemaining);
        Assert.Equal(1, countdown.Value.MonthsAfterExpiration);
        Assert.Equal(0, countdown.Value.YearsRemaining);
        Assert.Equal(0, countdown.Value.YearsAfterExpiration);
    }

    [Fact]
    public void ComputeCountdown_TracksMonthsAndYears()
    {
        var reference = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var validUntil = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

        var countdown = ValidityComputation.ComputeCountdown(reference, validUntil);

        Assert.NotNull(countdown);
        Assert.Equal(731, countdown.Value.TotalDays);
        Assert.Equal(731, countdown.Value.DaysRemaining);
        Assert.Equal(0, countdown.Value.DaysAfterExpiration);
        Assert.Equal(104, countdown.Value.WeeksRemaining);
        Assert.Equal(24, countdown.Value.MonthsRemaining);
        Assert.Equal(2, countdown.Value.YearsRemaining);
    }

    [Fact]
    public void ComputeStatus_RespectsThresholds()
    {
        var thresholds = ValidityThresholds.Normalize(0, 7, 30);

        Assert.Equal(ValidityStatus.Expired, ValidityComputation.ComputeStatus(-1, thresholds));
        Assert.Equal(ValidityStatus.ExpiringToday, ValidityComputation.ComputeStatus(0, thresholds));
        Assert.Equal(ValidityStatus.ExpiringSoon, ValidityComputation.ComputeStatus(3, thresholds));
        Assert.Equal(ValidityStatus.ExpiringLater, ValidityComputation.ComputeStatus(14, thresholds));
        Assert.Equal(ValidityStatus.LongTerm, ValidityComputation.ComputeStatus(60, thresholds));

        var countdown = new ValidityCountdown(5, 5, 0, 0, 0, 0, 0, 0, 0);
        Assert.Equal(ValidityStatus.ExpiringSoon, ValidityComputation.ComputeStatus(countdown, thresholds));
    }

    [Theory]
    [InlineData(1, "1 den")]
    [InlineData(2, "2 dny")]
    [InlineData(5, "5 dní")]
    [InlineData(11, "11 dní")]
    [InlineData(-3, "3 dny")]
    public void CzechPluralization_FormatsDays(int value, string expected)
    {
        Assert.Equal(expected, CzechPluralization.FormatDays(value));
    }

    [Theory]
    [InlineData(1, "1 týden")]
    [InlineData(4, "4 týdny")]
    [InlineData(6, "6 týdnů")]
    [InlineData(-2, "2 týdny")]
    public void CzechPluralization_FormatsWeeks(int value, string expected)
    {
        Assert.Equal(expected, CzechPluralization.FormatWeeks(value));
    }

    [Theory]
    [InlineData(1, "1 měsíc")]
    [InlineData(3, "3 měsíce")]
    [InlineData(8, "8 měsíců")]
    [InlineData(-5, "5 měsíců")]
    public void CzechPluralization_FormatsMonths(int value, string expected)
    {
        Assert.Equal(expected, CzechPluralization.FormatMonths(value));
    }

    [Theory]
    [InlineData(1, "1 rok")]
    [InlineData(2, "2 roky")]
    [InlineData(5, "5 let")]
    [InlineData(-7, "7 let")]
    public void CzechPluralization_FormatsYears(int value, string expected)
    {
        Assert.Equal(expected, CzechPluralization.FormatYears(value));
    }

    [Fact]
    public void ValidityRelativeFormatter_SelectsLargestUnitBeforeExpiration()
    {
        var countdown = new ValidityCountdown(
            TotalDays: 45,
            DaysRemaining: 45,
            DaysAfterExpiration: 0,
            WeeksRemaining: 6,
            WeeksAfterExpiration: 0,
            MonthsRemaining: 1,
            MonthsAfterExpiration: 0,
            YearsRemaining: 0,
            YearsAfterExpiration: 0);

        var formatted = ValidityRelativeFormatter.FormatRemaining(countdown);
        Assert.Equal("1 měsíc", formatted);
    }

    [Fact]
    public void ValidityRelativeFormatter_SelectsLargestUnitAfterExpiration()
    {
        var countdown = new ValidityCountdown(
            TotalDays: -400,
            DaysRemaining: 0,
            DaysAfterExpiration: 400,
            WeeksRemaining: 0,
            WeeksAfterExpiration: 57,
            MonthsRemaining: 0,
            MonthsAfterExpiration: 13,
            YearsRemaining: 0,
            YearsAfterExpiration: 1);

        var formatted = ValidityRelativeFormatter.FormatAfterExpiration(countdown);
        Assert.Equal("1 rok", formatted);
    }

    [Theory]
    [InlineData(5, 5, 0, 0, 0, 0, 0, 0, 0, "5 dní do expirace")]
    [InlineData(1, 1, 0, 0, 0, 0, 0, 0, 0, "1 den do expirace")]
    [InlineData(10, 10, 0, 1, 0, 0, 0, 0, 0, "1 týden do expirace")]
    [InlineData(45, 45, 0, 6, 0, 1, 0, 0, 0, "1 měsíc do expirace")]
    [InlineData(500, 500, 0, 71, 0, 16, 0, 1, 0, "1 rok do expirace")]
    public void ValidityRelativeFormatter_BuildsBeforeExpirationPhrase(
        int totalDays,
        int daysRemaining,
        int daysAfterExpiration,
        int weeksRemaining,
        int weeksAfterExpiration,
        int monthsRemaining,
        int monthsAfterExpiration,
        int yearsRemaining,
        int yearsAfterExpiration,
        string expected)
    {
        var countdown = new ValidityCountdown(
            totalDays,
            daysRemaining,
            daysAfterExpiration,
            weeksRemaining,
            weeksAfterExpiration,
            monthsRemaining,
            monthsAfterExpiration,
            yearsRemaining,
            yearsAfterExpiration);

        var phrase = ValidityRelativeFormatter.FormatBeforeExpirationPhrase(countdown);
        Assert.Equal(expected, phrase);
    }

    [Theory]
    [InlineData(-1, 0, 1, 0, 0, 0, 0, 0, 0, "1 den po expiraci")]
    [InlineData(-10, 0, 10, 0, 1, 0, 0, 0, 0, "1 týden po expiraci")]
    [InlineData(-40, 0, 40, 0, 5, 0, 1, 0, 0, "1 měsíc po expiraci")]
    [InlineData(-800, 0, 800, 0, 114, 0, 26, 0, 2, "2 roky po expiraci")]
    public void ValidityRelativeFormatter_BuildsAfterExpirationPhrase(
        int totalDays,
        int daysRemaining,
        int daysAfterExpiration,
        int weeksRemaining,
        int weeksAfterExpiration,
        int monthsRemaining,
        int monthsAfterExpiration,
        int yearsRemaining,
        int yearsAfterExpiration,
        string expected)
    {
        var countdown = new ValidityCountdown(
            totalDays,
            daysRemaining,
            daysAfterExpiration,
            weeksRemaining,
            weeksAfterExpiration,
            monthsRemaining,
            monthsAfterExpiration,
            yearsRemaining,
            yearsAfterExpiration);

        var phrase = ValidityRelativeFormatter.FormatAfterExpirationPhrase(countdown);
        Assert.Equal(expected, phrase);
    }
}
