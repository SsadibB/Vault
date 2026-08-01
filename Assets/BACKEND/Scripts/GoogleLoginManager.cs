using UnityEngine;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;



//LOGIN WORKING 

public class GoogleLoginManager : MonoBehaviour
{
    [Header("PlayFab Credentials")]
    [Tooltip("Your 4 to 6 character PlayFab Title ID (found in PlayFab Dashboard URL or Settings)")]
    public string playFabTitleId = "";

    [Header("Google OAuth Credentials")]
    [Tooltip("PlayFab Web Client ID from Google Cloud Console")]
    public string webClientId = "655038524634-hicg3h7ab0t2hussf2hpsaj4ufbfak40.apps.googleusercontent.com";

    [Tooltip("Local port for redirect URI during Editor/Desktop login")]
    public int redirectPort = 5000;

    private HttpListener httpListener;
    private CancellationTokenSource listenerCts;

    private void Awake()
    {
        if (!string.IsNullOrEmpty(playFabTitleId))
        {
            PlayFabSettings.staticSettings.TitleId = playFabTitleId;
        }
    }

    private void OnDisable() => StopListener();
    private void OnDestroy() => StopListener();

    /// <summary>
    /// Call this method when the "Sign in with Google" UI Button is clicked.
    /// </summary>
    public void OnGoogleSignInButtonClicked()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        StartEditorLoopbackLogin();
#elif UNITY_ANDROID || UNITY_IOS
        StartMobileGoogleSignIn();
#else
        Debug.LogWarning("[GoogleLogin] Platform not explicitly handled. Attempting browser loopback login...");
        StartEditorLoopbackLogin();
#endif
    }

    #region Unity Editor / PC (System Browser Loopback OAuth)
    private async void StartEditorLoopbackLogin()
    {
        // Cancel and clean up any previously running listener session
        StopListener();

        string redirectUri = $"http://127.0.0.1:{redirectPort}/callback/";
        string state = Guid.NewGuid().ToString("N");

        string authUrl = "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(webClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&response_type=code" +
            "&scope=openid%20email%20profile" +
            "&prompt=select_account" +
            $"&state={state}";

        listenerCts = new CancellationTokenSource();

        try
        {
            httpListener = new HttpListener();
            httpListener.Prefixes.Add(redirectUri);
            httpListener.Start();

            Debug.Log($"[GoogleLogin] Listening for Google callback on {redirectUri}");
            Debug.Log("[GoogleLogin] Opening system browser for Google Account Sign-In...");
            Application.OpenURL(authUrl);

            // Listen asynchronously for Google's redirect response (with timeout of 120 seconds)
            listenerCts.CancelAfter(TimeSpan.FromSeconds(120));

            HttpListenerContext context = null;
            try
            {
                var getContextTask = httpListener.GetContextAsync();
                var completedTask = await Task.WhenAny(getContextTask, Task.Delay(-1, listenerCts.Token));

                if (completedTask == getContextTask)
                {
                    context = await getContextTask;
                }
                else
                {
                    Debug.LogWarning("[GoogleLogin] Google Sign-In timed out or was canceled.");
                    StopListener();
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                // Listener was stopped manually
                return;
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[GoogleLogin] Google Sign-In task was canceled.");
                StopListener();
                return;
            }

            if (context == null) return;

            HttpListenerRequest request = context.Request;
            string code = request.QueryString["code"];
            string error = request.QueryString["error"];

            // Send friendly success response page back to the browser window
            HttpListenerResponse response = context.Response;
            response.ContentType = "text/html; charset=utf-8";
            response.StatusCode = (int)HttpStatusCode.OK;

            string responseHtml = "<!DOCTYPE html><html><head><title>Authentication Complete</title></head>" +
                                 "<body style='font-family:Segoe UI, sans-serif; text-align:center; padding-top:60px; background-color:#F8F9FA; color:#202124;'>" +
                                 "<div style='display:inline-block; padding:40px; background:white; border-radius:12px; box-shadow:0 4px 12px rgba(0,0,0,0.1);'>" +
                                 "<h1 style='color:#1A73E8; margin-bottom:10px;'>Google Sign-In Successful!</h1>" +
                                 "<p style='font-size:16px; color:#5F6368;'>You can now close this browser tab and return to Unity.</p>" +
                                 "</div></body></html>";

            byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
            response.ContentLength64 = buffer.Length;
            
            try
            {
                using (Stream output = response.OutputStream)
                {
                    await output.WriteAsync(buffer, 0, buffer.Length);
                    await output.FlushAsync();
                }
                // CRITICAL: Close the HTTP response so the browser finishes loading the page!
                response.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GoogleLogin] Non-critical error sending browser response: {ex.Message}");
            }

            // Small delay to ensure TCP connection finishes closing cleanly before stopping listener
            await Task.Delay(200);
            StopListener();

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"[GoogleLogin] Google OAuth Error: {error}");
                return;
            }

            if (!string.IsNullOrEmpty(code))
            {
                Debug.Log($"[GoogleLogin] Received Auth Code from Google. Exchanging code with PlayFab...");
                LoginToPlayFab(code);
            }
        }
        catch (HttpListenerException hex)
        {
            Debug.LogError($"[GoogleLogin] HttpListener failed to start. Port {redirectPort} may be occupied or blocked. Error: {hex.Message}");
            StopListener();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GoogleLogin] Listener error: {ex.Message}");
            StopListener();
        }
    }

    private void StopListener()
    {
        if (listenerCts != null)
        {
            try { listenerCts.Cancel(); } catch { }
            try { listenerCts.Dispose(); } catch { }
            listenerCts = null;
        }

        if (httpListener != null)
        {
            try
            {
                if (httpListener.IsListening)
                {
                    httpListener.Stop();
                }
                httpListener.Close();
            }
            catch { }
            finally
            {
                httpListener = null;
            }
        }
    }
    #endregion

    #region Android & iOS Google Sign-In
    private void StartMobileGoogleSignIn()
    {
        Debug.Log("[GoogleLogin] Mobile Sign-In triggered.");
        // If you are using Google Sign-In Unity Plugin (Google.GoogleSignIn):
        /*
        GoogleSignIn.Configuration = new GoogleSignInConfiguration {
            WebClientId = webClientId,
            RequestServerAuthCode = true,
            RequestEmail = true,
            RequestProfile = true
        };

        GoogleSignIn.DefaultInstance.SignIn().ContinueWith(task => {
            if (task.IsFaulted) {
                Debug.LogError("[GoogleLogin] Google Mobile Sign-In Faulted: " + task.Exception);
            } else if (task.IsCanceled) {
                Debug.LogWarning("[GoogleLogin] Google Mobile Sign-In Canceled.");
            } else {
                string serverAuthCode = task.Result.ServerAuthCode;
                LoginToPlayFab(serverAuthCode);
            }
        });
        */

        // Fallback / Browser-based auth if plugin not used:
        StartEditorLoopbackLogin();
    }
    #endregion

    #region PlayFab Authentication
    public void LoginToPlayFab(string serverAuthCode)
    {
        if (!string.IsNullOrEmpty(playFabTitleId))
        {
            PlayFabSettings.staticSettings.TitleId = playFabTitleId;
        }

        if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId))
        {
            Debug.LogError("[GoogleLogin] ERROR: PlayFab Title ID is missing! Please enter your PlayFab Title ID in the GoogleLoginManager Inspector or PlayFabSharedSettings asset.");
            return;
        }

        var request = new LoginWithGoogleAccountRequest
        {
            TitleId = PlayFabSettings.staticSettings.TitleId,
            ServerAuthCode = serverAuthCode,
            CreateAccount = true
        };

        PlayFabClientAPI.LoginWithGoogleAccount(
            request,
            result =>
            {
                Debug.Log($"<color=green>[GoogleLogin] PlayFab Login SUCCESS! PlayFab ID: {result.PlayFabId}</color>");
                if (result.NewlyCreated)
                {
                    Debug.Log("<color=cyan>[GoogleLogin] A brand new PlayFab user account was created!</color>");
                }
            },
            error =>
            {
                Debug.LogError($"[GoogleLogin] PlayFab Login Failed: {error.GenerateErrorReport()}");
            }
        );
    }
    #endregion
}