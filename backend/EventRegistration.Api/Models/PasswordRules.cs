namespace EventRegistration.Api.Models;

// The password a player chooses: 8–128 characters, at least one letter and one digit, and not
// their email address.
public static class PasswordRules
{
    public const string Message = "Password must be 8 to 128 characters with a letter and a digit, and not your email.";

    public static string? Validate(string? password, string email) =>
        password is null || password.Length < 8 || password.Length > 128
            || !password.Any(char.IsLetter) || !password.Any(char.IsDigit)
            || string.Equals(password, email, StringComparison.OrdinalIgnoreCase)
            ? Message
            : null;
}
