using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Memory (concentration) game. Builds its UI automatically on Play, so nothing needs to be placed in the scene.
// Card faces use the images in Assets/Resources/Fruits (one image per pair, up to MaxPairs kinds).
public class MemoryGame : MonoBehaviour
{
    const int Columns = 4;
    const int MaxPairs = 8;
    const float CardSize = 136f;
    const float CardSpacing = 14f;
    const float FlipSeconds = 0.12f;
    const float MismatchSeconds = 0.8f;
    const int RankingSize = 5;

    static readonly Color BackgroundColor = new Color(1f, 0.97f, 0.88f);
    static readonly Color TextColor = new Color(0.2f, 0.2f, 0.2f);
    static readonly Color CardBackColor = new Color(0.25f, 0.5f, 0.85f);
    static readonly Color CardFaceColor = Color.white;
    static readonly Color CardMatchedColor = new Color(0.82f, 0.94f, 0.78f);

    class Card
    {
        public int Id;
        public RectTransform Rect;
        public Image Background;
        public RawImage Face;
        public Text Mark;
        public bool FaceUp;
        public bool Matched;
    }

    readonly List<Card> cards = new List<Card>();
    Texture2D[] fruits;
    int pairs;
    Font font;
    RectTransform board;
    Text status;
    Card first;
    bool busy;
    int moves;
    int matchedPairs;

