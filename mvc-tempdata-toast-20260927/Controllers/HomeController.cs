using Microsoft.AspNetCore.Mvc;

namespace MvcToastProof.Controllers;

public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Save(string? name)
    {
        // This is a proof of the notification flow, not a database write.
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ToastMessage"] = "Enter a name before saving.";
            TempData["ToastKind"] = "error";
        }
        else
        {
            TempData["ToastMessage"] = $"Simulated save for {name.Trim()}.";
            TempData["ToastKind"] = "success";
        }

        return RedirectToAction(nameof(Index));
    }
}
