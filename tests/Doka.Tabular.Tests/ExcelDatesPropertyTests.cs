using FsCheck.Xunit;

namespace Doka.Tabular.Tests;

/// <summary>
/// The 128-bit conversion of serial numbers to ticks must decide exactly like the decimal conversion. Every run draws
/// new values; a failure is a real defect, and its reported counterexample becomes a fixed test before the fix.
/// </summary>
public sealed class ExcelDatesPropertyTests
{
    private const double MaxSerial = 2_958_466;

    private static readonly long[] s_steps =
    [
        1,
        10,
        100,
        1_000,
        TimeSpan.TicksPerMillisecond,
        TimeSpan.TicksPerSecond,
        3 * TimeSpan.TicksPerSecond,
        TimeSpan.TicksPerMinute,
        TimeSpan.TicksPerHour,
        TimeSpan.TicksPerDay,
    ];

    private static readonly MidpointRounding[] s_modes = Enum.GetValues<MidpointRounding>();

    [Property(MaxTest = 2_000)]
    public void BinaryPath_DecidesLikeTheDecimalPath(
        uint whole,
        double fraction,
        byte stepChoice,
        byte modeChoice
    )
    {
        // Arrange
        var days = whole % MaxSerial
            + (double.IsFinite(fraction)
                ? Math.Abs(fraction % 1)
                : 0);

        var step = s_steps[stepChoice % s_steps.Length];
        var mode = s_modes[modeChoice % s_modes.Length];

        // Act
        var binaryDecided = ExcelDates.TryTicksBinary(days, step, mode, out var binary);
        var exactDecided = ExcelDates.TryTicksExact(days, step, mode, out var exact);

        // Assert
        // WHY: The binary path may decline a value the decimal path decides; when it decides, it must agree.
        Assert.True(!binaryDecided || (exactDecided && exact == binary), $"{days} {step} {mode}: {binary} != {exact}");
    }
}
