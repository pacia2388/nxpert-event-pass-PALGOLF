namespace EventRegistration.Api.Models;

// The password a player chooses: 8–128 characters with at least one letter, one digit and one
// symbol (any character that is not a letter, digit or space), and not their email address.
// Checked in Unicode NFC, as PasswordHasher hashes it, so "n" plus a combining tilde is the
// letter "ñ", not a letter and a symbol.
public static class PasswordRules
{
    public const string Message =
        "Password must be 8 to 128 characters with a letter, a digit and a symbol, and not your email.";

    public static string? Validate(string? password, string email) =>
        Check(password?.Normalize(), email);

    private static string? Check(string? password, string email) =>
        password is null || password.Length < 8 || password.Length > 128
            || !password.Any(char.IsLetter) || !password.Any(char.IsDigit) || !password.Any(IsSymbol)
            || string.Equals(password, email.Normalize(), StringComparison.OrdinalIgnoreCase)
            ? Message
            : null;

    private static bool IsSymbol(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);
}
