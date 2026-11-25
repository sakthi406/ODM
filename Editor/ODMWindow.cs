using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ODM.Editor
{
    /// <summary>ODM: jump to any SOP step in Play mode and fire saved zone QR scans.</summary>
    public sealed class ODMWindow : EditorWindow
    {
        private enum Tab { Steps, Zones }

        private enum ViewState { NotPlaying, WaitingScenario, WaitingWorkflow, NoSteps, Ready }

        private enum RowKind { Group, SubHeader, Step }

        private struct Row
        {
            public RowKind Kind;
            public float Y;
            public float H;
            public int Group;
            public int Matches;
            public bool First;
            public bool Last;
            public FlowEntry Entry;
            public string Text;
        }

        private const float HeaderH = 64f;
        private const float TabsH = 40f;
        private const float StatusH = 28f;
        private const float PadX = 12f;
        private const float TimelineX = 20f;

        private const float GroupH = 38f;
        private const float SubH = 24f;
        private const float StepH = 30f;
        private const float GroupGap = 6f;

        private const float ZoneCardH = 56f;
        private const float ZoneEditH = 188f;
        private const float EmptyCardH = 78f;
        private const float AddHeadH = 44f;
        private const float AddFormH = 192f;
        private const float FooterH = 56f;

        private readonly FlowSession flow = new FlowSession();
        private readonly ZoneStore zones = new ZoneStore();
        private readonly List<Row> rows = new List<Row>();

        private Tab tab;
        private bool instructionActive;
        private int expandedGroup = -1;
        private int lastGroup = -1;
        private bool scrollToCurrent;
        private string filter = string.Empty;
        private Vector2 stepScroll;
        private float rowsHeight;
        private float currentRowY = -1f;

        private Vector2 zoneScroll;
        private float zoneContentHeight;
        private int editingZone = -1;
        private bool addOpen;
        private string newName = string.Empty;
        private string newQr = string.Empty;
        private int pendingRemove = -1;
        private ZonePreset pendingScan;
        private bool pendingAdd;

        private string status = "Enter Play mode to detect a loaded workflow.";
        private StatusKind statusKind = StatusKind.Info;
        private double nextSceneScan;
        private double nextCursorPoll;

        [MenuItem("Tools/ODM")]
        private static void Open()
        {
            ODMWindow window = GetWindow<ODMWindow>();
            window.titleContent = new GUIContent("ODM");
            window.minSize = new Vector2(340f, 460f);
            window.Show();
        }

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            wantsMouseMove = true;
            string error = zones.Load();
            if (error != null)
                SetStatus(error, StatusKind.Error);

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;
            ResetWorkflow("Enter Play mode and select a scenario to load its SOP steps.");
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                instructionActive = false;
                ResetWorkflow("Enter Play mode and select a scenario to load its SOP steps.");
            }
            Repaint();
        }

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now >= nextSceneScan)
            {
                nextSceneScan = now + 0.5d;
                ScanScene();
            }

            if (flow.IsLoaded && now >= nextCursorPoll)
            {
                nextCursorPoll = now + 0.1d;
                if (flow.PollCursor())
                {
                    FollowCursor();
                    Repaint();
                }
            }
        }

        private void ScanScene()
        {
            if (!EditorApplication.isPlaying)
            {
                if (instructionActive || flow.HasState)
                {
                    instructionActive = false;
                    ResetWorkflow("Enter Play mode and select a scenario to load its SOP steps.");
                    Repaint();
                }
                return;
            }

            MonoBehaviour uiManager = Reflect.FindSceneComponent(AppTypes.UiManager);
            GameObject panel = Reflect.Read(uiManager, "_instructionPanel") as GameObject;
            bool active = panel != null && panel.activeInHierarchy;
            if (active != instructionActive)
            {
                instructionActive = active;
                Repaint();
            }

            if (!active)
            {
                if (flow.HasState)
                {
                    ResetWorkflow("Waiting for a scenario to be selected.");
                    Repaint();
                }
                return;
            }

            MonoBehaviour detected = Reflect.FindSceneComponent(AppTypes.Flow);
            object detectedRoot = detected == null ? null : Reflect.Read(detected, "sopRoot");
            if (detected != flow.Controller || detectedRoot != flow.Root)
            {
                string message = flow.Load(detected);
                flow.ProcedureName = ReadProcedureName();
                filter = string.Empty;
                stepScroll = Vector2.zero;
                lastGroup = -1;
                expandedGroup = -1;
                FollowCursor();
                if (expandedGroup < 0 && flow.Groups.Count > 0)
                    expandedGroup = 0;
                SetStatus(message, StatusKind.Info);
                Repaint();
            }
            else if (flow.IsLoaded && string.IsNullOrEmpty(flow.ProcedureName))
            {
                string name = ReadProcedureName();
                if (!string.IsNullOrEmpty(name))
                {
                    flow.ProcedureName = name;
                    Repaint();
                }
            }
        }

        private static string ReadProcedureName()
        {
            MonoBehaviour backend = Reflect.FindSceneComponent(AppTypes.QrBackend);
            return FlowSession.Clean(Reflect.Read(backend, "SelectedProcedureName") as string);
        }

        /// <summary>When the app moves to another sequence, open it and scroll to it.</summary>
        private void FollowCursor()
        {
            if (flow.CurrentGroup < 0 || flow.CurrentGroup == lastGroup)
                return;

            lastGroup = flow.CurrentGroup;
            expandedGroup = flow.CurrentGroup;
            scrollToCurrent = true;
        }

        private void ResetWorkflow(string message)
        {
            flow.Clear();
            expandedGroup = -1;
            lastGroup = -1;
            filter = string.Empty;
            stepScroll = Vector2.zero;
            SetStatus(message, StatusKind.Info);
        }

        private void SetStatus(string message, StatusKind kind)
        {
            status = message;
            statusKind = kind;
            Repaint();
        }

        private ViewState GetView()
        {
            if (!EditorApplication.isPlaying)
                return ViewState.NotPlaying;
            if (!instructionActive)
                return ViewState.WaitingScenario;
            if (!flow.IsLoaded)
                return ViewState.WaitingWorkflow;
            return flow.Entries.Count == 0 ? ViewState.NoSteps : ViewState.Ready;
        }

        // ------------------------------------------------------------------ frame

        private void OnGUI()
        {
            ODMTheme.Begin();
            Event current = Event.current;
            if (current.type == EventType.MouseMove || current.type == EventType.MouseLeaveWindow)
                Repaint();
            ODMTheme.HitEnabled = current.type != EventType.Repaint || mouseOverWindow == this;

            Rect header = GUILayoutUtility.GetRect(0f, HeaderH, GUILayout.ExpandWidth(true));
            Rect tabs = GUILayoutUtility.GetRect(0f, TabsH, GUILayout.ExpandWidth(true));
            Rect body = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            Rect bar = GUILayoutUtility.GetRect(0f, StatusH, GUILayout.ExpandWidth(true));
            if (current.type == EventType.Layout)
                return;

            ODMTheme.Fill(new Rect(0f, 0f, position.width, position.height), ODMTheme.Bg);
            DrawHeader(header);
            DrawTabs(tabs);
            if (tab == Tab.Steps)
                DrawSteps(body);
            else
                DrawZones(body);
            DrawStatus(bar);
        }

        private void DrawHeader(Rect r)
        {
            ODMTheme.Fill(r, ODMTheme.Header);
            ODMTheme.Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), ODMTheme.Line);

            Rect badge = new Rect(r.x + 14f, r.y + 13f, 38f, 38f);
            ODMTheme.Round(badge, ODMTheme.Accent, 9f);
            ODMTheme.Draw(Ico.Grapple, ODMTheme.Inset(badge, 8f), ODMTheme.Header);

            float textX = badge.xMax + 12f;
            ODMTheme.Label(new Rect(textX, r.y + 11f, 120f, 24f), "ODM", 18, FontStyle.Bold, ODMTheme.Ink);
            ODMTheme.Label(new Rect(textX, r.y + 34f, 150f, 16f), "Jump to any step", 10, FontStyle.Normal, ODMTheme.InkDim);

            bool live = GetView() == ViewState.Ready;
            Rect pill = new Rect(r.xMax - 14f - 62f, r.y + 13f, 62f, 20f);
            ODMTheme.Round(pill, live ? ODMTheme.OkSoft : ODMTheme.Panel, 10f);
            ODMTheme.Disc(new Vector2(pill.x + 12f, pill.center.y), 3.5f, live ? ODMTheme.Ok : ODMTheme.InkFaint);
            ODMTheme.Label(
                new Rect(pill.x + 21f, pill.y, 38f, pill.height),
                live ? "Live" : "Idle",
                10,
                FontStyle.Bold,
                live ? ODMTheme.Ok : ODMTheme.InkDim
            );

            if (live && flow.CurrentIndex >= 0)
            {
                ODMTheme.Label(
                    new Rect(r.xMax - 14f - 130f, r.y + 36f, 130f, 14f),
                    $"Step {flow.CurrentIndex + 1} of {flow.Entries.Count}",
                    10,
                    FontStyle.Normal,
                    ODMTheme.InkDim,
                    TextAnchor.MiddleRight
                );
                float fraction = (flow.CurrentIndex + 1f) / flow.Entries.Count;
                ODMTheme.Fill(new Rect(r.x, r.yMax - 3f, r.width, 3f), ODMTheme.Line);
                ODMTheme.Fill(new Rect(r.x, r.yMax - 3f, r.width * fraction, 3f), ODMTheme.Accent);
            }
        }

        private void DrawTabs(Rect r)
        {
            ODMTheme.Fill(r, ODMTheme.Bg);
            ODMTheme.Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), ODMTheme.Line);

            float half = r.width * 0.5f;
            for (int t = 0; t < 2; t++)
            {
                Tab which = (Tab)t;
                Rect tr = new Rect(r.x + half * t, r.y, half, r.height);
                bool active = tab == which;
                bool hover = ODMTheme.Hover(tr);
                string name = which == Tab.Steps ? "Steps" : "Zones";
                Ico ico = which == Tab.Steps ? Ico.List : Ico.Qr;
                int count = which == Tab.Steps ? (GetView() == ViewState.Ready ? flow.Entries.Count : 0) : zones.Items.Count;
                string badge = count > 0 ? count.ToString() : string.Empty;
                Color ink = active ? ODMTheme.Ink : (hover ? ODMTheme.InkDim : ODMTheme.InkFaint);

                float labelW = ODMTheme.Width(name, 12, FontStyle.Bold);
                float badgeW = badge.Length > 0 ? Mathf.Max(20f, ODMTheme.Width(badge, 10, FontStyle.Bold) + 12f) : 0f;
                float total = 16f + 7f + labelW + (badgeW > 0f ? 7f + badgeW : 0f);
                float x = tr.center.x - total * 0.5f;

                ODMTheme.Draw(ico, new Rect(x, tr.center.y - 8f, 16f, 16f), ink);
                ODMTheme.Label(new Rect(x + 23f, tr.y, labelW + 2f, tr.height), name, 12, FontStyle.Bold, ink);
                if (badgeW > 0f)
                {
                    Rect pill = new Rect(x + 23f + labelW + 7f, tr.center.y - 9f, badgeW, 18f);
                    ODMTheme.Round(pill, active ? ODMTheme.AccentSoft : ODMTheme.Panel, 9f);
                    ODMTheme.Label(pill, badge, 10, FontStyle.Bold, active ? ODMTheme.Accent : ODMTheme.InkFaint, TextAnchor.MiddleCenter);
                }
                if (active)
                    ODMTheme.Round(new Rect(tr.x + 22f, tr.yMax - 3f, tr.width - 44f, 3f), ODMTheme.Accent, 1.5f);

                ODMTheme.Pointer(tr);
                if (ODMTheme.Clicked(tr))
                    tab = which;
            }
        }

        private void DrawStatus(Rect r)
        {
            ODMTheme.Fill(r, ODMTheme.Header);
            ODMTheme.Fill(new Rect(r.x, r.y, r.width, 1f), ODMTheme.Line);

            Color dot;
            switch (statusKind)
            {
                case StatusKind.Ok: dot = ODMTheme.Ok; break;
                case StatusKind.Warn: dot = ODMTheme.Warn; break;
                case StatusKind.Error: dot = ODMTheme.Error; break;
                default: dot = ODMTheme.Accent; break;
            }

            ODMTheme.Disc(new Vector2(r.x + 16f, r.center.y + 0.5f), 3.5f, dot);
            float textWidth = r.width - 38f;
            ODMTheme.Label(
                new Rect(r.x + 28f, r.y + 1f, textWidth, r.height - 1f),
                ODMTheme.Fit(status, textWidth, 11, FontStyle.Normal),
                11,
                FontStyle.Normal,
                ODMTheme.InkDim
            );
        }

        private static void DrawScrollThumb(Rect area, float contentHeight, float scrollY)
        {
            if (Event.current.type != EventType.Repaint || contentHeight <= area.height + 1f)
                return;

            float thumbHeight = Mathf.Max(28f, area.height * area.height / contentHeight);
            float t = Mathf.Clamp01(scrollY / (contentHeight - area.height));
            Rect thumb = new Rect(area.xMax - 6f, area.y + 2f + t * (area.height - thumbHeight - 4f), 3f, thumbHeight);
            ODMTheme.Round(thumb, new Color(1f, 1f, 1f, 0.16f), 1.5f);
        }

        // ------------------------------------------------------------------ steps tab

        private void DrawEmptyState(Rect body, ViewState view)
        {
            Ico ico;
            string title;
            string sub;
            switch (view)
            {
                case ViewState.NotPlaying:
                    ico = Ico.Play;
                    title = "Press Play to begin";
                    sub = "Start the app and pick a scenario. Its steps show up here.";
                    break;
                case ViewState.WaitingScenario:
                    ico = Ico.Qr;
                    title = "Pick a scenario";
                    sub = "Choose a zone, asset and procedure in the app. Steps load as soon as the instructions screen opens.";
                    break;
                case ViewState.WaitingWorkflow:
                    ico = Ico.List;
                    title = "Loading the workflow";
                    sub = "Waiting for the flow controller to finish loading.";
                    break;
                default:
                    ico = Ico.List;
                    title = "No steps in this workflow";
                    sub = "The loaded SOP has no steps to jump to.";
                    break;
            }

            float top = body.y + Mathf.Max(24f, body.height * 0.28f - 32f);
            Rect circle = new Rect(body.center.x - 32f, top, 64f, 64f);
            ODMTheme.Round(circle, ODMTheme.Panel, 32f);
            ODMTheme.RoundOutline(circle, ODMTheme.Line, 32f);
            ODMTheme.Draw(ico, ODMTheme.Inset(circle, 20f), ODMTheme.Accent);

            ODMTheme.Label(
                new Rect(body.x + 20f, circle.yMax + 16f, body.width - 40f, 22f),
                title, 14, FontStyle.Bold, ODMTheme.Ink, TextAnchor.MiddleCenter
            );
            float subWidth = Mathf.Min(270f, body.width - 40f);
            ODMTheme.Label(
                new Rect(body.center.x - subWidth * 0.5f, circle.yMax + 42f, subWidth, 64f),
                sub, 11, FontStyle.Normal, ODMTheme.InkDim, TextAnchor.UpperCenter, true
            );
        }

        private void DrawSteps(Rect body)
        {
            ViewState view = GetView();
            if (view != ViewState.Ready)
            {
                DrawEmptyState(body, view);
                return;
            }

            float x = body.x + PadX;
            float w = body.width - PadX * 2f;
            float y = body.y + 12f;

            string scenario = string.IsNullOrWhiteSpace(flow.ProcedureName) ? "Current scenario" : flow.ProcedureName;
            float nameWidth = w - 100f;
            ODMTheme.Label(
                new Rect(x, y, nameWidth, 28f),
                ODMTheme.Fit(scenario, nameWidth, 13, FontStyle.Bold),
                13, FontStyle.Bold, ODMTheme.Ink
            );
            if (ODMTheme.Button(new Rect(x + w - 92f, y, 92f, 28f), "Current", Ico.Locate, ButtonKind.Ghost))
            {
                filter = string.Empty;
                GUI.FocusControl(null);
                expandedGroup = Mathf.Max(0, flow.CurrentGroup);
                scrollToCurrent = true;
            }
            y += 38f;

            DrawFilter(new Rect(x, y, w, 32f));
            y += 42f;

            DrawStepList(new Rect(body.x, y, body.width, Mathf.Max(0f, body.yMax - y)));
        }

        private void DrawFilter(Rect r)
        {
            filter = filter ?? string.Empty;
            string next = ODMTheme.TextField(r, "odm-filter", filter, "Filter steps", false, 32f, filter.Length > 0 ? 32f : 10f);
            if (next != filter)
            {
                filter = next;
                stepScroll = Vector2.zero;
            }

            ODMTheme.Draw(Ico.Filter, new Rect(r.x + 11f, r.center.y - 7f, 14f, 14f), ODMTheme.InkFaint);
            if (filter.Length > 0)
            {
                Rect clear = new Rect(r.xMax - 28f, r.y + 4f, 24f, r.height - 8f);
                bool hover = ODMTheme.Hover(clear);
                ODMTheme.Draw(Ico.Close, new Rect(clear.center.x - 6f, clear.center.y - 6f, 12f, 12f), hover ? ODMTheme.Ink : ODMTheme.InkDim);
                ODMTheme.Pointer(clear);
                if (ODMTheme.Clicked(clear))
                {
                    filter = string.Empty;
                    GUI.FocusControl(null);
                    stepScroll = Vector2.zero;
                }
            }
        }

        private static bool Matches(FlowEntry entry, string text)
        {
            return entry.Label.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void BuildRows()
        {
            rows.Clear();
            currentRowY = -1f;
            string text = (filter ?? string.Empty).Trim();
            bool filtering = text.Length > 0;
            float y = 0f;

            for (int g = 0; g < flow.Groups.Count; g++)
            {
                FlowGroup group = flow.Groups[g];
                int matches = 0;
                for (int i = 0; i < group.Steps.Count; i++)
                    if (!filtering || Matches(group.Steps[i], text))
                        matches++;
                if (matches == 0)
                    continue;

                if (g == flow.CurrentGroup && currentRowY < 0f)
                    currentRowY = y;
                rows.Add(new Row { Kind = RowKind.Group, Y = y, H = GroupH, Group = g, Matches = matches });
                y += GroupH;

                if (filtering || expandedGroup == g)
                {
                    string lastSub = string.Empty;
                    bool firstStep = true;
                    for (int i = 0; i < group.Steps.Count; i++)
                    {
                        FlowEntry entry = group.Steps[i];
                        if (filtering && !Matches(entry, text))
                            continue;

                        if (entry.SubKey != lastSub)
                        {
                            lastSub = entry.SubKey;
                            if (lastSub.Length > 0)
                            {
                                rows.Add(new Row { Kind = RowKind.SubHeader, Y = y, H = SubH, Group = g, Text = entry.SubText });
                                y += SubH;
                            }
                        }

                        if (entry.FlowIndex == flow.CurrentIndex)
                            currentRowY = y;
                        rows.Add(new Row { Kind = RowKind.Step, Y = y, H = StepH, Group = g, Entry = entry, First = firstStep });
                        firstStep = false;
                        y += StepH;
                    }

                    int lastIndex = rows.Count - 1;
                    if (rows[lastIndex].Kind == RowKind.Step)
                    {
                        Row last = rows[lastIndex];
                        last.Last = true;
                        rows[lastIndex] = last;
                    }
                }
                y += GroupGap;
            }
            rowsHeight = y + 8f;
        }

        private void DrawStepList(Rect area)
        {
            BuildRows();
            if (rows.Count == 0)
            {
                ODMTheme.Label(
                    new Rect(area.x, area.y + 20f, area.width, 24f),
                    "No steps match this filter",
                    12, FontStyle.Normal, ODMTheme.InkDim, TextAnchor.MiddleCenter
                );
                return;
            }

            if (scrollToCurrent)
            {
                scrollToCurrent = false;
                if (currentRowY >= 0f)
                    stepScroll.y = Mathf.Max(0f, currentRowY - area.height * 0.3f);
            }

            bool filtering = (filter ?? string.Empty).Trim().Length > 0;
            bool previousHit = ODMTheme.HitEnabled;
            ODMTheme.HitEnabled = previousHit && area.Contains(Event.current.mousePosition);

            int toggleGroup = -1;
            int jumpTo = -1;
            stepScroll = GUI.BeginScrollView(
                area, stepScroll, new Rect(0f, 0f, area.width, rowsHeight), false, false, GUIStyle.none, GUIStyle.none
            );
            float top = stepScroll.y;
            float bottom = top + area.height;
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row.Y + row.H < top)
                    continue;
                if (row.Y > bottom)
                    break;

                switch (row.Kind)
                {
                    case RowKind.Group:
                        if (DrawGroupRow(row, area.width, filtering))
                            toggleGroup = row.Group;
                        break;
                    case RowKind.SubHeader:
                        DrawSubRow(row, area.width);
                        break;
                    default:
                        if (DrawStepRow(row, area.width))
                            jumpTo = row.Entry.FlowIndex;
                        break;
                }
            }
            GUI.EndScrollView();
            ODMTheme.HitEnabled = previousHit;
            DrawScrollThumb(area, rowsHeight, stepScroll.y);

            if (toggleGroup >= 0)
                expandedGroup = expandedGroup == toggleGroup ? -1 : toggleGroup;
            if (jumpTo >= 0)
                Jump(jumpTo);
        }

        private bool DrawGroupRow(Row row, float width, bool filtering)
        {
            FlowGroup group = flow.Groups[row.Group];
            Rect card = new Rect(PadX, row.Y + 2f, width - PadX * 2f, row.H - 4f);
            bool current = row.Group == flow.CurrentGroup;
            bool done = flow.CurrentGroup >= 0 && row.Group < flow.CurrentGroup;
            bool open = filtering || expandedGroup == row.Group;
            bool hover = !filtering && ODMTheme.Hover(card);

            ODMTheme.Round(card, current ? ODMTheme.AccentSoft : (hover ? ODMTheme.PanelHi : ODMTheme.Panel), 6f);
            if (current)
                ODMTheme.RoundOutline(card, ODMTheme.AccentLine, 6f);

            ODMTheme.Draw(
                open ? Ico.ChevronDown : Ico.Chevron,
                new Rect(card.x + 8f, card.center.y - 7f, 14f, 14f),
                ODMTheme.InkDim
            );

            Rect badge = new Rect(card.x + 28f, card.center.y - 11f, 30f, 22f);
            ODMTheme.Round(badge, current ? ODMTheme.Accent : (done ? ODMTheme.OkSoft : ODMTheme.PanelHi), 5f);
            if (done)
            {
                ODMTheme.Draw(Ico.Check, new Rect(badge.center.x - 6f, badge.center.y - 6f, 12f, 12f), ODMTheme.Ok);
            }
            else
            {
                string id = string.IsNullOrEmpty(group.Id) ? (row.Group + 1).ToString() : group.Id;
                ODMTheme.Label(
                    badge, ODMTheme.Fit(id, 26f, 10, FontStyle.Bold), 10, FontStyle.Bold,
                    current ? ODMTheme.Header : ODMTheme.InkDim, TextAnchor.MiddleCenter
                );
            }

            string right;
            if (filtering)
                right = row.Matches.ToString();
            else if (current && flow.CurrentIndex >= group.Steps[0].FlowIndex)
                right = (flow.CurrentIndex - group.Steps[0].FlowIndex + 1) + "/" + group.Steps.Count;
            else
                right = group.Steps.Count.ToString();
            ODMTheme.Label(
                new Rect(card.xMax - 56f, card.y, 46f, card.height), right, 10, FontStyle.Bold,
                current ? ODMTheme.Accent : ODMTheme.InkFaint, TextAnchor.MiddleRight
            );

            float titleX = badge.xMax + 10f;
            float titleWidth = card.xMax - 62f - titleX;
            ODMTheme.Label(
                new Rect(titleX, card.y, titleWidth, card.height),
                ODMTheme.Fit(group.Title, titleWidth, 12, FontStyle.Bold),
                12, FontStyle.Bold, done ? ODMTheme.InkDim : ODMTheme.Ink
            );

            if (filtering)
                return false;
            ODMTheme.Pointer(card);
            return ODMTheme.Clicked(card);
        }

        private void DrawSubRow(Row row, float width)
        {
            float lineX = PadX + TimelineX;
            ODMTheme.Fill(new Rect(lineX - 1f, row.Y, 2f, row.H), ODMTheme.Line);
            float textX = lineX + 14f;
            float textWidth = width - PadX - textX;
            ODMTheme.Label(
                new Rect(textX, row.Y, textWidth, row.H),
                ODMTheme.Fit(row.Text, textWidth, 10, FontStyle.Bold),
                10, FontStyle.Bold, ODMTheme.InkFaint
            );
        }

        private bool DrawStepRow(Row row, float width)
        {
            FlowEntry entry = row.Entry;
            Rect hit = new Rect(PadX, row.Y, width - PadX * 2f, row.H);
            bool current = entry.FlowIndex == flow.CurrentIndex;
            bool done = entry.FlowIndex < flow.CurrentIndex;
            bool hover = ODMTheme.Hover(hit);
            float lineX = PadX + TimelineX;

            float lineTop = row.First ? hit.center.y : hit.y;
            float lineBottom = row.Last ? hit.center.y : hit.yMax;
            ODMTheme.Fill(new Rect(lineX - 1f, lineTop, 2f, lineBottom - lineTop), done ? ODMTheme.OkLine : ODMTheme.Line);

            Rect bg = new Rect(hit.x + 30f, hit.y + 1f, hit.width - 30f, hit.height - 2f);
            if (current)
            {
                ODMTheme.Round(bg, ODMTheme.AccentSoft, 4f);
                ODMTheme.Fill(new Rect(bg.x, bg.y + 5f, 2f, bg.height - 10f), ODMTheme.Accent);
            }
            else if (hover)
            {
                ODMTheme.Round(bg, ODMTheme.PanelHi, 4f);
            }

            Vector2 dot = new Vector2(lineX, hit.center.y);
            if (current)
            {
                ODMTheme.Disc(dot, 8f, ODMTheme.AccentSoft);
                ODMTheme.Disc(dot, 4.5f, ODMTheme.Accent);
            }
            else if (done)
            {
                ODMTheme.Disc(dot, 3.5f, ODMTheme.Ok);
            }
            else
            {
                ODMTheme.Disc(dot, 4f, ODMTheme.Bg);
                ODMTheme.Ring(dot, 3.5f, ODMTheme.InkFaint, 1.4f);
            }

            ODMTheme.Label(
                new Rect(bg.x + 10f, hit.y, 34f, hit.height),
                ODMTheme.Fit(entry.StepNo, 32f, 10, FontStyle.Normal),
                10, FontStyle.Normal, current ? ODMTheme.Accent : ODMTheme.InkFaint
            );

            float titleX = bg.x + 46f;
            float titleWidth = bg.xMax - 28f - titleX;
            FontStyle titleStyle = current ? FontStyle.Bold : FontStyle.Normal;
            ODMTheme.Label(
                new Rect(titleX, hit.y, titleWidth, hit.height),
                ODMTheme.Fit(entry.Title, titleWidth, 12, titleStyle),
                12, titleStyle, done ? ODMTheme.InkDim : ODMTheme.Ink
            );

            if (hover)
                ODMTheme.Draw(Ico.Play, new Rect(bg.xMax - 24f, hit.center.y - 7f, 14f, 14f), ODMTheme.Accent);

            ODMTheme.Pointer(hit);
            return ODMTheme.Clicked(hit);
        }

        private void Jump(int index)
        {
            string message;
            bool ok = flow.GoTo(index, out message);
            SetStatus(message, ok ? StatusKind.Ok : StatusKind.Error);
            if (ok)
            {
                expandedGroup = flow.CurrentGroup;
                lastGroup = flow.CurrentGroup;
            }
        }

        // ------------------------------------------------------------------ zones tab

        private void DrawZones(Rect body)
        {
            Rect footer = new Rect(body.x, body.yMax - FooterH, body.width, FooterH);
            Rect area = new Rect(body.x, body.y, body.width, Mathf.Max(0f, body.height - FooterH));
            float w = area.width - PadX * 2f;

            bool previousHit = ODMTheme.HitEnabled;
            ODMTheme.HitEnabled = previousHit && area.Contains(Event.current.mousePosition);
            zoneScroll = GUI.BeginScrollView(
                area, zoneScroll, new Rect(0f, 0f, area.width, zoneContentHeight), false, false, GUIStyle.none, GUIStyle.none
            );

            float y = 14f;
            ODMTheme.Label(new Rect(PadX, y, w, 18f), "Saved zones", 11, FontStyle.Bold, ODMTheme.InkDim);
            y += 24f;

            if (zones.Items.Count == 0)
            {
                DrawNoZones(new Rect(PadX, y, w, EmptyCardH));
                y += EmptyCardH + 8f;
            }
            else
            {
                for (int i = 0; i < zones.Items.Count; i++)
                {
                    float h = ZoneCardH + (editingZone == i ? ZoneEditH : 0f);
                    DrawZoneCard(new Rect(PadX, y, w, h), i);
                    y += h + 8f;
                }
            }

            y += 6f;
            float addHeight = AddHeadH + (addOpen ? AddFormH : 0f);
            DrawAddCard(new Rect(PadX, y, w, addHeight));
            y += addHeight + 18f;

            GUI.EndScrollView();
            ODMTheme.HitEnabled = previousHit;

            if (Event.current.type == EventType.Repaint && Mathf.Abs(y - zoneContentHeight) > 0.5f)
            {
                zoneContentHeight = y;
                Repaint();
            }

            DrawScrollThumb(area, zoneContentHeight, zoneScroll.y);
            DrawZoneFooter(footer);
            ApplyZoneActions();
        }

        private void DrawNoZones(Rect r)
        {
            ODMTheme.Round(r, ODMTheme.Panel, 6f);
            ODMTheme.RoundOutline(r, ODMTheme.Line, 6f);
            ODMTheme.Label(
                new Rect(r.x + 16f, r.y + 12f, r.width - 32f, 20f),
                "No zones yet", 12, FontStyle.Bold, ODMTheme.Ink, TextAnchor.MiddleCenter
            );
            ODMTheme.Label(
                new Rect(r.x + 16f, r.y + 34f, r.width - 32f, 36f),
                "Add the encrypted QR text of a zone below and scan it with one click.",
                11, FontStyle.Normal, ODMTheme.InkDim, TextAnchor.UpperCenter, true
            );
        }

        private static string QrPreview(string qr)
        {
            if (string.IsNullOrWhiteSpace(qr))
                return "No QR text yet";

            string text = qr.Trim();
            string head = text.Length > 16 ? text.Substring(0, 16) + "…" : text;
            return head + "  (" + text.Length + " characters)";
        }

        private void DrawZoneCard(Rect r, int index)
        {
            ZonePreset zone = zones.Items[index];
            bool editing = editingZone == index;

            ODMTheme.Round(r, ODMTheme.Panel, 6f);
            ODMTheme.RoundOutline(r, editing ? ODMTheme.AccentLine : ODMTheme.Line, 6f);

            Rect tile = new Rect(r.x + 12f, r.y + 12f, 32f, 32f);
            ODMTheme.Round(tile, ODMTheme.PanelHi, 5f);
            ODMTheme.Draw(Ico.Qr, ODMTheme.Inset(tile, 8f), ODMTheme.Accent);

            Rect play = new Rect(r.xMax - 44f, r.y + 12f, 32f, 32f);
            bool playHover = ODMTheme.Hover(play);
            ODMTheme.Round(play, playHover ? ODMTheme.AccentHi : ODMTheme.Accent, 16f);
            ODMTheme.Draw(Ico.Play, new Rect(play.x + 9f, play.y + 9f, 14f, 14f), ODMTheme.Header);
            ODMTheme.Pointer(play);
            if (ODMTheme.Clicked(play))
                pendingScan = zone;

            Rect edit = new Rect(play.x - 36f, r.y + 14f, 28f, 28f);
            bool editHover = ODMTheme.Hover(edit);
            if (editHover || editing)
                ODMTheme.Round(edit, ODMTheme.PanelHi, 5f);
            ODMTheme.Draw(editing ? Ico.Close : Ico.Pencil, ODMTheme.Inset(edit, 7f), editHover || editing ? ODMTheme.Ink : ODMTheme.InkDim);
            ODMTheme.Pointer(edit);
            if (ODMTheme.Clicked(edit))
            {
                editingZone = editing ? -1 : index;
                GUI.FocusControl(null);
            }

            float textX = tile.xMax + 10f;
            float textWidth = edit.x - 8f - textX;
            string zoneName = string.IsNullOrWhiteSpace(zone.zoneName) ? "Unnamed zone" : zone.zoneName;
            ODMTheme.Label(
                new Rect(textX, r.y + 10f, textWidth, 18f),
                ODMTheme.Fit(zoneName, textWidth, 12, FontStyle.Bold),
                12, FontStyle.Bold, ODMTheme.Ink
            );
            ODMTheme.Label(
                new Rect(textX, r.y + 29f, textWidth, 16f),
                ODMTheme.Fit(QrPreview(zone.encryptedQr), textWidth, 10, FontStyle.Normal),
                10, FontStyle.Normal, ODMTheme.InkFaint
            );

            if (!editing)
                return;

            ODMTheme.Fill(new Rect(r.x + 1f, r.y + ZoneCardH, r.width - 2f, 1f), ODMTheme.Line);
            float fx = r.x + 12f;
            float fw = r.width - 24f;
            float fy = r.y + ZoneCardH + 10f;

            EditorGUI.BeginChangeCheck();
            ODMTheme.Label(new Rect(fx, fy, fw, 12f), "Name", 10, FontStyle.Normal, ODMTheme.InkDim);
            fy += 15f;
            zone.zoneName = ODMTheme.TextField(new Rect(fx, fy, fw, 28f), "odm-zone-name-" + index, zone.zoneName, "Zone name", false);
            fy += 36f;
            ODMTheme.Label(new Rect(fx, fy, fw, 12f), "Encrypted QR text", 10, FontStyle.Normal, ODMTheme.InkDim);
            fy += 15f;
            zone.encryptedQr = ODMTheme.TextField(new Rect(fx, fy, fw, 62f), "odm-zone-qr-" + index, zone.encryptedQr, "Paste the encrypted QR text", true);
            fy += 72f;
            if (EditorGUI.EndChangeCheck())
                zones.Save();

            if (ODMTheme.Button(new Rect(fx, fy, 100f, 30f), "Remove", Ico.Trash, ButtonKind.Danger))
                pendingRemove = index;
            if (ODMTheme.Button(new Rect(fx + fw - 84f, fy, 84f, 30f), "Done", Ico.Check, ButtonKind.Ghost))
            {
                editingZone = -1;
                GUI.FocusControl(null);
            }
        }

        private void DrawAddCard(Rect r)
        {
            ODMTheme.Round(r, ODMTheme.Panel, 6f);
            Rect head = new Rect(r.x, r.y, r.width, AddHeadH);
            if (ODMTheme.Hover(head))
                ODMTheme.Round(head, ODMTheme.PanelHi, 6f);
            ODMTheme.RoundOutline(r, ODMTheme.Line, 6f);

            Rect tile = new Rect(r.x + 12f, r.y + 10f, 24f, 24f);
            ODMTheme.Round(tile, ODMTheme.AccentSoft, 5f);
            ODMTheme.Draw(Ico.Plus, ODMTheme.Inset(tile, 5f), ODMTheme.Accent);
            ODMTheme.Label(new Rect(tile.xMax + 10f, head.y, 140f, head.height), "New zone", 12, FontStyle.Bold, ODMTheme.Ink);
            ODMTheme.Draw(
                addOpen ? Ico.ChevronDown : Ico.Chevron,
                new Rect(head.xMax - 28f, head.center.y - 7f, 14f, 14f),
                ODMTheme.InkDim
            );
            ODMTheme.Pointer(head);
            if (ODMTheme.Clicked(head))
            {
                addOpen = !addOpen;
                GUI.FocusControl(null);
            }

            if (!addOpen)
                return;

            ODMTheme.Fill(new Rect(r.x + 1f, r.y + AddHeadH, r.width - 2f, 1f), ODMTheme.Line);
            float fx = r.x + 12f;
            float fw = r.width - 24f;
            float fy = r.y + AddHeadH + 10f;

            ODMTheme.Label(new Rect(fx, fy, fw, 12f), "Name", 10, FontStyle.Normal, ODMTheme.InkDim);
            fy += 15f;
            newName = ODMTheme.TextField(new Rect(fx, fy, fw, 28f), "odm-new-name", newName, "For example, Fabrication yard", false);
            fy += 36f;
            ODMTheme.Label(new Rect(fx, fy, fw, 12f), "Encrypted QR text", 10, FontStyle.Normal, ODMTheme.InkDim);
            fy += 15f;
            newQr = ODMTheme.TextField(new Rect(fx, fy, fw, 62f), "odm-new-qr", newQr, "Paste the encrypted QR text", true);
            fy += 72f;

            if (ODMTheme.Button(new Rect(fx, fy, fw, 32f), "Save zone", Ico.Plus, ButtonKind.Primary))
                pendingAdd = true;
        }

        private void DrawZoneFooter(Rect r)
        {
            ODMTheme.Fill(r, ODMTheme.Header);
            ODMTheme.Fill(new Rect(r.x, r.y, r.width, 1f), ODMTheme.Line);

            float gap = 8f;
            float buttonWidth = (r.width - PadX * 2f - gap) * 0.5f;
            Rect import = new Rect(r.x + PadX, r.y + 11f, buttonWidth, 34f);
            Rect export = new Rect(import.xMax + gap, import.y, buttonWidth, 34f);
            if (ODMTheme.Button(import, "Import", Ico.Import, ButtonKind.Ghost))
                ImportZones();
            if (ODMTheme.Button(export, "Export", Ico.Export, ButtonKind.Ghost, zones.Items.Count > 0))
                ExportZones();
        }

        private void ApplyZoneActions()
        {
            if (pendingRemove >= 0)
            {
                if (pendingRemove < zones.Items.Count)
                {
                    zones.Items.RemoveAt(pendingRemove);
                    zones.Save();
                }
                pendingRemove = -1;
                editingZone = -1;
                GUIUtility.keyboardControl = 0;
                SetStatus("Zone removed.", StatusKind.Info);
            }

            if (pendingScan != null)
            {
                ZonePreset zone = pendingScan;
                pendingScan = null;
                string message;
                StatusKind kind = QrSimulator.Scan(zone, out message);
                SetStatus(message, kind);
            }

            if (pendingAdd)
            {
                pendingAdd = false;
                AddZone();
            }
        }

        private void AddZone()
        {
            if (string.IsNullOrWhiteSpace(newName) || string.IsNullOrWhiteSpace(newQr))
            {
                SetStatus("Enter both a zone name and its encrypted QR text.", StatusKind.Warn);
                return;
            }

            zones.Add(newName, newQr);
            SetStatus($"Saved '{newName.Trim()}'. Zones are kept in your Unity Editor preferences.", StatusKind.Ok);
            newName = string.Empty;
            newQr = string.Empty;
            addOpen = false;
            GUI.FocusControl(null);
        }

        private void ImportZones()
        {
            string path = EditorUtility.OpenFilePanel("Import zones", string.Empty, "json");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    int count = zones.Import(path);
                    editingZone = -1;
                    SetStatus($"Imported {count} zone(s).", StatusKind.Ok);
                }
                catch (Exception exception)
                {
                    SetStatus($"Import failed: {exception.Message}", StatusKind.Error);
                    Debug.LogException(exception);
                }
            }
            GUIUtility.ExitGUI();
        }

        private void ExportZones()
        {
            string path = EditorUtility.SaveFilePanel("Export zones", string.Empty, "ODM-Zones", "json");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    zones.Export(path);
                    SetStatus($"Exported {zones.Items.Count} zone(s). The file holds encrypted QR text, so share it with care.", StatusKind.Ok);
                }
                catch (Exception exception)
                {
                    SetStatus($"Export failed: {exception.Message}", StatusKind.Error);
                    Debug.LogException(exception);
                }
            }
            GUIUtility.ExitGUI();
        }
    }
}
