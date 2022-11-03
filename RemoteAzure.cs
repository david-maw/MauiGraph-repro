using Microsoft.Graph;
using Microsoft.Identity.Client;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Graph
{
    public static class RemoteAzure
    {
        #region Globals
        private static readonly IPublicClientApplication PCA = null;

        static RemoteAzure()
        {

            try
            {
                PCA = PublicClientApplicationBuilder.Create(OAuthSettings.ApplicationId)
                    .WithAuthority($"https://login.microsoftonline.com/consumers/") // Personal credentials only
                    .WithDefaultRedirectUri()
                    .Build();
            }
            catch (Exception)
            {
            }
        }
        #endregion
        public delegate void SendMsg(string message);
        public static SendMsg OnMsg { get; set; }
        private static void Msg(string msg)
        {
            OnMsg?.Invoke(msg);
        }

        #region Microsoft Graph interface
        // Microsoft Graph client
        private static GraphServiceClient GraphClient;
        #endregion

        #region Session Management
        public static string UserName { get; private set; }
        public static string UserEmail { get; private set; }
        private static bool signedIn;
        public static bool SignedIn
        {
            get => signedIn;
            private set
            {
                if (signedIn != value)
                {
                    signedIn = value;
                    if (!SignedIn)
                    {
                        UserName = null;
                        UserEmail = null;
                    }
                }
            }
        }
        private static bool once = true;
        public static async Task<bool> SignInAsync(bool silentOnly = false)
        {
            if (SignedIn)
                return true;
            if (once) 
            { 
                once = false;
#if WINDOWS
                var storageProperties =
                     new StorageCreationPropertiesBuilder(@"TokenCache", @"C:\Temp")
                     .Build();

                // This hooks up the cross-platform cache into MSAL
                var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
                cacheHelper.RegisterCache(PCA.UserTokenCache);
#endif
            }
            bool tokenAcquired = false;
            // First, attempt silent sign in
            // If the user's information is already in the app's cache,
            // they won't have to sign in again.
            try
            {
                var accounts = await PCA.GetAccountsAsync();
                Msg($"Client accounts in the msal cache: {accounts.Count()}.");

                var silentAuthResult = await PCA
                    .AcquireTokenSilent(OAuthSettings.Scopes, accounts.FirstOrDefault())
                    .ExecuteAsync();

                Msg("User already signed in.");
                Msg($"Successful silent authentication for: {silentAuthResult.Account.Username}");
                Debug.WriteLine($"Access token: {silentAuthResult.AccessToken}");
                tokenAcquired = true;
            }
            catch (MsalUiRequiredException msalEx)
            {
                // This exception is thrown when an interactive sign-in is required.
                Msg("Silent token request failed, user needs to sign-in: " + msalEx.Message);
                if (!silentOnly)
                {
                    // Prompt the user to sign-in
                    var interactiveRequest = PCA.AcquireTokenInteractive(OAuthSettings.Scopes);
#if ANDROID
                    interactiveRequest = interactiveRequest
                        .WithParentActivityOrWindow(Platform.CurrentActivity);
#endif
                    Msg("About to await interactiveRequest.ExecuteAsync");
                    var interactiveAuthResult = await interactiveRequest.ExecuteAsync();
                    Msg("Returned from await interactiveRequest.ExecuteAsync");
                    Msg($"Successful interactive authentication for: {interactiveAuthResult.Account.Username}");
                    Debug.WriteLine($"Access token: {interactiveAuthResult.AccessToken}");
                    tokenAcquired = true;
                }
            }
            catch (Exception ex)
            {
                Msg("Authentication failed. Exception message: " + ex.Message);
            }
            if (!tokenAcquired)
                return SignedIn = false;
            return SignedIn = true;
        }
        public static async Task SignOutAsync()
        {
            // Get all cached accounts for the app
            // (Should only be one)
            var accounts = await PCA.GetAccountsAsync();
            while (accounts.Any())
            {
                // Remove the account info from the cache
                await PCA.RemoveAsync(accounts.First());
                accounts = await PCA.GetAccountsAsync();
            }
            SignedIn = false;
        }
        private static async Task<bool> InitializeGraphClientAsync()
        {
            var currentAccounts = await PCA.GetAccountsAsync();
            Msg($"Initializing Graph client. Accounts in the msal cache: {currentAccounts.Count()}.");
            try
            {
                if (currentAccounts.Any())
                {
                    // Initialize Graph client
                    GraphClient = new GraphServiceClient(new DelegateAuthenticationProvider(
                        async (requestMessage) =>
                        {
                            var result = await PCA.AcquireTokenSilent(OAuthSettings.Scopes, currentAccounts.FirstOrDefault())
                                .ExecuteAsync();

                            requestMessage.Headers.Authorization =
                                new AuthenticationHeaderValue("Bearer", result.AccessToken);
                        }));
                    GraphClient.HttpProvider.OverallTimeout = TimeSpan.FromSeconds(10);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Msg("Failed to initialize graph client.");
                Msg($"See exception message for details: {ex.Message}");
            }
            return false;
        }
#endregion
#region Graph Access
        public static async Task<bool> InitializeGraphAsync()
        {
            // If we used a cached local token we will not have communicated with the Internet yet and
            // doing so might fail, if so we want to fail gracefully, hence the try/catch below
            try
            {
                if (GraphClient != null)
                    Msg("Microsoft Graph already initialized");
                else if (await InitializeGraphClientAsync())
                    Msg("Microsoft Graph initialization completed without error");
                else
                {
                    Msg("Microsoft Graph initialization failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Msg("Microsoft Graph initialization failed. Exception message: " + ex.Message);
                return false;
            }
            return true;
        }
        public static async Task<bool> GetUserInfoAsync()
        {
            if (await InitializeGraphAsync())
            {
                User user = null;
                try
                {
                    user = await GraphClient.Me.Request().GetAsync();
                }
                catch (Exception ex)
                {
                    Msg("Microsoft Graph simple query for user information failed. Exception message: " + ex.Message);
                }
                if (user == null)
                {
                    try
                    {
                        var response = await GraphClient.Me.Request().GetResponseAsync();
                        var data = await response.Content.ReadAsStringAsync();
                        user = JsonSerializer.Deserialize<User>(data);
                    }
                    catch (Exception e)
                    {
                        Msg("Microsoft Graph query for user information failed. Exception message: " + e.Message);
                        return false;
                    }

                }
                UserName = user.DisplayName;
                UserEmail = string.IsNullOrEmpty(user.Mail) ? user.UserPrincipalName : user.Mail;
                return true;
            }
            else
                return false;
        }
        private static async Task<List<DriveItem>> GetFileSystemChildItems(string folderId, bool foldersOnly = false)
        {
            List<DriveItem> driveItems = new List<DriveItem>();
            // Because the remote files are shared by all instances of DivisiBill, introdice a separate folder
            // for use in development. 
            IDriveItemChildrenCollectionPage children = await GraphClient.Me.Drive.Items[folderId].Children
                .Request()
                .Filter(foldersOnly ? "Folder ne null" : "File ne null")
                .GetAsync();
            driveItems.AddRange(children.CurrentPage);
            while (children.NextPageRequest != null)
            {
                children = await children.NextPageRequest.GetAsync();
                driveItems.AddRange(children.CurrentPage);
            }
            return driveItems;
        }

        static DriveItem readmeDriveitem = null;
        public static async Task<bool> GetFoldersAsync()
        {
            if (await InitializeGraphAsync())
            {
                try
                {
                    var response = await GraphClient.Me.Drive.Root
                        .Request()
                        .GetResponseAsync();
                    var data = await response.Content.ReadAsStringAsync();
                    DriveItem BaseFolderItem = JsonSerializer.Deserialize<DriveItem>(data);
                    Msg("Found the base folder item now search for folders, then files, within it");
                    List<DriveItem> folderDiveItems = await GetFileSystemChildItems(BaseFolderItem.Id, true);
                    Msg($"Searched for folders, {folderDiveItems.Count} found");
                    foreach (var content in folderDiveItems)
                        Msg($"   {content.Name}");
                    List<DriveItem> fileDriveItems = await GetFileSystemChildItems(BaseFolderItem.Id, false);
                    Msg($"Searched for files, {fileDriveItems.Count} found");
                    foreach (var content in fileDriveItems)
                    {
                        Msg($"   {content.Name}");
                        if (content.Name.Equals("readme.txt"))
                            readmeDriveitem = content;
                    }
                    await GetFileContentAsync();
                }
                catch (ServiceException ex) when (ex.Error.Code == "itemNotFound")
                {
                    // The absence of any folder is a normal situation, not an error
                }
                catch (Exception ex)
                {
                    Msg("Microsoft Graph folder/file query failed. Exception message: " + ex.Message);
                    return false;
                }
                return true;
            }
            else
                return false;
        }

        internal static async Task<Stream> GetFileStreamAsync(string folderId, string fileName)
        {
            if (string.IsNullOrEmpty(folderId))
                return null; // There isn't even a folder, so there is no point looking for a file in it!
            try
            {
                return await GraphClient.Me.Drive.Items[folderId].ItemWithPath(fileName).Content
                    .Request()
                    .GetAsync();
            }
            catch (ServiceException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null; // The absence of the file is a normal situation, not an error
            }
            catch (Exception)
            {
                if (Debugger.IsAttached)
                    Debugger.Break();
                return null;
            }
        }

        public static async Task GetFileContentAsync()
        {
            if (readmeDriveitem == null)
            {
                Msg("readme.txt file was not found so will not be listed");
                return;
            }
            Msg("readme.txt file contents:");
            Stream content = await GraphClient.Me.Drive.Items[readmeDriveitem.Id].Content.Request().GetAsync();
            StreamReader sr = new StreamReader(content);
            while (!sr.EndOfStream)
                Msg(">>> "+ sr.ReadLine());

        }
#endregion
    }
}