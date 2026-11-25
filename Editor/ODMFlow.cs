using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ODM.Editor
{
    internal sealed class FlowEntry
    {
        public int FlowIndex;
        public int SequenceIndex; // -1 for post-sequence steps
        public int Group;
        public string StepNo = string.Empty;
        public string Title = string.Empty;
        public string SubKey = string.Empty;
        public string SubText = string.Empty;
        public string Label = string.Empty; // full text the filter matches against
    }

    internal sealed class FlowGroup
    {
        public int SequenceIndex;
        public bool IsPost;
        public string Id = string.Empty;
        public string Title = string.Empty;
        public readonly List<FlowEntry> Steps = new List<FlowEntry>();
    }

    /// <summary>The workflow currently loaded in the running app, flattened into groups and steps.</summary>
    internal sealed class FlowSession
    {
        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

        public readonly List<FlowEntry> Entries = new List<FlowEntry>();
        public readonly List<FlowGroup> Groups = new List<FlowGroup>();
        public MonoBehaviour Controller { get; private set; }
        public object Root { get; private set; }
        public string ProcedureName = string.Empty;
        public int SopStepCount { get; private set; }
        public int CurrentIndex { get; private set; } = -1;
        public int CurrentGroup { get; private set; } = -1;

        public bool IsLoaded => Controller != null && Root != null;
        public bool HasState => Controller != null || Root != null || Entries.Count > 0;

        public int SequenceCount
        {
            get
            {
                int count = 0;
                foreach (FlowGroup group in Groups)
                    if (!group.IsPost)
                        count++;
                return count;
            }
        }

        public void Clear()
        {
            Entries.Clear();
            Groups.Clear();
            Controller = null;
            Root = null;
            ProcedureName = string.Empty;
            SopStepCount = 0;
            CurrentIndex = -1;
            CurrentGroup = -1;
        }

        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return Spaces.Replace(Tags.Replace(text, string.Empty), " ").Trim();
        }

        /// <summary>Reads the controller's step flow. Returns a short status message.</summary>
        public string Load(MonoBehaviour controller)
        {
            Clear();
            Controller = controller;
            if (controller == null)
                return "Waiting for the flow controller to load.";

            Root = Reflect.Read(controller, "sopRoot");
            if (Root == null || !(Reflect.Read(controller, "stepFlow") is IEnumerable flow))
                return "Waiting for the workflow to load.";

            int index = 0;
            FlowGroup group = null;
            foreach (object raw in flow)
            {
                object step = Reflect.Read(raw, "step");
                object sequence = Reflect.Read(raw, "sequence");
                object sub = Reflect.Read(raw, "subSequence");
                if (step == null || sequence == null)
                    continue;

                int sequenceIndex = Reflect.Int(raw, "sequenceIndex");
                string seqId = Clean(Reflect.Str(sequence, "sequenceId"));
                string seqTitle = Clean(Reflect.Str(sequence, "title"));
                string subId = sub == null ? string.Empty : Clean(Reflect.Str(sub, "subSequenceId"));
                string subTitle = sub == null ? string.Empty : Clean(Reflect.Str(sub, "title"));
                string stepNo = Clean(Reflect.Str(step, "stepNo"));
                string title = StepTitle(step);

                if (group == null || group.SequenceIndex != sequenceIndex)
                {
                    group = new FlowGroup { SequenceIndex = sequenceIndex, Id = seqId, Title = seqTitle };
                    Groups.Add(group);
                }

                string label = $"Sequence {seqId} - {seqTitle}";
                if (sub != null)
                    label += $" / Subsequence {subId} - {subTitle}";
                label += $" / Step {stepNo}: {title}";

                FlowEntry entry = new FlowEntry
                {
                    FlowIndex = index++,
                    SequenceIndex = sequenceIndex,
                    Group = Groups.Count - 1,
                    StepNo = stepNo,
                    Title = title,
                    SubKey = sub == null ? string.Empty : subId + "|" + subTitle,
                    SubText = sub == null ? string.Empty : $"Subsequence {subId}: {subTitle}",
                    Label = label
                };
                Entries.Add(entry);
                group.Steps.Add(entry);
            }
            SopStepCount = index;

            object post = Reflect.Read(Root, "postSequence");
            if (Reflect.Read(post, "steps") is IEnumerable postSteps)
            {
                FlowGroup postGroup = null;
                foreach (object step in postSteps)
                {
                    if (postGroup == null)
                    {
                        string postTitle = Clean(Reflect.Str(post, "title"));
                        postGroup = new FlowGroup
                        {
                            SequenceIndex = -1,
                            IsPost = true,
                            Id = "P",
                            Title = postTitle.Length > 0 ? postTitle : "Post-sequence"
                        };
                        Groups.Add(postGroup);
                    }

                    string stepNo = Clean(Reflect.Str(step, "stepNo"));
                    string title = StepTitle(step);
                    FlowEntry entry = new FlowEntry
                    {
                        FlowIndex = index++,
                        SequenceIndex = -1,
                        Group = Groups.Count - 1,
                        StepNo = stepNo,
                        Title = title,
                        Label = $"Post-sequence / Step {stepNo}: {title}"
                    };
                    Entries.Add(entry);
                    postGroup.Steps.Add(entry);
                }
            }

            PollCursor();
            return Entries.Count == 0
                ? "Workflow loaded, but it has no steps."
                : $"Loaded {Entries.Count} steps in {Groups.Count} sequences.";
        }

        private static string StepTitle(object step)
        {
            string title = Clean(Reflect.Str(step, "title"));
            if (title.Length > 0)
                return title;
            string instruction = Clean(Reflect.Str(step, "instruction"));
            return instruction.Length > 0 ? instruction : Clean(Reflect.Str(step, "stepId"));
        }

        /// <summary>Re-reads where the app currently is. Returns true when the position changed.</summary>
        public bool PollCursor()
        {
            if (Controller == null)
                return false;

            bool post = Reflect.Bool(Controller, "isPostSequenceMode");
            int index = post
                ? SopStepCount + Reflect.Int(Controller, "currentPostStepIndex")
                : Reflect.Int(Controller, "currentStepIndex");
            if (index < 0 || index >= Entries.Count)
                index = -1;
            if (index == CurrentIndex)
                return false;

            CurrentIndex = index;
            CurrentGroup = index < 0 ? -1 : Entries[index].Group;
            return true;
        }

        /// <summary>Loads the step at <paramref name="index"/> through the app's normal step-loading path.</summary>
        public bool GoTo(int index, out string message)
        {
            if (!IsLoaded || index < 0 || index >= Entries.Count)
            {
                message = "Could not jump: that step is not available in the live workflow.";
                return false;
            }

            FlowEntry entry = Entries[index];
            Type type = Controller.GetType();

            ISet<int> visited = Reflect.Read(Controller, "visitedSequenceIndices") as ISet<int>;
            if (visited == null)
            {
                message = "Could not jump: the workflow does not expose its sequence progress.";
                return false;
            }
            if (!(Reflect.Read(Root, "sop") is ICollection sequences) || sequences.Count == 0)
            {
                message = "Could not jump: the workflow does not contain SOP sequences.";
                return false;
            }
            if (SopStepCount == 0)
            {
                message = "Could not jump: the workflow has no SOP steps.";
                return false;
            }

            bool isPost = entry.SequenceIndex < 0;
            int targetSequence = isPost ? sequences.Count - 1 : entry.SequenceIndex;

            if (Reflect.FindMethod(type, isPost ? "ShowPostSequenceStep" : "ShowStep") == null)
            {
                message = isPost
                    ? "Could not jump: post-sequence navigation is not supported by this workflow."
                    : "Could not jump: this workflow cannot show a step directly.";
                return false;
            }
            if (Reflect.FindMethod(type, "StopCurrentFlow") == null)
            {
                message = "Could not jump: this workflow cannot stop its current step.";
                return false;
            }
            if (Reflect.FindMethod(type, "MarkSequenceCompleted", 0) == null)
            {
                message = "Could not jump: this workflow does not track sequence completion.";
                return false;
            }
            if (Reflect.FindMethod(type, "RefreshSequenceMenu", 0) == null)
            {
                message = "Could not jump: this workflow cannot refresh the sequence panel.";
                return false;
            }

            try
            {
                Reflect.Call(Controller, "StopCurrentFlow");

                for (int sequenceIndex = 0; sequenceIndex <= targetSequence; sequenceIndex++)
                {
                    visited.Add(sequenceIndex);
                    if (isPost || sequenceIndex < targetSequence)
                        Reflect.Call(Controller, "MarkSequenceCompleted", sequenceIndex);
                }

                if (!Reflect.Write(Controller, "currentStepIndex", isPost ? SopStepCount - 1 : entry.FlowIndex))
                    throw new InvalidOperationException("The workflow cursor could not be updated.");
                if (!Reflect.Write(Controller, "isPostSequenceMode", isPost))
                    throw new InvalidOperationException("The workflow mode could not be updated.");
                if (!Reflect.Write(Controller, "currentPostStepIndex", isPost ? index - SopStepCount : 0))
                    throw new InvalidOperationException("The post-sequence cursor could not be updated.");

                Reflect.Write(Controller, "showingProcedureComplete", false);
                Reflect.Write(Controller, "showingTrainingComplete", false);
                Reflect.Call(Controller, "RefreshSequenceMenu", targetSequence);
                Reflect.Call(Controller, isPost ? "ShowPostSequenceStep" : "ShowStep");

                PollCursor();
                message = $"Jumped to step {index + 1} of {Entries.Count}: {entry.Title}";
                return true;
            }
            catch (Exception exception)
            {
                message = $"Could not jump: {exception.GetBaseException().Message}";
                Debug.LogException(exception);
                return false;
            }
        }
    }
}
