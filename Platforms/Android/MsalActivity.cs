using Android.App;
using Android.Content;
using Microsoft.Identity.Client;

namespace Graph
{
    [Activity(Exported = true)]
    [IntentFilter(new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryBrowsable, Intent.CategoryDefault },
        DataHost = "auth",
        DataScheme = $"msal{OAuthSettings.ApplicationId}")]
    public class MsalActivity : BrowserTabActivity
    {
    }
}