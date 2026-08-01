using UnityEngine;

[CreateAssetMenu(menuName = "Auth/GoogleAuthConfig")]
public class GoogleAuthConfig : ScriptableObject
{
    public string webClientId = "655038524634-hicg3h7ab0t2hussf2hpsaj4ufbfak40.apps.googleusercontent.com";
    public string redirectUri = "https://oauth.playfab.com/oauth2/google";

    public string scope = "openid email profile";
}
