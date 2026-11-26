using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventRegistration.Api.Controllers;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventRegistration.Api.Tests;

// The uploaded display photo and the face-recognition capture are separate: recognition uses
// FacePhoto, while the TV, pass and check-in result only ever show PhotoUrl / GuestPhoto.
[Collection("api")]
public class SeparateFacePhotoTests(ApiFixture api)
{
    // A different random descriptor per call: the duplicate-face guard rejects a shared one.
    private static string FaceDescriptor() =>
        JsonSerializer.Serialize(Enumerable.Range(0, 128).Select(_ => Random.Shared.NextSingle()).ToArray());
    private const string DisplayPhoto = "data:image/jpeg;base64,ZGlzcGxheQ==";
    private const string FacePhoto = "data:image/jpeg;base64,ZmFjZQ==";

    private static async Task<User> UserRow(ApiFixture api, int id)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    [Fact]
    public async Task A_face_capture_does_not_change_the_display_photo()
    {
        var admin = await api.AdminAsync();
        var email = ApiFixture.NewEmail();
        var created = await admin.PostAsJsonAsync("/api/users", new
        {
            firstName = "Face",
            lastName = "Split",
            email,
            password = ApiFixture.UserPassword,
            role = "Participant",
            birthdate = "1990-05-01",
            canConfirmRegistrations = false,
            photo = DisplayPhoto,
        });
        created.EnsureSuccessStatusCode();
        var user = (await created.Content.ReadFromJsonAsync<UserBody>())!;
        var client = await api.SignInAsync(email, ApiFixture.UserPassword);

        var update = await client.PutAsJsonAsync("/api/auth/me", new
        {
            firstName = "Face",
            lastName = "Split",
            email,
            notificationChannel = "Email",
            birthdate = "1990-05-01",
            photo = (string?)null, // leave the uploaded photo as it is
            face = FaceDescriptor(),
            facePhoto = FacePhoto,
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var row = await UserRow(api, user.Id);
        Assert.Equal(DisplayPhoto, row.PhotoUrl);
        Assert.Equal(FacePhoto, row.FacePhoto);
        Assert.NotNull(row.FaceDescriptor);

        // The profile response still carries only the uploaded display photo.
        var me = await client.GetFromJsonAsync<ProfileBody>("/api/auth/me");
        Assert.Equal(DisplayPhoto, me!.PhotoUrl);
    }

    [Fact]
    public async Task Withdrawing_the_face_clears_the_capture_and_keeps_the_display_photo()
    {
        var admin = await api.AdminAsync();
        var email = ApiFixture.NewEmail();
        var created = await admin.PostAsJsonAsync("/api/users", new
        {
            firstName = "Face",
            lastName = "Clear",
            email,
            password = ApiFixture.UserPassword,
            role = "Participant",
            birthdate = "1990-05-01",
            canConfirmRegistrations = false,
            photo = DisplayPhoto,
            face = FaceDescriptor(),
            facePhoto = FacePhoto,
        });
        created.EnsureSuccessStatusCode();
        var user = (await created.Content.ReadFromJsonAsync<UserBody>())!;
        var client = await api.SignInAsync(email, ApiFixture.UserPassword);

        var update = await client.PutAsJsonAsync("/api/auth/me", new
        {
            firstName = "Face",
            lastName = "Clear",
            email,
            notificationChannel = "Email",
            birthdate = "1990-05-01",
            face = "", // withdraw consent
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var row = await UserRow(api, user.Id);
        Assert.Equal(DisplayPhoto, row.PhotoUrl);
        Assert.Null(row.FacePhoto);
        Assert.Null(row.FaceDescriptor);
    }

    [Fact]
    public async Task The_face_gallery_prefers_the_capture_over_the_display_photo()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var user = await api.CreateUserAsync(admin, "Participant");

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.SingleAsync(u => u.Id == user.Id);
            row.PhotoUrl = DisplayPhoto;
            row.FacePhoto = FacePhoto;
            row.FaceDescriptor = FaceDescriptor();
            db.Registrations.Add(new Registration
            {
                EventId = ev.Id,
                UserId = user.Id,
                RegisteredAt = DateTime.Now,
                Token = Guid.NewGuid(),
                ConfirmedAt = DateTime.Now,
            });
            await db.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FaceEngine:ApiKey"] = "test-key",
        }).Build();
        using var check = api.Services.CreateScope();
        var controller = new FaceEngineController(
            check.ServiceProvider.GetRequiredService<AppDbContext>(), config)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        controller.Request.Headers[FaceEngineController.KeyHeader] = "test-key";

        var result = await controller.Gallery(ev.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<FaceEngineGalleryItem>>(ok.Value).ToList();
        var item = items.Single(i => i.PersonId == $"u{user.Id}");
        Assert.Equal(FacePhoto, item.Photo);
    }

    [Fact]
    public async Task The_offline_bundle_carries_the_guest_display_photo()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var email = ApiFixture.NewEmail();
        (await api.Anonymous().PostAsJsonAsync($"/api/registrations/guest/{ev.Slug}", new
        {
            fullName = "Guest Photo",
            email,
            phone = ApiFixture.NewPhone(),
            notificationChannel = "Email",
            senateNo = $"ID-{Guid.NewGuid():N}"[..10],
            face = FaceDescriptor(),
            facePhoto = FacePhoto,
            displayPicture = DisplayPhoto,
        })).EnsureSuccessStatusCode();

        var bundle = await admin.GetFromJsonAsync<OfflineBundleDto>($"/api/events/{ev.Id}/offline-bundle");

        var reg = bundle!.Registrations.Single(r => r.GuestName == "Guest Photo");
        Assert.Equal(DisplayPhoto, reg.DisplayPicture);
        Assert.NotEqual(FacePhoto, reg.DisplayPicture);
    }

    private sealed record ProfileBody(string? PhotoUrl);
}
