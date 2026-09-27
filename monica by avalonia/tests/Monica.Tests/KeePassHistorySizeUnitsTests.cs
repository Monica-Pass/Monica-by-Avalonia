using Monica.App.Services;

namespace Monica.Tests;

/// <summary>
/// The conversion between the byte limit a KeePass file carries and the number a person reads and types
/// in the box. The platform suite proves the database honours a byte limit; the UI suite proves the box
/// shows it. What lives here is the arithmetic in between, including the two ways it can be wrong in
/// silence: rounding a limit nobody edited, and accepting a number the other clients of the file cannot
/// carry.
/// </summary>
public sealed class KeePassHistorySizeUnitsTests
{
    private const long Megabyte = KeePassHistorySizeUnits.BytesPerMegabyte;

    [Theory]
    // The unlimited spelling is the one the file uses, and it is the only thing on screen.
    [InlineData(-1, "-1")]
    [InlineData(long.MinValue, "-1")]
    [InlineData(0, "0")]
    // A default 6 MiB limit reads as 6, not as the seven digits the file holds.
    [InlineData(6 * Megabyte, "6")]
    [InlineData(6 * Megabyte + 1, "6")]
    [InlineData(Megabyte - 1, "0")]
    [InlineData(int.MaxValue, "2047")]
    public void The_box_shows_megabytes(long bytes, string expected)
    {
        var shown = KeePassHistorySizeUnits.ToDisplayMegabytes(bytes);
        Assert.Equal(expected, shown);
        // No separators and no decimals, in any culture: the box is a plain run of digits, because a
        // thousands separator is what a person would then have to retype to change the number.
        Assert.True(
            shown.All(c => c is >= '0' and <= '9' or '-'),
            "the size box rendered something other than digits");
    }

    [Theory]
    // The truncated reading comes back as the file's own bytes, not as the round number it looks like.
    [InlineData("6", 6 * Megabyte, 6 * Megabyte)]
    [InlineData("6", 6 * Megabyte + 1, 6 * Megabyte + 1)]
    [InlineData("11", 12 * Megabyte - 1, 12 * Megabyte - 1)]
    [InlineData("0", Megabyte - 1, Megabyte - 1)]
    public void A_box_nobody_edited_gives_the_file_its_own_bytes_back(
        string text,
        long fileBytes,
        long expected)
    {
        Assert.Equal(
            text,
            KeePassHistorySizeUnits.ToDisplayMegabytes(fileBytes));
        Assert.True(KeePassHistorySizeUnits.TryParseMegabytes(text, fileBytes, out var bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("7", 6 * Megabyte, 7 * Megabyte)]
    [InlineData("1", 6 * Megabyte, Megabyte)]
    [InlineData("0", 6 * Megabyte, 0)]
    [InlineData("6", 12 * Megabyte - 1, 6 * Megabyte)]
    [InlineData("3", 5_000, 3 * Megabyte)]
    [InlineData("-1", 6 * Megabyte, -1)]
    public void A_number_typed_into_the_box_becomes_that_many_bytes(
        string text,
        long fileBytes,
        long expected)
    {
        Assert.NotEqual(
            text,
            KeePassHistorySizeUnits.ToDisplayMegabytes(fileBytes));
        Assert.True(KeePassHistorySizeUnits.TryParseMegabytes(text, fileBytes, out var bytes));
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void A_number_still_converts_when_no_database_is_open_to_preserve()
    {
        Assert.True(KeePassHistorySizeUnits.TryParseMegabytes("6", null, out var six));
        Assert.Equal(6 * Megabyte, six);
        Assert.True(KeePassHistorySizeUnits.TryParseMegabytes(" 6 ", null, out var spaced));
        Assert.Equal(6 * Megabyte, spaced);
        Assert.True(KeePassHistorySizeUnits.TryParseMegabytes("-1", null, out var unlimited));
        Assert.Equal(-1, unlimited);
    }

    [Theory]
    // One over what the other clients of this file can hold in their 32-bit field.
    [InlineData("2048")]
    [InlineData("99999999999999999999999")]
    // Below the unlimited spelling, and past the bottom of the field it would be stored in.
    [InlineData("-2")]
    [InlineData("-9223372036854775808")]
    // Not a plain integer: a decimal, a separator, an exponent, a radix, or nothing.
    [InlineData("6.5")]
    [InlineData("1,048")]
    [InlineData("1e2")]
    [InlineData("0x10")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("six")]
    [InlineData("6 MB")]
    public void A_size_the_box_has_no_spelling_for_is_refused(string text)
    {
        Assert.False(
            KeePassHistorySizeUnits.TryParseMegabytes(text, 6 * Megabyte, out var bytes),
            "the size box accepted a number the file has no spelling for");
        Assert.Equal(0L, bytes);
    }

    [Fact]
    public void The_ceiling_is_the_number_the_other_clients_carry_and_the_message_names()
    {
        Assert.Equal(2047, (int)KeePassHistorySizeUnits.MaximumMegabytes);
        Assert.True(
            KeePassHistorySizeUnits.MaximumMegabytes * KeePassHistorySizeUnits.BytesPerMegabyte
                <= int.MaxValue,
            "the accepted size no longer fits the field the other clients of this file hold it in");
    }

    [Fact]
    public void Every_number_the_box_can_show_round_trips_through_it()
    {
        for (var megabytes = 0L; megabytes <= KeePassHistorySizeUnits.MaximumMegabytes; megabytes++)
        {
            var aligned = megabytes * Megabyte;
            Assert.Equal(megabytes.ToString(), KeePassHistorySizeUnits.ToDisplayMegabytes(aligned));
            Assert.True(KeePassHistorySizeUnits.TryParseMegabytes(megabytes.ToString(), aligned, out var kept));
            Assert.Equal(aligned, kept);

            // Just under the next megabyte: still the same reading, and still the file's own bytes.
            var offByOne = aligned + 1;
            if (offByOne < (megabytes + 1) * Megabyte)
            {
                Assert.Equal(megabytes.ToString(), KeePassHistorySizeUnits.ToDisplayMegabytes(offByOne));
                Assert.True(
                    KeePassHistorySizeUnits.TryParseMegabytes(megabytes.ToString(), offByOne, out var held));
                Assert.Equal(offByOne, held);
            }
        }
    }
}
