using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ODM.Editor
{
    internal enum StatusKind { Info, Ok, Warn, Error }

    internal enum ButtonKind { Primary, Ghost, Danger }

    internal enum Ico
    {
        None, Chevron, ChevronDown, Check, Play, Grapple, Qr, List,
        Plus, Close, Trash, Pencil, Import, Export, Locate, Filter
    }

    /// <summary>Palette, text helpers, vector icons and small controls shared by the ODM window.</summary>
    internal static class ODMTheme
    {
        // Slate and steel with Wings-of-Freedom blue; green marks what is already done.
        public static readonly Color Bg = Rgb(26, 33, 38);
        public static readonly Color Header = Rgb(21, 28, 33);
        public static readonly Color Panel = Rgb(35, 44, 50);
        public static readonly Color PanelHi = Rgb(45, 56, 64);
        public static readonly Color Line = Rgb(55, 68, 76);
        public static readonly Color Ink = Rgb(236, 232, 220);
        public static readonly Color InkDim = Rgb(163, 173, 171);
        public static readonly Color InkFaint = Rgb(112, 125, 129);
        public static readonly Color Accent = Rgb(90, 162, 230);
        public static readonly Color AccentHi = Rgb(125, 184, 240);
        public static readonly Color AccentSoft = Rgba(90, 162, 230, 0.16f);
        public static readonly Color AccentLine = Rgba(90, 162, 230, 0.6f);
        public static readonly Color Ok = Rgb(134, 192, 108);
        public static readonly Color OkSoft = Rgba(134, 192, 108, 0.16f);
        public static readonly Color OkLine = Rgba(134, 192, 108, 0.5f);
        public static readonly Color Warn = Rgb(224, 169, 59);
        public static readonly Color Error = Rgb(229, 118, 107);
        public static readonly Color ErrorSoft = Rgba(229, 118, 107, 0.16f);

        /// <summary>When false, hover and click tests report nothing (mouse is elsewhere or outside a scroll area).</summary>
        public static bool HitEnabled = true;

        private static readonly Dictionary<int, GUIStyle> LabelStyles = new Dictionary<int, GUIStyle>();
        private static readonly Dictionary<string, string> FitCache = new Dictionary<string, string>();
        private static readonly GUIContent Measure = new GUIContent();
        private static GUIStyle fieldStyle;
        private static GUIStyle areaStyle;

        private static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        private static Color Rgba(int r, int g, int b, float a)
        {
            return new Color(r / 255f, g / 255f, b / 255f, a);
        }

        /// <summary>Call at the start of OnGUI; builds the text field styles once.</summary>
        public static void Begin()
        {
            if (fieldStyle == null)
            {
                fieldStyle = MakeFieldStyle(false);
                areaStyle = MakeFieldStyle(true);
            }
        }

        private static GUIStyle MakeFieldStyle(bool multiline)
        {
            GUIStyle style = new GUIStyle(EditorStyles.textField)
            {
                fontSize = 12,
                alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft,
                wordWrap = multiline,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                fixedHeight = 0f,
                stretchHeight = false
            };
            ClearState(style.normal);
            ClearState(style.hover);
            ClearState(style.active);
            ClearState(style.focused);
            ClearState(style.onNormal);
            ClearState(style.onHover);
            ClearState(style.onActive);
            ClearState(style.onFocused);
            return style;
        }

        private static void ClearState(GUIStyleState state)
        {
            state.background = null;
            state.textColor = Ink;
        }

        // ---------- text ----------

        private static GUIStyle LabelStyle(int size, FontStyle fontStyle, TextAnchor anchor, bool wrap)
        {
            int key = size * 1000 + (int)fontStyle * 100 + (int)anchor * 10 + (wrap ? 1 : 0);
            GUIStyle style;
            if (!LabelStyles.TryGetValue(key, out style) || style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = size,
                    fontStyle = fontStyle,
                    alignment = anchor,
                    wordWrap = wrap,
                    clipping = TextClipping.Clip,
                    richText = false,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0)
                };
                style.normal.textColor = Color.white;
                LabelStyles[key] = style;
            }
            return style;
        }

        public static void Label(
            Rect rect,
            string text,
            int size,
            FontStyle fontStyle,
            Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft,
            bool wrap = false
        )
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(text))
                return;

            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, text, LabelStyle(size, fontStyle, anchor, wrap));
            GUI.contentColor = previous;
        }

        public static float Width(string text, int size, FontStyle fontStyle)
        {
            Measure.text = text;
            return LabelStyle(size, fontStyle, TextAnchor.MiddleLeft, false).CalcSize(Measure).x;
        }

        /// <summary>Shortens text with an ellipsis so it fits in the given width.</summary>
        public static string Fit(string text, float width, int size, FontStyle fontStyle)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            if (width < 12f)
                return string.Empty;

            string key = size + "|" + (int)fontStyle + "|" + Mathf.RoundToInt(width) + "|" + text;
            string cached;
            if (FitCache.TryGetValue(key, out cached))
                return cached;

            GUIStyle style = LabelStyle(size, fontStyle, TextAnchor.MiddleLeft, false);
            Measure.text = text;
            string result = text;
            if (style.CalcSize(Measure).x > width)
            {
                int low = 0;
                int high = text.Length;
                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    Measure.text = text.Substring(0, mid).TrimEnd() + "…";
                    if (style.CalcSize(Measure).x <= width)
                        low = mid;
                    else
                        high = mid - 1;
                }
                result = text.Substring(0, low).TrimEnd() + "…";
            }

            if (FitCache.Count > 4000)
                FitCache.Clear();
            FitCache[key] = result;
            return result;
        }

        // ---------- shapes ----------

        public static void Fill(Rect rect, Color color)
        {
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, color);
        }

        public static Rect Inset(Rect rect, float amount)
        {
            return new Rect(rect.x + amount, rect.y + amount, rect.width - amount * 2f, rect.height - amount * 2f);
        }

        public static void Round(Rect rect, Color color, float radius)
        {
            if (Event.current.type != EventType.Repaint || color.a <= 0f || rect.width <= 0f || rect.height <= 0f)
                return;

            float clamped = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, clamped);
        }

        public static void RoundOutline(Rect rect, Color color, float radius, float width = 1f)
        {
            if (Event.current.type != EventType.Repaint || color.a <= 0f || rect.width <= 0f || rect.height <= 0f)
                return;

            float clamped = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, width, clamped);
        }

        public static void Disc(Vector2 center, float radius, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            const int segments = 20;
            Vector3[] points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                points[i] = new Vector3(center.x + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius, 0f);
            }

            Color previous = Handles.color;
            Handles.color = color;
            Handles.DrawAAConvexPolygon(points);
            Handles.color = previous;
        }

        public static void Ring(Vector2 center, float radius, Color color, float width)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            const int segments = 24;
            Vector3[] points = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                points[i] = new Vector3(center.x + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius, 0f);
            }

            Color previous = Handles.color;
            Handles.color = color;
            Handles.DrawAAPolyLine(width, points);
            Handles.color = previous;
        }

        // ---------- icons ----------

        public static void Draw(Ico ico, Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint || ico == Ico.None)
                return;

            float s = Mathf.Min(rect.width, rect.height);
            float x0 = rect.center.x - s * 0.5f;
            float y0 = rect.center.y - s * 0.5f;
            float w = Mathf.Max(1.4f, s * 0.12f);
            System.Func<float, float, Vector3> P = (px, py) => new Vector3(x0 + s * px, y0 + s * py, 0f);
            System.Action<float, float, float> Block = (px, py, size) =>
            {
                float side = Mathf.Max(1f, Mathf.Round(s * size));
                EditorGUI.DrawRect(new Rect(Mathf.Round(x0 + s * px), Mathf.Round(y0 + s * py), side, side), color);
            };

            Color previous = Handles.color;
            Handles.color = color;
            switch (ico)
            {
                case Ico.Chevron:
                    Handles.DrawAAPolyLine(w, P(.38f, .2f), P(.68f, .5f), P(.38f, .8f));
                    break;
                case Ico.ChevronDown:
                    Handles.DrawAAPolyLine(w, P(.2f, .38f), P(.5f, .68f), P(.8f, .38f));
                    break;
                case Ico.Check:
                    Handles.DrawAAPolyLine(w, P(.2f, .54f), P(.42f, .74f), P(.8f, .3f));
                    break;
                case Ico.Play:
                    Handles.DrawAAConvexPolygon(P(.32f, .2f), P(.8f, .5f), P(.32f, .8f));
                    break;
                case Ico.Grapple:
                    // Two cables latching onto one anchor point: jump straight to the target.
                    Handles.DrawAAPolyLine(w * 1.2f, P(.14f, .86f), P(.74f, .26f));
                    Handles.DrawAAPolyLine(w * 1.2f, P(.5f, .9f), P(.74f, .26f));
                    Disc(new Vector2(x0 + s * .76f, y0 + s * .24f), s * .15f, color);
                    Handles.DrawAAPolyLine(w, P(.62f, .08f), P(.78f, .02f));
                    break;
                case Ico.Qr:
                    Block(.08f, .08f, .34f);
                    Block(.58f, .08f, .34f);
                    Block(.08f, .58f, .34f);
                    Block(.58f, .58f, .14f);
                    Block(.78f, .78f, .14f);
                    break;
                case Ico.List:
                    for (int i = 0; i < 3; i++)
                    {
                        float y = .24f + i * .26f;
                        Disc(new Vector2(x0 + s * .16f, y0 + s * y), s * .07f, color);
                        Handles.DrawAAPolyLine(w, P(.34f, y), P(.88f, y));
                    }
                    break;
                case Ico.Plus:
                    Handles.DrawAAPolyLine(w, P(.5f, .2f), P(.5f, .8f));
                    Handles.DrawAAPolyLine(w, P(.2f, .5f), P(.8f, .5f));
                    break;
                case Ico.Close:
                    Handles.DrawAAPolyLine(w, P(.25f, .25f), P(.75f, .75f));
                    Handles.DrawAAPolyLine(w, P(.75f, .25f), P(.25f, .75f));
                    break;
                case Ico.Trash:
                    Handles.DrawAAPolyLine(w, P(.2f, .28f), P(.8f, .28f));
                    Handles.DrawAAPolyLine(w, P(.4f, .28f), P(.4f, .16f), P(.6f, .16f), P(.6f, .28f));
                    Handles.DrawAAPolyLine(w, P(.28f, .38f), P(.32f, .84f), P(.68f, .84f), P(.72f, .38f));
                    break;
                case Ico.Pencil:
                    Handles.DrawAAPolyLine(w * 1.9f, P(.34f, .66f), P(.72f, .28f));
                    Handles.DrawAAPolyLine(w, P(.2f, .84f), P(.5f, .84f));
                    break;
                case Ico.Import:
                    Handles.DrawAAPolyLine(w, P(.5f, .1f), P(.5f, .6f));
                    Handles.DrawAAPolyLine(w, P(.3f, .42f), P(.5f, .62f), P(.7f, .42f));
                    Handles.DrawAAPolyLine(w, P(.2f, .62f), P(.2f, .86f), P(.8f, .86f), P(.8f, .62f));
                    break;
                case Ico.Export:
                    Handles.DrawAAPolyLine(w, P(.5f, .64f), P(.5f, .14f));
                    Handles.DrawAAPolyLine(w, P(.3f, .32f), P(.5f, .12f), P(.7f, .32f));
                    Handles.DrawAAPolyLine(w, P(.2f, .62f), P(.2f, .86f), P(.8f, .86f), P(.8f, .62f));
                    break;
                case Ico.Locate:
                    Ring(new Vector2(x0 + s * .5f, y0 + s * .5f), s * .25f, color, w);
                    Disc(new Vector2(x0 + s * .5f, y0 + s * .5f), s * .08f, color);
                    Handles.DrawAAPolyLine(w, P(.5f, .04f), P(.5f, .2f));
                    Handles.DrawAAPolyLine(w, P(.5f, .8f), P(.5f, .96f));
                    Handles.DrawAAPolyLine(w, P(.04f, .5f), P(.2f, .5f));
                    Handles.DrawAAPolyLine(w, P(.8f, .5f), P(.96f, .5f));
                    break;
                case Ico.Filter:
                    Handles.DrawAAPolyLine(
                        w, P(.14f, .2f), P(.86f, .2f), P(.58f, .52f), P(.58f, .84f), P(.42f, .74f), P(.42f, .52f), P(.14f, .2f)
                    );
                    break;
            }
            Handles.color = previous;
        }

        // ---------- input ----------

        public static bool Hover(Rect rect)
        {
            Event current = Event.current;
            return HitEnabled && current.type != EventType.Layout && rect.Contains(current.mousePosition);
        }

        /// <summary>True once when the left mouse button goes down inside the rect.</summary>
        public static bool Clicked(Rect rect)
        {
            Event current = Event.current;
            if (!HitEnabled || current.type != EventType.MouseDown || current.button != 0 || !rect.Contains(current.mousePosition))
                return false;

            current.Use();
            GUIUtility.keyboardControl = 0;
            return true;
        }

        public static void Pointer(Rect rect)
        {
            if (HitEnabled && Event.current.type == EventType.Repaint)
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }

        public static bool Button(Rect rect, string text, Ico ico, ButtonKind kind, bool enabled = true)
        {
            bool hover = enabled && Hover(rect);
            if (Event.current.type == EventType.Repaint)
            {
                Color fill;
                Color ink;
                switch (kind)
                {
                    case ButtonKind.Primary:
                        fill = hover ? AccentHi : Accent;
                        ink = Header;
                        break;
                    case ButtonKind.Danger:
                        fill = hover ? ErrorSoft : Panel;
                        ink = Error;
                        break;
                    default:
                        fill = hover ? PanelHi : Panel;
                        ink = Ink;
                        break;
                }
                if (!enabled)
                    ink.a = 0.4f;

                Round(rect, fill, 6f);
                if (kind != ButtonKind.Primary)
                    RoundOutline(rect, enabled ? Line : new Color(Line.r, Line.g, Line.b, 0.5f), 6f);

                float iconWidth = ico == Ico.None ? 0f : 21f;
                float textWidth = Width(text, 11, FontStyle.Bold);
                float x = rect.center.x - (iconWidth + textWidth) * 0.5f;
                if (ico != Ico.None)
                    Draw(ico, new Rect(x, rect.center.y - 7f, 14f, 14f), ink);
                Label(new Rect(x + iconWidth, rect.y, textWidth + 4f, rect.height), text, 11, FontStyle.Bold, ink);
                if (enabled)
                    Pointer(rect);
            }
            return enabled && Clicked(rect);
        }

        public static string TextField(
            Rect rect,
            string controlName,
            string value,
            string placeholder,
            bool multiline,
            float padLeft = 10f,
            float padRight = 10f
        )
        {
            value = value ?? string.Empty;
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint)
            {
                bool focused = GUI.GetNameOfFocusedControl() == controlName;
                Round(rect, Bg, 5f);
                RoundOutline(rect, focused ? Accent : Line, 5f);
            }

            float padY = multiline ? 7f : 0f;
            Rect inner = new Rect(
                rect.x + padLeft,
                rect.y + padY,
                Mathf.Max(0f, rect.width - padLeft - padRight),
                Mathf.Max(0f, rect.height - padY * 2f)
            );

            GUI.SetNextControlName(controlName);
            Color cursor = GUI.skin.settings.cursorColor;
            Color selection = GUI.skin.settings.selectionColor;
            GUI.skin.settings.cursorColor = Ink;
            GUI.skin.settings.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.4f);
            string result = multiline
                ? EditorGUI.TextArea(inner, value, areaStyle)
                : EditorGUI.TextField(inner, value, fieldStyle);
            GUI.skin.settings.cursorColor = cursor;
            GUI.skin.settings.selectionColor = selection;

            if (repaint && result.Length == 0)
                Label(inner, placeholder, 12, FontStyle.Normal, InkFaint, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            return result;
        }
    }
}
