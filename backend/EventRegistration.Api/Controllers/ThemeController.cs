using EventRegistration.Api.Services.Theming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventRegistration.Api.Controllers;

// The deployment's look. Public: the web app applies it before anyone signs in.
[ApiController]
[Route("api/theme")]
[AllowAnonymous]
public class ThemeController : ControllerBase
{
    [HttpGet]
    public ActionResult<ThemeDto> Get() => Ok(ThemeDto.From(ThemeStore.Current));
}
