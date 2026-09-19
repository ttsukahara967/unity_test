using UnityEditor;

// The score API runs over plain HTTP during development, and Unity rejects HTTP requests by default.
// Allow them in the Editor and in development builds only (Player Settings > Other Settings > Allow downloads over HTTP).
[InitializeOnLoad]
static class AllowInsecureHttp
{
    static AllowInsecureHttp()
    {
        if (PlayerSettings.insecureHttpOption == InsecureHttpOption.NotAllowed)
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
    }
}
