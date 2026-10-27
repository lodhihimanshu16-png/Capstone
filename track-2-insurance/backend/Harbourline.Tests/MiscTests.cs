using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Harbourline.Tests;

/// <summary>AC-45 to AC-47: card numbers in notes, password hashes, and the bordereau import.</summary>
public class MiscTests
{
    [Fact]
    public async Task AC_45_NoteWithCardNumberIsRefused()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "Assessing"; s.HandlerId = Ids.Ben; });

        // 4111 1111 1111 1111 passes the Luhn check. 1234567890123456 has 16 digits but fails it (checksum 64).
        var card = await c.PostAsJsonAsync($"/api/claims/{id}/notes",
            new { authorUserId = Ids.Ben, noteType = "Update", body = "Customer read out card 4111 1111 1111 1111 on the phone" });
        await card.AssertProblem(HttpStatusCode.UnprocessableEntity);
        Assert.Equal(0, (await c.GetJson($"/api/claims/{id}/notes")).GetArrayLength());

        var order = await c.PostAsJsonAsync($"/api/claims/{id}/notes",
            new { authorUserId = Ids.Ben, noteType = "Update", body = "Repairer order number 1234567890123456 confirmed" });
        Assert.True(order.IsSuccessStatusCode, $"note with a non-card 16 digit number returned {(int)order.StatusCode}");
        var notes = (await c.GetJson($"/api/claims/{id}/notes")).Rows();
        var saved = Assert.Single(notes);
        Assert.Contains("1234567890123456", saved.Str("body"));
    }

    [Fact]
    public async Task AC_46_UsersEndpointDoesNotReturnPasswordHash()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();

        var list = await c.GetAsync("/api/users");
        var one = await c.GetAsync($"/api/users/{Ids.Priya}");

        foreach (var response in new[] { list, one })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Priya Raman", body);   // the endpoint still returns the users
            Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Ids.SecretHash, body);
        }
    }

    // Characterisation of the importer as it is today, derived by reading BordereauImportService against the sample
    // file and the seeded policies in ApiFactory: 10 data rows, 4 created (rows 1, 2, 3 and 8), 0 updated and 6
    // rejected (row 4 policy not found, 5 bad date, 6 reported before loss, 7 negative amount, 9 duplicate
    // ExternalRef, 10 loss before the policy starts). If the importer is changed on purpose these numbers change.
    [Fact]
    public async Task AC_47_BordereauImportStillWorks()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var csv = await File.ReadAllBytesAsync(FindSample());
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "bordereau-sample.csv");
        form.Add(new StringContent("Test broker"), "source");

        var result = await (await c.PostAsync("/api/imports/bordereau", form)).AssertOk();

        Assert.Equal(10, result.Int("totalRows"));
        Assert.Equal(4, result.Int("created"));
        Assert.Equal(0, result.Int("updated"));
        Assert.Equal(6, result.Int("rejected"));
        var list = await c.GetJson("/api/claims?pageSize=100");
        Assert.Equal(4, list.Int("total"));
        var items = list.GetProperty("items").Rows();
        Assert.Equal(4, items.Count);
        Assert.All(items, i => Assert.Equal("New", i.Str("status")));
    }

    private static string FindSample()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, "docs", "samples", "bordereau-sample.csv");
            if (File.Exists(path)) return path;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("docs/samples/bordereau-sample.csv was not found above " + AppContext.BaseDirectory);
    }
}
