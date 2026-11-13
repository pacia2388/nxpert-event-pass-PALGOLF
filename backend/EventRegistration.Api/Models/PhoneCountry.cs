using System.ComponentModel.DataAnnotations;

namespace EventRegistration.Api.Models;

/// <summary>
/// A country code users may save phone numbers with (admin-managed; the codes are the choices in
/// the phone control). Each channel flag says whether that channel may send to the code's numbers.
/// A new channel is a new bool column here (it needs its own sender code anyway).
/// </summary>
public class PhoneCountry
{
    public int Id { get; set; }
    // Digits only, no "+", 1–4 digits, e.g. "63", "1".
    [MaxLength(4)] public string CountryCode { get; set; } = "";
    public bool Viber { get; set; }
    public bool Sms { get; set; }
}
