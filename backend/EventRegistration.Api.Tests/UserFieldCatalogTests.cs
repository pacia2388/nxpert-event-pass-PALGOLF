using System.Net.Http.Json;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Tests;

// Department / Job role are optional and off by default; a disabled field is ignored and never
// shown, an enabled one is trimmed and length-checked, and an edit applies without a restart.
public class UserFieldCatalogTests
{
    private sealed class MutableOptions(UserFieldOptions value) : IOptionsMonitor<UserFieldOptions>
    {
        public UserFieldOptions CurrentValue { get; set; } = value;
        public UserFieldOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<UserFieldOptions, string?> listener) => null;
    }

    private static UserFieldCatalog Catalog(bool department = false, bool jobRole = false) =>
        new(new MutableOptions(new UserFieldOptions
        {
            Department = new UserFieldToggle { Enabled = department },
            JobRole = new UserFieldToggle { Enabled = jobRole },
        }));

    [Fact]
    public void Both_fields_are_off_by_default_and_their_input_is_ignored()
    {
        var catalog = Catalog();

        Assert.False(catalog.DepartmentEnabled);
        Assert.False(catalog.JobRoleEnabled);
        Assert.Null(catalog.CleanDepartment("Finance", out var departmentError));
        Assert.Null(departmentError);
        Assert.Null(catalog.CleanJobRole("Analyst", out var jobRoleError));
        Assert.Null(jobRoleError);
        Assert.All(catalog.List(), f => Assert.False(f.Enabled));
    }

    [Fact]
    public void Enabled_fields_are_trimmed_and_saved()
    {
        var catalog = Catalog(department: true, jobRole: true);

        Assert.Equal("Finance", catalog.CleanDepartment("  Finance ", out _));
        Assert.Equal("Senior Analyst", catalog.CleanJobRole(" Senior Analyst ", out _));
        Assert.Null(catalog.CleanDepartment("   ", out _)); // blank clears the value
    }

    [Fact]
    public void A_too_long_value_is_refused()
    {
        var catalog = Catalog(department: true, jobRole: true);

        Assert.Null(catalog.CleanDepartment(new string('x', 101), out var departmentError));
        Assert.Contains("100 characters", departmentError);
        Assert.Null(catalog.CleanJobRole(new string('x', 201), out var jobRoleError));
        Assert.Contains("200 characters", jobRoleError);
    }

    [Fact]
    public void Turning_a_field_off_after_it_was_on_keeps_reading_the_new_setting()
    {
        var options = new MutableOptions(new UserFieldOptions
        {
            Department = new UserFieldToggle { Enabled = true },
        });
        var catalog = new UserFieldCatalog(options);
        Assert.True(catalog.DepartmentEnabled);

        options.CurrentValue = new UserFieldOptions
        {
            Department = new UserFieldToggle { Enabled = false },
        };

        Assert.False(catalog.DepartmentEnabled);
        Assert.Null(catalog.CleanDepartment("Finance", out _));
    }
}

// With both fields off (the default, pinned by ApiFixture): input is ignored and nothing is shown.
[Collection("api")]
public class UserFieldDisabledTests(ApiFixture api)
{
    [Fact]
    public async Task Disabled_fields_are_ignored_on_create_and_not_returned()
    {
        var admin = await api.AdminAsync();
        var fields = (await admin.GetFromJsonAsync<List<FieldBody>>("/api/settings/user-fields"))!;
        Assert.All(fields, f => Assert.False(f.Enabled));

        var email = ApiFixture.NewEmail();
        var created = await admin.PostAsJsonAsync("/api/users", new
        {
            firstName = "No",
            lastName = "Fields",
            email,
            password = ApiFixture.UserPassword,
            role = "Participant",
            birthdate = "1990-05-01",
            canConfirmRegistrations = false,
            department = "Finance",
            jobRole = "Analyst",
        });
        created.EnsureSuccessStatusCode();
        var user = (await created.Content.ReadFromJsonAsync<UserFieldBody>())!;

        Assert.Null(user.Department);
        Assert.Null(user.JobRole);
    }

    private sealed record FieldBody(string Name, bool Enabled);
    private sealed record UserFieldBody(string? Department, string? JobRole);
}
