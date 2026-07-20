using Monica.Core.Categories;
using Monica.Core.Models;

namespace Monica.Tests;

public sealed class LocalCategoryPathTests
{
    [Fact]
    public void BuildOptions_projects_virtual_parents_and_real_nested_targets()
    {
        var categories = new[]
        {
            new Category { Id = 1, Name = "Work/Production" },
            new Category { Id = 2, Name = "Work\\Development\\Cloud" }
        };

        var options = LocalCategoryPath.BuildOptions(categories);

        Assert.Equal(
            ["Work", "Work/Development", "Work/Development/Cloud", "Work/Production"],
            options.Select(option => option.Path));
        Assert.Null(options[0].Category);
        Assert.Equal(2, options[2].Depth);
        Assert.Equal("Work/Development", options[2].ParentPath);
        Assert.Equal(2, options[2].Category?.Id);
    }

    [Fact]
    public void Build_defaults_new_folder_to_selected_parent()
    {
        Assert.Equal("Work/Production/Secrets", LocalCategoryPath.Build("Work/Production", " Secrets "));
    }

    [Fact]
    public void PlanSubtreeRename_preserves_descendants_and_reports_conflicts()
    {
        var parent = new Category { Id = 1, Name = "Work" };
        var child = new Category { Id = 2, Name = "Work/Production" };
        var occupied = new Category { Id = 3, Name = "Personal/Production" };

        var valid = LocalCategoryPath.PlanSubtreeRename([parent, child, occupied], parent, "Team");
        var conflict = LocalCategoryPath.PlanSubtreeRename([parent, child, occupied], parent, "Personal");

        Assert.False(valid.HasConflict);
        Assert.Equal("Team", valid.UpdatedPaths[parent.Id]);
        Assert.Equal("Team/Production", valid.UpdatedPaths[child.Id]);
        Assert.True(conflict.HasConflict);
        Assert.Equal("Personal/Production", conflict.ConflictPath);
    }
}
