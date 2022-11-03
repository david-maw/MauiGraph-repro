using Microsoft.Identity.Client;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Graph;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    public MainPage()
	{
		InitializeComponent();
        RemoteAzure.OnMsg = Msg;
        if (!Guid.TryParse(OAuthSettings.ApplicationId, out _))
            Msg("The Client ID has not been set in the program, set OAuthSettings.ApplicationId to an Azure Application (client) ID");
	}
    private async void LogInOut(object sender, EventArgs e)
    {
        if (RemoteAzure.SignedIn)
            await RemoteAzure.SignOutAsync();
        else if (!Guid.TryParse(OAuthSettings.ApplicationId, out _))
            Msg("Cannot log on, the Client ID has not been set");
        else if (await RemoteAzure.SignInAsync())
            Msg("Logged on to Azure");
        else
            Msg("Logon failed");
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(IsLoggedOn));
    }
    private async void GetUserInfoBtn_Clicked(object sender, EventArgs e)
    {
        if (RemoteAzure.SignedIn)
        {
            Msg($"Requesting user info");
            if (await RemoteAzure.GetUserInfoAsync())
                Msg($"Logged on as {RemoteAzure.UserName} ({RemoteAzure.UserEmail})");
            else
                Msg("Request failed");
        }
    }
    private async void GetFolderInfoBtn_Clicked(object sender, EventArgs e)
    {
        if (RemoteAzure.SignedIn)
        {
            Msg($"Requesting folder info");
            if (await RemoteAzure.GetFoldersAsync())
                Msg("Request succeeded");
            else
                Msg("Request failed");
        }
    }

    private void Msg(string msg)
    {
        Messages.Add(msg);
    }

    public ObservableCollection<string> Messages { get; set; } = new();

    public string ActionText =>  IsLoggedOn ? "Sign Out" : "Sign In";

    public bool IsLoggedOn => RemoteAzure.SignedIn;

}

