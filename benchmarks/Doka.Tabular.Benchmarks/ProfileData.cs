using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Doka.Tabular.Benchmarks;

/// <summary>Generates the records of a profile from its parameters and seed; the same profile gives the same records.</summary>
public static class ProfileData
{
    // WHY: Texts mix letters, digits, CSV delimiters and quotes, umlauts, accents, and CJK characters, so the formats
    // escape, quote, and encode; the characters beyond ASCII are given as codes, so the source stays ASCII.
    private static readonly string s_textAlphabet =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_.,;:\"'"
        + new string(
        [
            (char)0x00E4, (char)0x00F6, (char)0x00FC, (char)0x00DF, (char)0x00E9, (char)0x00E8, (char)0x4E2D,
            (char)0x6587,
        ]);

    public static IEnumerable<ProfileRow> Standard(
        ReferenceProfile profile
    )
    {
        var random = new Random(profile.Seed);
        for (var index = 1; index <= profile.Rows; index++)
        {
            yield return NextStandard(random, index, profile);
        }
    }

    /// <summary>The standard records, produced one by one as an asynchronous source that keeps generating.</summary>
    public static async IAsyncEnumerable<ProfileRow> StandardAsync(
        ReferenceProfile profile,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var random = new Random(profile.Seed);
        for (var index = 1; index <= profile.Rows; index++)
        {
            if (index % 1_000 == 0)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }

            yield return NextStandard(random, index, profile);
        }
    }

    public static IEnumerable<WideRow> Wide(
        ReferenceProfile profile
    )
    {
        var random = new Random(profile.Seed);
        var parts = new ProfileRow[WideRow.Parts];
        for (var index = 0; index < profile.Rows; index++)
        {
            for (var part = 0; part < parts.Length; part++)
            {
                parts[part] = NextStandard(random, index * WideRow.Parts + part + 1, profile);
            }

            yield return WideRow.Of(parts);
        }
    }

    public static IEnumerable<ConversionRow> Conversions(
        ReferenceProfile profile
    )
    {
        var random = new Random(profile.Seed);
        var start = new DateTime(
            2024,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Unspecified);

        for (var index = 1; index <= profile.Rows; index++)
        {
            var offset = TimeSpan.FromHours(random.Next(-12, 15));
            yield return new ConversionRow(
                index,
                random.Next(-1_000_000, 1_000_000) / 100m,
                random.Next(0, 1_001) / 1000m,
                new DateTimeOffset(start.AddMinutes(random.Next(0, 525_600)), offset),
                TimeSpan.FromSeconds(random.Next(0, 259_200)),
                Pick(random, Rights.Read) | Pick(random, Rights.Write) | Pick(random, Rights.Delete),
                random.Next(2) == 1,
                DateOnly
                    .FromDateTime(start)
                    .AddDays(random.Next(3_650)));
        }
    }

    /// <summary>
    /// The standard records as written with invalid amounts; the record numbers of the invalid ones follow from the
    /// seed of the error distribution.
    /// </summary>
    public static (List<ErrorSourceRow> Rows, List<ProfileRow> Valid, List<long> Invalid) WithErrors(
        ReferenceProfile profile
    )
    {
        var errors = new Random(profile.Seed + 1);
        var rows = new List<ErrorSourceRow>(profile.Rows);
        var valid = new List<ProfileRow>(profile.Rows);
        var invalid = new List<long>();
        foreach (var row in Standard(profile))
        {
            var broken = errors.Next(1_000) < profile.ErrorsPerThousand;
            rows.Add(
                new ErrorSourceRow(
                    row.Id,
                    row.Name,
                    broken
                        ? "invalid"
                        : row.Amount.ToString(CultureInfo.InvariantCulture),
                    row.Date,
                    row.Active,
                    row.PostalCode,
                    row.Key));

            if (broken)
            {
                invalid.Add(row.Id);
            }
            else
            {
                valid.Add(row);
            }
        }

        return (rows, valid, invalid);
    }

    private static ProfileRow NextStandard(
        Random random,
        int index,
        ReferenceProfile profile
    )
    {
        var name = profile.Shape == ProfileShape.Texts
            ? Text(random, 1 + random.Next(profile.TextLength))
            : new string((char)('a' + random.Next(26)), profile.TextLength);

        var key = new byte[16];
        random.NextBytes(key);

        return new ProfileRow(
            index,
            name,
            random.Next(0, 1_000_000) / 100m,
            new DateOnly(2024, 1, 1).AddDays(random.Next(365)),
            random.Next(2) == 1,
            random
                .Next(0, 99999)
                .ToString("00000", CultureInfo.InvariantCulture),
            new Guid(key));
    }

    private static Rights Pick(
        Random random,
        Rights right
    ) => random.Next(2) == 1
        ? right
        : Rights.None;

    private static string Text(
        Random random,
        int length
    )
    {
        var text = new StringBuilder(length);
        for (var position = 0; position < length; position++)
        {
            text.Append(s_textAlphabet[random.Next(s_textAlphabet.Length)]);
        }

        return text.ToString();
    }
}
