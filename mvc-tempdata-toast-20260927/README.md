# ASP.NET Core MVC TempData toast proof

This local demo targets .NET 10. It demonstrates a one-time notice after the POST → Redirect → GET pattern. It is separate from DotNetCoder's published Blazor toast components. The `Save` action deliberately simulates persistence; it has no database.

Article: https://dotnetcoder.com/aspnet-core-mvc-toast-after-redirect-tempdata/

## Prerequisites

- .NET 10 SDK. No database, Azure service, or paid account is required.

## Run

From this folder, run the HTTP proof in PowerShell. It starts a local MVC server on an available loopback port, checks the redirect and encoding behavior, and stops the server:

```powershell
dotnet run --project .\ProofRunner\ProofRunner.csproj
```

The final output line should begin with `PASS:`. To inspect the page visually, start the MVC app separately:

```powershell
dotnet run --no-launch-profile --urls http://127.0.0.1:5127
```

In a browser, open `http://127.0.0.1:5127/`, enter a name, submit, and then refresh. The first page after submission should show the notice; refresh should remove it. Press Ctrl+C when finished. The included screenshots and `proof-evidence.txt` record the earlier user-run two-window test of the same behavior.

## What the proof checks

- The initial GET has no notification.
- The form includes an antiforgery token; POST returns 302.
- The redirected GET contains exactly one notification.
- A script-shaped name is HTML encoded by Razor.
- Refreshing the page does not show the notification again.

The automated check uses a cookie jar because the default TempData provider stores protected data in a cookie. This proves response markup and request behavior only after it runs successfully. It does not prove screen-reader announcements in every browser, real database persistence, distributed Data Protection key configuration, or cross-domain redirects. In production, use HTTPS, stable Data Protection keys across instances, and a real save operation. Keep the TempData payload short; do not store secrets in it.

References: [Microsoft ASP.NET Core state management](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/app-state?view=aspnetcore-10.0) and [MDN live regions](https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Guides/Live_regions).
