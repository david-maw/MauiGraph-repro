namespace Graph;
public static class OAuthSettings
{
    public static readonly string ApplicationId = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"; // TODO replace with your AppID
    public static readonly string [] Scopes = { "User.Read", "MailboxSettings.Read", "Files.ReadWrite.All" };
    public static readonly string RedirectUri = $"msal{ApplicationId}://auth";
}
