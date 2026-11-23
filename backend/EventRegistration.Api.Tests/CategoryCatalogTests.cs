using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Tests;

// The category list comes from categories.json: each client sets its own, or none.
public class CategoryCatalogTests
{
    private sealed class Fixed(CategoryOptions value) : IOptionsMonitor<CategoryOptions>
    {
        public CategoryOptions CurrentValue => value;
        public CategoryOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<CategoryOptions, string?> listener) => null;
    }

    private static CategoryCatalog Catalog(params CategoryItem[] items) =>
        new(new Fixed(new CategoryOptions { Items = [.. items], DefaultTeamMaxMembers = 12 }));

    private static readonly CategoryItem Senior = new() { Name = "Senior", Icon = "elderly", TeamMaxMembers = 8 };
    private static readonly CategoryItem Junior = new() { Name = "Junior" };

    [Theory]
    [InlineData(" junior ", "Junior")]
    [InlineData("SENIOR", "Senior")]
    public void An_event_category_is_trimmed_matched_ignoring_case_and_saved_as_listed(string sent, string saved)
    {
        Assert.True(Catalog(Senior, Junior).TryEventCategory(sent, null, out var result));
        Assert.Equal(saved, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void An_event_may_have_no_category(string? sent)
    {
        Assert.True(Catalog(Senior).TryEventCategory(sent, "Senior", out var result));
        Assert.Null(result);
    }

    [Fact]
    public void An_event_category_not_in_the_list_is_refused_unless_the_event_already_has_it()
    {
        var catalog = Catalog(Senior);

        Assert.False(catalog.TryEventCategory("Regular", null, out _));
        Assert.True(catalog.TryEventCategory("regular", "Regular", out var kept));
        Assert.Equal("Regular", kept);
    }

    [Fact]
    public void With_no_categories_an_event_takes_none()
    {
        var catalog = Catalog();

        Assert.False(catalog.Enabled);
        Assert.False(catalog.TryEventCategory("Senior", null, out _));
        Assert.True(catalog.TryEventCategory("", null, out var none));
        Assert.Null(none);
    }

    [Fact]
    public void A_team_needs_a_listed_category_while_there_are_categories()
    {
        var catalog = Catalog(Senior, Junior);

        Assert.True(catalog.TryTeamCategory("junior", null, out var saved));
        Assert.Equal("Junior", saved);
        Assert.False(catalog.TryTeamCategory("", null, out _));
        Assert.False(catalog.TryTeamCategory("Regular", null, out _));
        Assert.True(catalog.TryTeamCategory("Regular", "Regular", out var kept));
        Assert.Equal("Regular", kept);
    }

    [Fact]
    public void With_no_categories_a_team_has_none()
    {
        var catalog = Catalog();

        Assert.True(catalog.TryTeamCategory(null, null, out var saved));
        Assert.Equal("", saved);
        Assert.False(catalog.TryTeamCategory("Senior", null, out _));
        Assert.True(catalog.TryTeamCategory("Senior", "Senior", out var kept)); // older team keeps it
        Assert.Equal("Senior", kept);
    }

    [Theory]
    [InlineData("Senior", 8)]
    [InlineData("senior", 8)]
    [InlineData("Junior", 12)] // listed without a cap: the default
    [InlineData("Regular", 12)] // no longer listed
    [InlineData("", 12)]
    public void A_team_holds_its_category_cap_or_the_default(string category, int max) =>
        Assert.Equal(max, Catalog(Senior, Junior).TeamMaxMembers(category));

    [Fact]
    public void A_listed_category_without_an_icon_gets_the_default_icon()
    {
        var listed = Catalog(Senior, Junior).List();

        Assert.Equal(["elderly", "label"], listed.Select(c => c.Icon));
        Assert.Equal([8, 12], listed.Select(c => c.TeamMaxMembers));
    }

    [Fact]
    public void A_good_file_has_no_errors() =>
        Assert.Empty(new CategoryOptions { Items = [Senior, Junior] }.Errors());

    [Fact]
    public void A_bad_file_names_every_problem()
    {
        var errors = new CategoryOptions
        {
            DefaultTeamMaxMembers = 0,
            Items =
            [
                new() { Name = "Senior" },
                new() { Name = " senior " },
                new() { Name = " " },
                new() { Name = new string('x', 21) },
                new() { Name = "Pro", TeamMaxMembers = 0 },
            ],
        }.Errors();

        Assert.Equal(5, errors.Count);
        Assert.Contains(errors, e => e.Contains("senior") && e.Contains("more than once"));
        Assert.Contains(errors, e => e.Contains("blank"));
        Assert.Contains(errors, e => e.Contains("20 characters"));
        Assert.Contains(errors, e => e.Contains("Pro") && e.Contains("at least 1"));
        Assert.Contains(errors, e => e.Contains("DefaultTeamMaxMembers"));
    }
}
