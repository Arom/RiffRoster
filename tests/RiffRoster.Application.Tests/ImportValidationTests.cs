using System.Text;
using System.Text.Json;

namespace RiffRoster.Application.Tests;

public sealed class ImportValidationTests
{
    private static byte[] Fixture() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "store-v7.json"));
    [Fact]
    public void Source_validation_does_not_mutate_bytes_and_stable_ids_are_framed()
    {
        var bytes = Fixture();
        var copy = bytes.ToArray();
        var first = JsonImport.Prepare(bytes);
        var second = JsonImport.Prepare(bytes);
        Assert.Equal(first.Report.CanonicalSha256, second.Report.CanonicalSha256);
        Assert.Equal(copy, bytes);
        Assert.Equal(StableImportId.For("performance", "fixture"), StableImportId.For("performance", "fixture"));
        Assert.NotEqual(StableImportId.For("group", "fixture"), StableImportId.For("performance", "fixture"));
        Assert.NotEqual(StableImportId.For("a|b", "c"), StableImportId.For("a", "b|c"));
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"dataVersion\":4}")]
    [InlineData("{\"dataVersion\":7}")]
    [InlineData("{\"dataVersion\":7,\"dataVersion\":7}")]
    public void Invalid_source_fails_without_database_access(string input)
        => Assert.ThrowsAny<Exception>(() => JsonImport.Prepare(Encoding.UTF8.GetBytes(input)));

    [Fact]
    public void Orphan_and_duplicate_references_fail_instead_of_losing_rows()
    {
        var source = JsonImport.Prepare(Fixture());
        source.State.Selections.Add(new UserSelection("missing", "fixture-performance"));
        Assert.Throws<InvalidDataException>(() => StateAudit.Report(source.State));
        source = JsonImport.Prepare(Fixture());
        source.State.GroupMemberships.Add(source.State.GroupMemberships[0]);
        Assert.Throws<InvalidDataException>(() => StateAudit.Report(source.State));
    }
    [Fact]
    public void Canonical_hash_detects_edits_beyond_counts_and_preserves_schedule_offsets()
    {
        var source = JsonImport.Prepare(Fixture());
        var before = StateAudit.Report(source.State);
        var day = source.State.Events[0].Days[0];
        day.Performances[0] = day.Performances[0] with { Artist = "Changed" };
        var after = StateAudit.Report(source.State);
        Assert.Equal(before.Counts, after.Counts);
        Assert.NotEqual(before.CanonicalSha256, after.CanonicalSha256);
        day.Performances[0] = day.Performances[0] with { StartsAt = day.Performances[0].StartsAt.ToUniversalTime() };
        Assert.NotEqual(after.CanonicalSha256, StateAudit.Report(source.State).CanonicalSha256);
    }

    [Fact]
    public void Import_rejects_unknown_fields_and_offsetless_instants_and_accepts_utf8_bom()
    {
        var original = Fixture();
        var text = Encoding.UTF8.GetString(original);
        Assert.ThrowsAny<Exception>(() => JsonImport.Prepare(Encoding.UTF8.GetBytes(text.Replace("\"dataVersion\": 7", "\"dataVersion\": 7, \"unknownData\": 42"))));
        Assert.Throws<InvalidDataException>(() => JsonImport.Prepare(Encoding.UTF8.GetBytes(text.Replace("2026-08-04T20:00:00+02:00", "2026-08-04T20:00:00"))));
        Assert.Throws<InvalidDataException>(() => JsonImport.Prepare(Encoding.UTF8.GetBytes(text.Replace("startsAt", "StartsAt").Replace("2026-08-04T20:00:00+02:00", "2026-08-04T20:00:00"))));
        var withBom = Encoding.UTF8.GetPreamble().Concat(original).ToArray();
        Assert.Equal(JsonImport.Prepare(original).Report.CanonicalSha256, JsonImport.Prepare(withBom).Report.CanonicalSha256);
    }
}
