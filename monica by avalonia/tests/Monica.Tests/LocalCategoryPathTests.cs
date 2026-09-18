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

    [Fact]
    public void PlanSubtreeMove_reparents_a_folder_with_its_descendants()
    {
        var work = new Category { Id = 1, Name = "Work" };
        var production = new Category { Id = 2, Name = "Work/Production" };
        var personal = new Category { Id = 3, Name = "Personal" };

        var plan = LocalCategoryPath.PlanSubtreeMove([work, production, personal], work, "Personal");

        Assert.NotNull(plan);
        Assert.False(plan!.HasConflict);
        Assert.Equal("Personal/Work", plan.DestinationPath);
        Assert.Equal("Personal/Work", plan.UpdatedPaths[work.Id]);
        Assert.Equal("Personal/Work/Production", plan.UpdatedPaths[production.Id]);
    }

    [Fact]
    public void PlanSubtreeMove_refuses_moves_that_change_nothing_or_create_a_cycle()
    {
        var work = new Category { Id = 1, Name = "Work" };
        var production = new Category { Id = 2, Name = "Work/Production" };

        var intoOwnChild = LocalCategoryPath.PlanSubtreeMove([work, production], work, "Work/Production");
        var underCurrentParent = LocalCategoryPath.PlanSubtreeMove([work, production], production, "Work");

        Assert.Null(intoOwnChild);
        Assert.Null(underCurrentParent);
    }

    [Fact]
    public void PlanSubtreeMove_reports_a_name_clash_at_the_destination()
    {
        var team = new Category { Id = 1, Name = "Team" };
        var personal = new Category { Id = 2, Name = "Personal" };
        var personalTeam = new Category { Id = 3, Name = "Personal/Team" };

        var plan = LocalCategoryPath.PlanSubtreeMove([team, personal, personalTeam], team, "Personal");

        Assert.NotNull(plan);
        Assert.True(plan!.HasConflict);
        Assert.Equal("Personal/Team", plan.ConflictPath);
    }

    [Fact]
    public void PlanSubtreeMove_lifts_a_nested_folder_to_the_root_without_a_destination_parent()
    {
        var work = new Category { Id = 1, Name = "Work" };
        var development = new Category { Id = 2, Name = "Work/Development" };
        var cloud = new Category { Id = 3, Name = "Work/Development/Cloud" };

        var plan = LocalCategoryPath.PlanSubtreeMove([work, development, cloud], development, null);

        Assert.NotNull(plan);
        Assert.False(plan!.HasConflict);
        Assert.Equal("Development", plan.DestinationPath);
        Assert.Equal("Development/Cloud", plan.UpdatedPaths[cloud.Id]);
    }
}
