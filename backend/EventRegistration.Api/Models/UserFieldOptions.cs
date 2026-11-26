namespace EventRegistration.Api.Models;

// The "UserFields" section of user-fields.json: optional profile fields, on or off per installation.
public class UserFieldOptions
{
    public const int DepartmentMaxLength = 100;
    public const int JobRoleMaxLength = 200;

    public UserFieldToggle Department { get; set; } = new();
    public UserFieldToggle JobRole { get; set; } = new();
}

public class UserFieldToggle
{
    public bool Enabled { get; set; }
}
