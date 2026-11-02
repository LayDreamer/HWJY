using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class MenuSeedTests
{
    [Fact]
    public void EverySeedMenuHasAnImplementedViewAndNoRetiredGrantsRemain()
    {
        var root = RepositoryBoundaryTests.RepositoryRoot;
        var seeds = Path.Combine(root, "src/modules/admin/ZhonTai.Admin.Host/InitData/Admin");
        using var permissionFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(seeds, "base_permission.json")));
        using var viewFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(seeds, "base_view.json")));
        var permissions = Flatten(permissionFile.RootElement).ToArray();
        var views = Flatten(viewFile.RootElement).ToDictionary(v => v.GetProperty("id").GetInt64());
        var ids = permissions.Select(p => p.GetProperty("id").GetInt64()).ToHashSet();
        foreach (var menu in permissions.Where(p => p.GetProperty("type").GetInt32() == 2))
        {
            if (menu.TryGetProperty("external", out var external) && external.ValueKind == JsonValueKind.True) continue;
            var viewId = menu.GetProperty("viewId").GetInt64();
            Assert.True(views.ContainsKey(viewId), "Missing seed view: " + viewId);
            var path = views[viewId].GetProperty("path").GetString();
            Assert.True(File.Exists(Path.Combine(root, "ui/zhontai.ui.admin.vue3/src/views", path + ".vue")) ||
                        File.Exists(Path.Combine(root, "ui/zhontai.ui.admin.vue3/src", path + ".vue")), "Unimplemented seed view: " + path);
        }
        foreach (var name in new[] { "base_permission_api.json", "base_role_permission.json", "base_pkg_permission.json" })
        {
            var file = Path.Combine(seeds, name);
            if (!File.Exists(file)) continue;
            using var relations = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var row in relations.RootElement.EnumerateArray())
                Assert.Contains(row.GetProperty("permissionId").GetInt64(), ids);
        }
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement rows)
    {
        foreach (var row in rows.EnumerateArray())
        {
            yield return row;
            if (row.TryGetProperty("childs", out var children) && children.ValueKind == JsonValueKind.Array)
                foreach (var child in Flatten(children)) yield return child;
        }
    }
}