    ScoreApiClient api;
    Text serverStatus;
    GameObject rankingPanel;
    Text rankingText;
    int session; // Incremented for every round, so late server responses for an old round are ignored.

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        new GameObject("MemoryGame").AddComponent<MemoryGame>();
    }

    void Start()
    {
        fruits = Resources.LoadAll<Texture2D>("Fruits");
        pairs = Mathf.Min(fruits.Length, MaxPairs);
        font = Font.CreateDynamicFontFromOSFont("Arial", 32);

        EnsureEventSystem();
        BuildUi();
        StartNewGame();

        api = gameObject.AddComponent<ScoreApiClient>();
        serverStatus.text = "Server: connecting...";
        api.Login(ShowServerStatus);
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    void BuildUi()
    {
        var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        Stretch(NewImage("Background", canvasObject.transform, BackgroundColor).rectTransform);

        status = NewText("Status", canvasObject.transform, 36, TextAnchor.MiddleCenter);
        var statusRect = status.rectTransform;
        statusRect.anchorMin = new Vector2(0, 1);
        statusRect.anchorMax = new Vector2(1, 1);
        statusRect.pivot = new Vector2(0.5f, 1);
        statusRect.sizeDelta = new Vector2(-400, 80);
        statusRect.anchoredPosition = new Vector2(0, -10);

        var restart = NewImage("Restart", canvasObject.transform, CardBackColor);
        var restartRect = restart.rectTransform;
        restartRect.anchorMin = restartRect.anchorMax = restartRect.pivot = new Vector2(1, 1);
        restartRect.sizeDelta = new Vector2(180, 60);
        restartRect.anchoredPosition = new Vector2(-20, -20);
        restart.gameObject.AddComponent<Button>().onClick.AddListener(StartNewGame);
        var restartLabel = NewText("Label", restart.transform, 30, TextAnchor.MiddleCenter);
        restartLabel.color = Color.white;
        restartLabel.text = "Restart";
        Stretch(restartLabel.rectTransform);

        var boardObject = new GameObject("Board", typeof(RectTransform), typeof(GridLayoutGroup));
        boardObject.transform.SetParent(canvasObject.transform, false);
        board = boardObject.GetComponent<RectTransform>();
        Stretch(board);
        board.offsetMin = new Vector2(0, 40);
        board.offsetMax = new Vector2(0, -90);

        var grid = boardObject.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(CardSize, CardSize);
        grid.spacing = new Vector2(CardSpacing, CardSpacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;
        grid.childAlignment = TextAnchor.MiddleCenter;

        serverStatus = NewText("ServerStatus", canvasObject.transform, 20, TextAnchor.LowerLeft);
        var serverRect = serverStatus.rectTransform;
        serverRect.anchorMin = serverRect.anchorMax = serverRect.pivot = new Vector2(0, 0);
        serverRect.sizeDelta = new Vector2(1200, 36);
        serverRect.anchoredPosition = new Vector2(20, 6);

        // Created last so it is drawn on top of the board.
        var panel = NewImage("Ranking", canvasObject.transform, new Color(1f, 1f, 1f, 0.96f));
        var panelRect = panel.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(520, 380);
        panelRect.anchoredPosition = new Vector2(0, -20);
        panel.raycastTarget = false;
        rankingText = NewText("Text", panel.transform, 30, TextAnchor.MiddleCenter);
        Stretch(rankingText.rectTransform);
        rankingPanel = panel.gameObject;
        rankingPanel.SetActive(false);
    }

    void StartNewGame()
    {
        StopAllCoroutines();
        session++;
        rankingPanel.SetActive(false);
        foreach (var card in cards)
            Destroy(card.Rect.gameObject);
        cards.Clear();
        first = null;
        busy = false;
        moves = 0;
        matchedPairs = 0;

        if (pairs == 0)
        {
            status.text = "Add images to Assets/Resources/Fruits";
            return;
        }

        var ids = new List<int>();
        for (int i = 0; i < pairs; i++)
        {
            ids.Add(i);
            ids.Add(i);
        }
        Shuffle(ids);

        foreach (var id in ids)
            cards.Add(CreateCard(id));
        UpdateStatus();
    }

    static void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    Card CreateCard(int id)
    {
        var background = NewImage("Card", board, CardBackColor);
        var card = new Card { Id = id, Rect = background.rectTransform, Background = background };

        var face = new GameObject("Face", typeof(RawImage)).GetComponent<RawImage>();
        face.transform.SetParent(background.transform, false);
        face.texture = fruits[id];
        face.raycastTarget = false;
        face.enabled = false;
        Stretch(face.rectTransform);
        face.rectTransform.offsetMin = new Vector2(12, 12);
        face.rectTransform.offsetMax = new Vector2(-12, -12);
        card.Face = face;

        var mark = NewText("Mark", background.transform, 72, TextAnchor.MiddleCenter);
        mark.color = Color.white;
        mark.text = "?";
        Stretch(mark.rectTransform);
        card.Mark = mark;

        background.gameObject.AddComponent<Button>().onClick.AddListener(() => OnCardClicked(card));
        return card;
    }

    void OnCardClicked(Card card)
    {
        if (busy || card.FaceUp || card.Matched)
            return;
        StartCoroutine(Reveal(card));
    }

    IEnumerator Reveal(Card card)
    {
        busy = true;
        yield return Flip(card, true);

        if (first == null)
        {
            first = card;
            busy = false;
            yield break;
        }

        var other = first;
        first = null;
        moves++;

        if (other.Id == card.Id)
        {
            other.Matched = card.Matched = true;
            ShowSide(other);
            ShowSide(card);
            matchedPairs++;
        }
        else
        {
            yield return new WaitForSeconds(MismatchSeconds);
            var flipOther = StartCoroutine(Flip(other, false));
            yield return Flip(card, false);
            yield return flipOther;
        }

        UpdateStatus();
        busy = false;

        if (matchedPairs == pairs)
            ReportScore();
    }

    // Saves the score, then shows the top of the ranking for this number of pairs.
    void ReportScore()
    {
        var round = session;
        ShowRankingMessage("Saving score...");
        api.SubmitScore(moves, pairs, saveError =>
        {
            if (round != session)
                return;
            if (saveError != null)
            {
                ShowRankingMessage($"Could not save the score:\n{saveError}");
                return;
            }

            ShowServerStatus(null);
            api.FetchRanking(pairs, RankingSize, (entries, fetchError) =>
            {
                if (round != session)
                    return;
                ShowRankingMessage(fetchError != null
                    ? $"Score saved: {moves} moves\n\nCould not load the ranking:\n{fetchError}"
                    : FormatRanking(entries));
            });
        });
    }

    string FormatRanking(ScoreApiClient.RankingEntry[] entries)
    {
        var text = new StringBuilder($"Score saved: {moves} moves\n\nRanking ({pairs} pairs)\n");
        foreach (var entry in entries)
            text.Append($"{entry.rank}. {entry.id}    {entry.moves} moves\n");
        return text.ToString();
    }

    void ShowRankingMessage(string message)
    {
        rankingText.text = message;
        rankingPanel.SetActive(true);
    }

    void ShowServerStatus(string error)
    {
        serverStatus.text = error == null
            ? $"Server: logged in as {ScoreApiClient.DevLoginId}"
            : $"Server: offline ({error})";
    }

    // Squeeze horizontally, swap the visible side, then expand back.
    IEnumerator Flip(Card card, bool faceUp)
    {
        yield return ScaleX(card.Rect, 1f, 0f);
        card.FaceUp = faceUp;
        ShowSide(card);
        yield return ScaleX(card.Rect, 0f, 1f);
    }

    static IEnumerator ScaleX(RectTransform rect, float from, float to)
    {
        for (float t = 0f; t < FlipSeconds; t += Time.deltaTime)
        {
            rect.localScale = new Vector3(Mathf.Lerp(from, to, t / FlipSeconds), 1f, 1f);
            yield return null;
        }
        rect.localScale = new Vector3(to, 1f, 1f);
    }

    static void ShowSide(Card card)
    {
        card.Background.color = card.Matched ? CardMatchedColor : card.FaceUp ? CardFaceColor : CardBackColor;
        card.Face.enabled = card.FaceUp;
        card.Mark.enabled = !card.FaceUp;
    }

    void UpdateStatus()
    {
        status.text = matchedPairs == pairs
            ? $"Cleared! Matched all pairs in {moves} moves"
            : $"Moves: {moves}    Pairs: {matchedPairs} / {pairs}";
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var image = new GameObject(name, typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.color = color;
        return image;
    }

    Text NewText(string name, Transform parent, int size, TextAnchor anchor)
    {
        var text = new GameObject(name, typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = TextColor;
        text.raycastTarget = false;
        return text;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
