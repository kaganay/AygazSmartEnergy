using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AygazSmartEnergy.Models;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Kullanıcı hesap akışı: kayıt, giriş, profil ve ayarlar.
namespace AygazSmartEnergy.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _configuration;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                TempData["ProfileMessage"] = "Kullanıcı bilgisi bulunamadı. Lütfen giriş yapın.";
                return RedirectToAction(nameof(Login));
            }
            return View(user);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                TempData["SettingsMessage"] = "Kullanıcı bulunamadı. Lütfen giriş yapın.";
                return RedirectToAction(nameof(Login));
            }

            var model = new AccountSettingsViewModel
            {
                UserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email ?? string.Empty,  // Email null ise boş string kullan
                PhoneNumber = user.PhoneNumber,
                Address = user.Address,
                IsActive = user.IsActive,
                LastLoginAt = user.LastLoginAt,
                
                GasThreshold = _configuration.GetValue<double>("GasSettings:Mq2Threshold", 40.0),
                TemperatureThreshold = _configuration.GetValue<double>("TemperatureSettings:Threshold", 27.0)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settings(AccountSettingsViewModel model)
        {
            if (!ModelState.IsValid)
            {
                PopulateReadOnlySettings(model);
                return View(model);
            }

            if (string.IsNullOrWhiteSpace(model.UserId))
            {
                ModelState.AddModelError(string.Empty, "Kullanıcı bulunamadı.");
                PopulateReadOnlySettings(model);
                return View(model);
            }

            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == model.UserId);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Kullanıcı bulunamadı.");
                PopulateReadOnlySettings(model);
                return View(model);
            }

            user.FirstName = model.FirstName;
            user.LastName = model.LastName;
            user.Email = model.Email;
            user.UserName = model.Email;  // Email ile username aynı (Identity kuralı)
            user.PhoneNumber = model.PhoneNumber;
            user.Address = model.Address;
            user.IsActive = model.IsActive;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                PopulateReadOnlySettings(model);
                return View(model);
            }

            await _signInManager.RefreshSignInAsync(user);
            TempData["SettingsSaved"] = true;
            return RedirectToAction(nameof(Settings));
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                PhoneNumber = model.PhoneNumber,
                Address = model.Address,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                user.LastLoginAt = DateTime.Now;
                await _userManager.UpdateAsync(user);
                await _signInManager.SignInAsync(user, isPersistent: false);
                return RedirectToAction("Index", "Dashboard");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            var model = new LoginViewModel
            {
                ReturnUrl = returnUrl
            };

            return View(model);
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                model.Email,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user != null)
                {
                    user.LastLoginAt = DateTime.UtcNow;
                    await _userManager.UpdateAsync(user);
                }

                if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                {
                    return Redirect(model.ReturnUrl);
                }

                return RedirectToAction("Index", "Dashboard");
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Hesabınız kilitlendi. Lütfen daha sonra tekrar deneyin.");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Geçersiz giriş denemesi.");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private Task<ApplicationUser?> GetCurrentUserAsync()
        {
            return _userManager.GetUserAsync(User);
        }

        private void PopulateReadOnlySettings(AccountSettingsViewModel model)
        {
            model.GasThreshold = _configuration.GetValue<double>("GasSettings:Mq2Threshold", 40.0);
            model.TemperatureThreshold = _configuration.GetValue<double>("TemperatureSettings:Threshold", 27.0);
        }
    }
}


