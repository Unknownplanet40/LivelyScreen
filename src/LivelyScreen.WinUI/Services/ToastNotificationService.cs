using System;
using System.IO;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace LivelyScreen_WinUI.Services;

public static class ToastNotificationService
{
    public const string AppId = "LivelyScreen.Screensaver";

    public static void EnsureAumidRegistered()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
            if (key != null)
            {
                key.SetValue("DisplayName", "LivelyScreen");
                if (File.Exists(iconPath))
                {
                    key.SetValue("IconUri", iconPath);
                }
                key.SetValue("IconBackgroundColor", "0");
            }
        }
        catch (Exception ex)
        {
            App.Log($"EnsureAumidRegistered error: {ex.Message}");
        }
    }

    public static void ShowToastWithBanner(string title, string message, string? bannerPath = null, string? logoPath = null)
    {
        try
        {
            EnsureAumidRegistered();

            bannerPath ??= Path.Combine(AppContext.BaseDirectory, "Assets", "Wide310x150Logo.scale-200.png");

            if (string.IsNullOrEmpty(logoPath))
            {
                var croppedPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LogoMark-cropped.png");
                if (File.Exists(croppedPath))
                {
                    logoPath = croppedPath;
                }
                else
                {
                    var squarePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Square150x150Logo.scale-200.png");
                    logoPath = File.Exists(squarePath) ? squarePath : Path.Combine(AppContext.BaseDirectory, "Assets", "LogoMark.png");
                }
            }

            var bannerUri = File.Exists(bannerPath) ? new Uri(bannerPath).AbsoluteUri : string.Empty;
            var logoUri = File.Exists(logoPath) ? new Uri(logoPath).AbsoluteUri : string.Empty;

            var xml = "<toast><visual><binding template=\"ToastGeneric\">";
            if (!string.IsNullOrEmpty(bannerUri))
            {
                xml += $"<image placement=\"hero\" src=\"{bannerUri}\"/>";
            }
            if (!string.IsNullOrEmpty(logoUri))
            {
                xml += $"<image placement=\"appLogoOverride\" hint-crop=\"none\" src=\"{logoUri}\"/>";
            }
            xml += $"<text>{SecurityElement.Escape(title)}</text>";
            xml += $"<text>{SecurityElement.Escape(message)}</text>";
            xml += "</binding></visual></toast>";

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var toast = new ToastNotification(doc);
            var notifier = ToastNotificationManager.CreateToastNotifier(AppId);
            notifier.Show(toast);
            App.Log($"ShowToastWithBanner: banner={bannerPath}, logo={logoPath} sent successfully.");
        }
        catch (Exception ex)
        {
            App.Log($"ShowToastWithBanner error: {ex.Message}");
        }
    }
}
