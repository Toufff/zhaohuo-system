using System;
using UnityEngine;

namespace ZhaoHuoSystem;

// F119: the panel's language, chosen in the title bar left of 刷新; following the game is the first choice.
public sealed partial class ToolWindow
{
    private bool _languageOpen;
    private Rect _languageButton;
    /// <summary>The choice made: "" follows the game, else a LocalizationCatalog language ID.</summary>
    public Action<string>? LanguageChosen;

    private static string LanguageName(string id) => LocalizationCatalog.Native(id);

    // Always the English word, so anyone can tell what the button is for whatever language the panel is in.
    private const string LanguageLabel = "Language ▾";

    /// <summary>The button in the title bar; the list marks the language in use.</summary>
    private void DrawLanguageButton(Rect r)
    {
        _languageButton = r;
        string text = LanguageLabel;
        GUI.SetNextControlName("zh.button.language");
        if (SkinnedButton(r, new GUIContent(text), _languageOpen ? _tabSelected : _button)) { _languageOpen = !_languageOpen; GUI.FocusControl(null); }
        Hover(r, L.Format("面板语言：{0}；可在这里覆盖游戏的语言设置", LanguageName(L.LanguageId)));
    }

    private float LanguageButtonWidth() => _button.CalcSize(new GUIContent(LanguageLabel)).x + S(18);

    private string[] LanguageItems()
    {
        var items = new string[LocalizationCatalog.Languages.Length + 1];
        items[0] = L.Format("跟随游戏（{0}）", LanguageName(L.FollowId));
        for (int i = 0; i < LocalizationCatalog.Languages.Length; i++) items[i + 1] = LanguageName(LocalizationCatalog.Languages[i].Id);
        return items;
    }

    private Rect LanguageMenu(string[] items, out float row)
    {
        row = S(30);
        float w = _languageButton.width;
        foreach (var item in items) w = Mathf.Max(w, _listItem.CalcSize(new GUIContent("✓ " + item)).x + S(24));
        return new Rect(_languageButton.xMax - w, _languageButton.yMax + S(4), w, row * items.Length + S(8));
    }

    /// <summary>Presses go to the open list before anything drawn under it; a press elsewhere only closes it.</summary>
    private void LanguageInput(Event current)
    {
        if (!_languageOpen) return;
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape) { _languageOpen = false; current.Use(); return; }
        if (current.type != EventType.MouseDown || _languageButton.Contains(current.mousePosition)) return;
        var items = LanguageItems();
        var menu = LanguageMenu(items, out float row);
        if (menu.Contains(current.mousePosition))
        {
            int index = (int)((current.mousePosition.y - menu.y - S(4)) / row);
            if (index >= 0 && index < items.Length)
            {
                string id = index == 0 ? "" : LocalizationCatalog.Languages[index - 1].Id;
                _languageOpen = false;
                if (id != L.Override) LanguageChosen?.Invoke(id);
            }
        }
        else _languageOpen = false;
        current.Use();
    }

    /// <summary>Drawn last, over the tabs and the page; the rows only show, LanguageInput takes the presses.</summary>
    private void DrawLanguageMenu()
    {
        if (!_languageOpen) return;
        var items = LanguageItems();
        var menu = LanguageMenu(items, out float row);
        if (menu.Contains(Event.current.mousePosition)) { _hoverText = ""; _hoverEnum = null; }
        GUI.DrawTexture(menu, _dialogFill);
        GameSkin.Draw(menu, GameSkin.Part.Card, _scale, Color.white);
        for (int i = 0; i < items.Length; i++)
        {
            bool chosen = i == 0 ? L.Override.Length == 0 : L.Override == LocalizationCatalog.Languages[i - 1].Id;
            var r = new Rect(menu.x + S(4), menu.y + S(4) + i * row, menu.width - S(8), row);
            if (r.Contains(Event.current.mousePosition)) GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(1, 1, 1, .08f), 0, 0);
            GUI.Label(new Rect(r.x + S(8), r.y + S(4), r.width - S(12), r.height - S(4)), (chosen ? "✓ " : "    ") + items[i], chosen ? _met : _label);
        }
    }
}
