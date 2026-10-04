using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cryptbound {

// UI Toolkit front end: HUD bars, floor tracker, boss bar, cinematic overlays and menus.
public sealed class Hud : MonoBehaviour
{
    // Element references

    VisualElement _root, _damageLayer, _stats, _floorPanel;
    VisualElement _hpFill, _hpTrail, _stFill, _stTrail, _expFill;
    Label _hpValue, _stValue, _expValue, _levelLabel;
    Label _floorLabel, _floorName, _nextLabel, _distLabel;
    VisualElement _trackFill, _trackNodes, _trackMarker;
    VisualElement _bossPanel, _bossFill, _bossTrail;
    Label _bossName;
    VisualElement _letterTop, _letterBottom;
    VisualElement _centerPanel;
    Label _centerTitle, _centerSubtitle;
    VisualElement _counterPanel, _counterTimerFill;
    Label _counterLabel;
    VisualElement _toast;
    Label _toastLabel;
    VisualElement _levelUpPanel, _cards;
    Label _levelUpSub;
    VisualElement _titlePanel;
    Label _titlePrompt, _titleBest;
    VisualElement _gameOverPanel;
    Label _gameOverStats;
    VisualElement _flash, _fade;

    public bool Ready => _root != null;

    // MonoBehaviour implementation

    void Awake() => GetComponent<PanelRenderer>().RegisterUIReloadCallback(Bind);

    void Bind(PanelRenderer renderer, VisualElement root, int version)
    {
        _root = root;
        T Q<T>(string name) where T : VisualElement => root.Q<T>(name);
        _damageLayer = Q<VisualElement>("damageLayer");
        _stats = Q<VisualElement>("stats");
        _floorPanel = Q<VisualElement>("floorPanel");
        _hpFill = Q<VisualElement>("hpFill");
        _hpTrail = Q<VisualElement>("hpTrail");
        _stFill = Q<VisualElement>("stFill");
        _stTrail = Q<VisualElement>("stTrail");
        _expFill = Q<VisualElement>("expFill");
        _hpValue = Q<Label>("hpValue");
        _stValue = Q<Label>("stValue");
        _expValue = Q<Label>("expValue");
        _levelLabel = Q<Label>("levelLabel");
        _floorLabel = Q<Label>("floorLabel");
        _floorName = Q<Label>("floorName");
        _nextLabel = Q<Label>("nextLabel");
        _distLabel = Q<Label>("distLabel");
        _trackFill = Q<VisualElement>("trackFill");
        _trackNodes = Q<VisualElement>("trackNodes");
        _trackMarker = Q<VisualElement>("trackMarker");
        _bossPanel = Q<VisualElement>("bossPanel");
        _bossFill = Q<VisualElement>("bossFill");
        _bossTrail = Q<VisualElement>("bossTrail");
        _bossName = Q<Label>("bossName");
        _letterTop = Q<VisualElement>("letterboxTop");
        _letterBottom = Q<VisualElement>("letterboxBottom");
        _centerPanel = Q<VisualElement>("centerPanel");
        _centerTitle = Q<Label>("centerTitle");
        _centerSubtitle = Q<Label>("centerSubtitle");
        _counterPanel = Q<VisualElement>("counterPanel");
        _counterTimerFill = Q<VisualElement>("counterTimerFill");
        _counterLabel = Q<Label>("counterLabel");
        _toast = Q<VisualElement>("toast");
        _toastLabel = Q<Label>("toastLabel");
        _levelUpPanel = Q<VisualElement>("levelUpPanel");
        _cards = Q<VisualElement>("cards");
        _levelUpSub = Q<Label>("levelUpSub");
        _titlePanel = Q<VisualElement>("titlePanel");
        _titlePrompt = Q<Label>("titlePrompt");
        _titleBest = Q<Label>("titleBest");
        _gameOverPanel = Q<VisualElement>("gameOverPanel");
        _gameOverStats = Q<Label>("gameOverStats");
        _flash = Q<VisualElement>("flash");
        _fade = Q<VisualElement>("fade");
        _numbers.Clear();
        _fade.style.opacity = _fadeValue;
    }

    void Update()
    {
        if (!Ready) return;
        var dt = Time.unscaledDeltaTime;
        var now = Time.unscaledTime;

        // Bar trails catch up after a short delay.
        _hpTrailValue = Mathf.MoveTowards(_hpTrailValue, _hpValueNow, dt * (now > _hpTrailHold ? 0.6f : 0));
        _stTrailValue = Mathf.MoveTowards(_stTrailValue, _stValueNow, dt * 0.9f);
        _bossTrailValue = Mathf.MoveTowards(_bossTrailValue, _bossValueNow, dt * (now > _bossTrailHold ? 0.35f : 0));
        if (_hpTrailValue < _hpValueNow) _hpTrailValue = _hpValueNow;
        if (_stTrailValue < _stValueNow) _stTrailValue = _stValueNow;
        if (_bossTrailValue < _bossValueNow) _bossTrailValue = _bossValueNow;
        _hpTrail.style.width = Length.Percent(_hpTrailValue * 100);
        _stTrail.style.width = Length.Percent(_stTrailValue * 100);
        _bossTrail.style.width = Length.Percent(_bossTrailValue * 100);

        // Timed elements
        if (_centerHide > 0 && now > _centerHide) { _centerPanel.RemoveFromClassList("center-panel--visible"); _centerHide = 0; }
        if (_toastHide > 0 && now > _toastHide) { _toast.RemoveFromClassList("toast--visible"); _toastHide = 0; }

        // Overlays
        _flashValue = Mathf.MoveTowards(_flashValue, 0, dt * 2.5f);
        _flash.style.opacity = _flashValue;
        if (_fadeSpeed > 0)
        {
            _fadeValue = Mathf.MoveTowards(_fadeValue, _fadeTarget, dt * _fadeSpeed);
            _fade.style.opacity = _fadeValue;
        }

        // Blinking prompts
        var blink = Mathf.Repeat(now, 1.8f) < 0.9f;
        _titlePrompt.EnableInClassList("title-prompt--dim", blink);

        UpdateNumbers(dt);
    }

    // Stats

    float _hpValueNow = 1, _hpTrailValue = 1, _hpTrailHold;
    float _stValueNow = 1, _stTrailValue = 1;

    public void SetStats(float hp, float maxHp, float st, float maxSt, int exp, int next, int level)
    {
        if (!Ready) return;
        var h = Mathf.Clamp01(hp / maxHp);
        if (h < _hpValueNow) _hpTrailHold = Time.unscaledTime + 0.5f;
        _hpValueNow = h;
        _stValueNow = Mathf.Clamp01(st / maxSt);
        _hpFill.style.width = Length.Percent(h * 100);
        _stFill.style.width = Length.Percent(_stValueNow * 100);
        _stFill.EnableInClassList("stamina-fill--low", _stValueNow < 0.25f);
        _expFill.style.width = Length.Percent(Mathf.Clamp01((float)exp / next) * 100);
        _hpValue.text = $"{Mathf.CeilToInt(Mathf.Max(0, hp))} / {Mathf.CeilToInt(maxHp)}";
        _stValue.text = $"{Mathf.FloorToInt(st)} / {Mathf.CeilToInt(maxSt)}";
        _expValue.text = $"{exp:N0} / {next:N0}";
        _levelLabel.text = $"Level {level}";
    }

    public void SetHudVisible(bool visible)
    {
        if (!Ready) return;
        _stats.style.opacity = visible ? 1 : 0;
        _floorPanel.style.opacity = visible ? 1 : 0;
    }

    // Floor tracker

    public void SetFloor(int floor, string name, float[] nodes)
    {
        if (!Ready) return;
        _floorLabel.text = $"B{floor}";
        _floorName.text = name;
        _trackNodes.Clear();
        _nodeElements.Clear();
        foreach (var n in nodes)
        {
            var e = new VisualElement();
            e.AddToClassList("track-node");
            e.style.left = Length.Percent(n * 100);
            _trackNodes.Add(e);
            _nodeElements.Add(e);
        }
        var boss = new VisualElement();
        boss.AddToClassList("track-node");
        boss.AddToClassList("track-node--boss");
        boss.style.left = Length.Percent(100);
        _trackNodes.Add(boss);
    }

    readonly List<VisualElement> _nodeElements = new();

    public void SetProgress(float remaining, float t, int nodesDone, bool guardian)
    {
        if (!Ready) return;
        _nextLabel.text = guardian ? "Guardian" : "Next Floor";
        _distLabel.text = guardian ? "Defeat it" : $"{Mathf.CeilToInt(Mathf.Max(0, remaining))} m";
        t = Mathf.Clamp01(t);
        _trackFill.style.width = Length.Percent(t * 100);
        _trackMarker.style.left = Length.Percent(t * 100);
        for (var i = 0; i < _nodeElements.Count; i++)
            _nodeElements[i].EnableInClassList("track-node--done", i < nodesDone);
    }

    // Boss bar

    float _bossValueNow = 1, _bossTrailValue = 1, _bossTrailHold;

    public void ShowBoss(string name)
    {
        if (!Ready) return;
        _bossName.text = name.ToUpperInvariant();
        _bossValueNow = _bossTrailValue = 1;
        _bossFill.style.width = Length.Percent(100);
        _bossPanel.AddToClassList("boss-panel--visible");
    }

    public void HideBoss() { if (Ready) _bossPanel.RemoveFromClassList("boss-panel--visible"); }

    public void SetBoss(float t)
    {
        if (!Ready) return;
        t = Mathf.Clamp01(t);
        if (t < _bossValueNow) _bossTrailHold = Time.unscaledTime + 0.6f;
        _bossValueNow = t;
        _bossFill.style.width = Length.Percent(t * 100);
    }

    // Cinematic overlays

    public void Letterbox(bool on)
    {
        if (!Ready) return;
        _letterTop.EnableInClassList("letterbox--on", on);
        _letterBottom.EnableInClassList("letterbox--on", on);
        _stats.style.opacity = on ? 0 : 1;
        _floorPanel.style.opacity = on ? 0 : 1;
    }

    float _centerHide;

    public void ShowCenter(string title, string subtitle, float duration, string variant = null)
    {
        if (!Ready) return;
        _centerTitle.text = title;
        _centerSubtitle.text = subtitle;
        _centerTitle.EnableInClassList("center-title--danger", variant == "danger");
        _centerTitle.EnableInClassList("center-title--glory", variant == "glory");
        _centerPanel.RemoveFromClassList("center-panel--visible");
        _centerPanel.schedule.Execute(() => _centerPanel.AddToClassList("center-panel--visible")).StartingIn(30);
        _centerHide = duration > 0 ? Time.unscaledTime + duration : 0;
    }

    public void HideCenter()
    {
        if (!Ready) return;
        _centerPanel.RemoveFromClassList("center-panel--visible");
        _centerHide = 0;
    }

    public void ShowCounter(bool visible, string label = "COUNTER!")
    {
        if (!Ready) return;
        _counterLabel.text = label;
        _counterPanel.EnableInClassList("counter-panel--visible", visible);
    }

    public void SetCounterTimer(float t)
    {
        if (Ready) _counterTimerFill.style.width = Length.Percent(Mathf.Clamp01(t) * 100);
    }

    float _toastHide;

    public void Toast(string text, float duration)
    {
        if (!Ready) return;
        _toastLabel.text = text;
        _toast.AddToClassList("toast--visible");
        _toastHide = Time.unscaledTime + duration;
    }

    float _flashValue;

    public void Flash(float amount) => _flashValue = Mathf.Max(_flashValue, amount);

    float _fadeValue = 1, _fadeTarget = 1, _fadeSpeed;

    public void FadeTo(float target, float duration)
    {
        _fadeTarget = target;
        _fadeSpeed = duration <= 0 ? 1000 : 1 / duration;
    }

    // Damage numbers

    sealed class Number
    {
        public Label Label;
        public Vector3 World;
        public Vector3 Velocity;
        public float Time, Life;
        public bool Active;
    }

    readonly List<Number> _numbers = new();

    public void Popup(Vector3 world, string text, string variant = null)
    {
        if (!Ready) return;
        Number n = null;
        foreach (var x in _numbers) if (!x.Active) { n = x; break; }
        if (n == null)
        {
            n = new Number { Label = new Label() };
            n.Label.pickingMode = PickingMode.Ignore;
            _damageLayer.Add(n.Label);
            _numbers.Add(n);
        }
        n.Label.ClearClassList();
        n.Label.AddToClassList("damage-number");
        if (variant != null) n.Label.AddToClassList("damage-number--" + variant);
        n.Label.text = text;
        n.Label.style.display = DisplayStyle.Flex;
        n.World = world + Random.insideUnitSphere * 0.25f;
        n.Velocity = new Vector3(Random.Range(-0.4f, 0.4f), variant == "big" ? 1.2f : 1.8f, 0);
        n.Time = 0;
        n.Life = variant == "big" ? 1.6f : 0.9f;
        n.Active = true;
    }

    void UpdateNumbers(float dt)
    {
        var cam = Camera.main;
        if (cam == null || _root.panel == null) return;
        foreach (var n in _numbers)
        {
            if (!n.Active) continue;
            n.Time += dt;
            n.World += n.Velocity * dt;
            n.Velocity.y -= 2.5f * dt;
            var sp = cam.WorldToScreenPoint(n.World);
            if (n.Time > n.Life || sp.z < 0)
            {
                n.Active = false;
                n.Label.style.display = DisplayStyle.None;
                continue;
            }
            var pp = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(sp.x, Screen.height - sp.y));
            n.Label.style.left = pp.x;
            n.Label.style.top = pp.y - 20;
            var k = n.Time / n.Life;
            n.Label.style.opacity = k < 0.7f ? 1 : 1 - (k - 0.7f) / 0.3f;
            var pop = n.Time < 0.08f ? 1.4f - n.Time * 5 : 1;
            n.Label.style.scale = new Scale(new Vector3(pop, pop, 1));
        }
    }

    // Level up

    readonly List<VisualElement> _cardElements = new();

    public void ShowLevelUp(int level, SkillDef[] options, int[] ranks)
    {
        if (!Ready) return;
        _levelUpSub.text = $"Level {level}  —  Choose a new skill";
        _cards.Clear();
        _cardElements.Clear();
        for (var i = 0; i < options.Length; i++)
        {
            var def = options[i];
            var card = new VisualElement();
            card.AddToClassList("card");
            var icon = new VisualElement();
            icon.AddToClassList("card-icon");
            var tex = GameAssets.Instance.Texture(def.Icon);
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            card.Add(icon);
            var name = new Label(def.Name);
            name.AddToClassList("card-name");
            card.Add(name);
            var rank = new Label(def.MaxRank > 1 ? $"Rank {ranks[i] + 1} / {def.MaxRank}" : "Unique");
            rank.AddToClassList("card-rank");
            card.Add(rank);
            var desc = new Label(def.Description);
            desc.AddToClassList("card-desc");
            card.Add(desc);
            _cards.Add(card);
            _cardElements.Add(card);
        }
        _toast.RemoveFromClassList("toast--visible");
        _levelUpPanel.RemoveFromClassList("hidden");
        _levelUpPanel.schedule.Execute(() => _levelUpPanel.AddToClassList("levelup-panel--visible")).StartingIn(20);
    }

    public void SelectCard(int index)
    {
        for (var i = 0; i < _cardElements.Count; i++)
            _cardElements[i].EnableInClassList("card--selected", i == index);
    }

    public void HideLevelUp()
    {
        if (!Ready) return;
        _levelUpPanel.RemoveFromClassList("levelup-panel--visible");
        _levelUpPanel.AddToClassList("hidden");
    }

    // Title / game over

    public void ShowTitleScreen(string best)
    {
        if (!Ready) return;
        _titleBest.text = best;
        _titlePanel.RemoveFromClassList("hidden");
        _titlePanel.RemoveFromClassList("title-panel--hidden");
    }

    public void HideTitleScreen()
    {
        if (!Ready) return;
        _titlePanel.AddToClassList("title-panel--hidden");
        _titlePanel.schedule.Execute(() => _titlePanel.AddToClassList("hidden")).StartingIn(1000);
    }

    public void ShowGameOver(string stats)
    {
        if (!Ready) return;
        _gameOverStats.text = stats;
        _gameOverPanel.RemoveFromClassList("hidden");
        _gameOverPanel.schedule.Execute(() => _gameOverPanel.AddToClassList("gameover-panel--visible")).StartingIn(20);
    }
}

} // namespace Cryptbound
