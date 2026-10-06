using System;
using System.Linq;
using Apocasetter;
using UnityEngine;

namespace Apocaspawner
{
    // Matches Apocapatrol's ledger chrome using Apocasetter's original game textures and Helveticrap menu font.
    internal static class SpawnerSkin
    {
        internal static readonly Color Background = C("1B1610"), Surface = C("282119"), Raised = C("332C21"),
            Field = C("17130D"), Text = C("EEE4CF"), Muted = C("B8AD99"), Line = C("514835"), Accent = C("E4CB69"), Selected = C("3B3321");
        internal static GUIStyle Body, Small, Heading, Brand, GameButton, Input, Scrollbar, Wrapped, Value;
        private static GUISkin _skin;
        private static Font _body, _bold, _game;
        private static float _nextScan;
        private static Font _scanned;
        internal static Color C(string hex) { return GameSkin.Hex(hex); }
        internal static GUISkin Begin()
        {
            var previous = GUI.skin; GameSkin.Ensure(1);
            if (_scanned == null && Time.realtimeSinceStartup >= _nextScan)
            { _nextScan = Time.realtimeSinceStartup + 1; _scanned = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name == "Helveticrap"); }
            var font = GameSkin.GameFont ?? _scanned;
            if (_skin == null || (font != null && _game != font))
            {
                if (_body == null) _body = Font.CreateDynamicFontFromOSFont("Segoe UI", 14);
                if (_bold == null) _bold = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Semibold", "Segoe UI" }, 14);
                _game = font; Body = Style(_body, 13); Small = Style(_body, 12); Heading = Style(_bold, 15);
                Brand = Style(_game ?? _bold, 26); GameButton = Style(_game ?? _bold, 15); Value = Style(_body, 13); Value.alignment = TextAnchor.MiddleRight;
                Wrapped = new GUIStyle(Body) { wordWrap = true };
                Input = new GUIStyle(GameSkin.TextField) { font = _body, fontSize = 14, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 0, 0), wordWrap = false };
                GameSkin.CenterText(Input, "0123456789");
                _skin = UnityEngine.Object.Instantiate(previous); _skin.label = Body; _skin.textField = Input;
                Scrollbar = new GUIStyle(GameSkin.ScrollV) { fixedWidth = 7, margin = new RectOffset(), padding = new RectOffset() };
                _skin.verticalScrollbar = Scrollbar; _skin.verticalScrollbarThumb = new GUIStyle(GameSkin.ScrollThumb) { fixedWidth = 7 };
                _skin.verticalScrollbarUpButton = _skin.verticalScrollbarDownButton = new GUIStyle(GUIStyle.none) { fixedHeight = 0, fixedWidth = 0 };
            }
            GUI.skin = _skin; return previous;
        }
        private static GUIStyle Style(Font font, int size)
        {
            var s = new GUIStyle(GUIStyle.none) { font = font, fontSize = size, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = Text; return s;
        }
        internal static void Fill(Rect r, Color color) { GameSkin.Fill(r, color); }
        internal static void Rule(float x, float y, float width) { Fill(new Rect(x, y, width, 1), Line); }
        internal static void Panel(Rect r, Color? fill = null, Color? edge = null)
        {
            Fill(r, fill ?? Surface); var color = edge ?? Line;
            Fill(new Rect(r.x, r.y, r.width, 1), color); Fill(new Rect(r.x, r.yMax - 1, r.width, 1), color);
            Fill(new Rect(r.x, r.y, 1, r.height), color); Fill(new Rect(r.xMax - 1, r.y, 1, r.height), color);
        }
        internal static void Label(Rect r, string text, GUIStyle style = null, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var st = style ?? Body; var old = st.alignment; st.alignment = align;
            GameSkin.Label(r, text, st, color ?? Text); st.alignment = old;
        }
        internal static bool Button(Rect r, string text, bool enabled = true, bool primary = false, bool gameFont = false, bool transparent = false)
        {
            bool old = GUI.enabled; GUI.enabled &= enabled;
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            Panel(r, primary ? new Color(.41f, .36f, .19f) : hover ? Selected : transparent ? Color.clear : Raised, hover ? Accent : Line);
            Label(r, text, gameFont ? GameButton : Body, enabled ? (primary ? Accent : Text) : Muted, TextAnchor.MiddleCenter);
            GUI.enabled = old; return hit;
        }
        internal static void Window(float width, float height)
        {
            var r = new Rect(0, 0, width, height); GameSkin.RustBack(r); Fill(r, new Color(16 / 255f, 13 / 255f, 9 / 255f, .5f));
            if (GameSkin.RebarH != null && GameSkin.RebarV != null)
            {
                GUI.DrawTexture(new Rect(0, 0, width, 6), GameSkin.RebarH); GUI.DrawTexture(new Rect(0, height - 6, width, 6), GameSkin.RebarH);
                GUI.DrawTexture(new Rect(0, 0, 6, height), GameSkin.RebarV); GUI.DrawTexture(new Rect(width - 6, 0, 6, height), GameSkin.RebarV);
            }
            else Panel(r, Color.clear, Line);
            if (GameSkin.Rivet != null) foreach (var p in new[] { new Vector2(10, 10), new Vector2(width - 18, 10), new Vector2(10, height - 18), new Vector2(width - 18, height - 18) }) GUI.DrawTexture(new Rect(p.x, p.y, 8, 8), GameSkin.Rivet);
            Fill(new Rect(6, 6, width - 12, 58), new Color(16 / 255f, 13 / 255f, 9 / 255f, .65f));
            Label(new Rect(26, 15, 420, 34), "APOCASPAWNER", Brand, Accent); Rule(6, 63, width - 12);
        }
        internal static Vector2 Scroll(Rect rect, Vector2 scroll, float height, Action<float> draw)
        {
            bool overflow = height > rect.height; float width = rect.width - (overflow ? 10 : 0);
            var value = GUI.BeginScrollView(rect, scroll, new Rect(0, 0, width, Mathf.Max(rect.height, height)), false, overflow, GUIStyle.none, Scrollbar);
            try { draw(width); } finally { GUI.EndScrollView(); }
            return value;
        }
    }
}
