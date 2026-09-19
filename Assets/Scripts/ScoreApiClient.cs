using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Talks to the score API (unity_test_server). It runs its own coroutines, so they keep going
// when MemoryGame restarts a round (MemoryGame.StartNewGame calls StopAllCoroutines on itself).
// Every callback receives an error message, or null on success.
public class ScoreApiClient : MonoBehaviour
{
    // Development defaults. See unity_test_server/README.md.
    const string BaseUrl = "http://localhost:5080";
    public const string DevLoginId = "user1";
    const string DevPassword = "pass";
    const int TimeoutSeconds = 10;

    [Serializable]
    class LoginRequest
    {
        public string id;
        public string password;
    }

    [Serializable]
    class LoginResponse
    {
        public string token;
        public string expiresAt;
    }

    [Serializable]
    class SubmitScoreRequest
    {
        public int moves;
        public int pairs;
    }

    [Serializable]
    public class RankingEntry
    {
        public int rank;
        public string id;
        public int moves;
    }

    // JsonUtility cannot parse a top-level array, so the response is wrapped in an object first.
    [Serializable]
    class RankingList
    {
        public RankingEntry[] items;
    }

    [Serializable]
    class Problem
    {
        public string title;
    }

    class Response
    {
        public long Code;
        public string Text;
        public string Error;
    }

    string token;

    bool IsLoggedIn => !string.IsNullOrEmpty(token);

    public void Login(Action<string> onDone)
    {
        StartCoroutine(LoginRoutine(onDone));
    }

    public void SubmitScore(int moves, int pairs, Action<string> onDone)
    {
        StartCoroutine(SubmitScoreRoutine(moves, pairs, onDone));
    }

    public void FetchRanking(int pairs, int limit, Action<RankingEntry[], string> onDone)
    {
        StartCoroutine(FetchRankingRoutine(pairs, limit, onDone));
    }

    IEnumerator LoginRoutine(Action<string> onDone)
    {
        token = null;
        var response = new Response();
        var body = JsonUtility.ToJson(new LoginRequest { id = DevLoginId, password = DevPassword });
        yield return Send("POST", "/api/login", body, response);

        if (response.Error == null)
            token = JsonUtility.FromJson<LoginResponse>(response.Text).token;
        onDone?.Invoke(response.Error);
    }

    IEnumerator SubmitScoreRoutine(int moves, int pairs, Action<string> onDone)
    {
        var response = new Response();
        var body = JsonUtility.ToJson(new SubmitScoreRequest { moves = moves, pairs = pairs });
        yield return SendAuthorized("POST", "/api/scores", body, response);
        onDone?.Invoke(response.Error);
    }

    IEnumerator FetchRankingRoutine(int pairs, int limit, Action<RankingEntry[], string> onDone)
    {
        var response = new Response();
        yield return SendAuthorized("GET", $"/api/scores/ranking?pairs={pairs}&limit={limit}", null, response);
        if (response.Error != null)
        {
            onDone?.Invoke(null, response.Error);
            yield break;
        }

        var list = JsonUtility.FromJson<RankingList>("{\"items\":" + response.Text + "}");
        onDone?.Invoke(list.items, null);
    }

    // Logs in first if needed, and logs in again once if the token was rejected (it expires after 60 minutes).
    IEnumerator SendAuthorized(string method, string path, string json, Response response)
    {
        if (!IsLoggedIn)
        {
            string loginError = null;
            yield return LoginRoutine(error => loginError = error);
            if (loginError != null)
            {
                response.Error = loginError;
                yield break;
            }
        }

        yield return Send(method, path, json, response);
        if (response.Code != 401)
            yield break;

        string relogError = null;
        yield return LoginRoutine(error => relogError = error);
        if (relogError != null)
        {
            response.Error = relogError;
            yield break;
        }
        yield return Send(method, path, json, response);
    }

    IEnumerator Send(string method, string path, string json, Response response)
    {
        using var request = new UnityWebRequest(BaseUrl + path, method)
        {
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = TimeoutSeconds,
        };
        if (json != null)
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.SetRequestHeader("Content-Type", "application/json");
        }
        if (IsLoggedIn)
            request.SetRequestHeader("Authorization", "Bearer " + token);

        yield return request.SendWebRequest();

        response.Code = request.responseCode;
        response.Text = request.downloadHandler.text;
        response.Error = request.result == UnityWebRequest.Result.Success ? null : Describe(request);
    }

    static string Describe(UnityWebRequest request)
    {
        if (request.result != UnityWebRequest.Result.ProtocolError)
            return request.error;

        // The API reports errors as problem details JSON ({"title": "..."}); a 401 from the token check has no body.
        string title = null;
        try
        {
            title = JsonUtility.FromJson<Problem>(request.downloadHandler.text)?.title;
        }
        catch (ArgumentException)
        {
        }
        return string.IsNullOrEmpty(title) ? $"HTTP {request.responseCode}" : $"HTTP {request.responseCode}: {title}";
    }
}
