// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Text;

namespace TarkovServerReporter
{
    public sealed class OperationNoteCheck
    {
        public OperationNoteCheck() { Id = Guid.NewGuid().ToString("N"); Text = string.Empty; }
        public string Id { get; set; }
        public string Text { get; set; }
        public bool Done { get; set; }
    }

    public sealed class OperationPinnedNote
    {
        public OperationPinnedNote()
        {
            Id = Guid.NewGuid().ToString("N");
            Title = string.Empty;
            BodyText = string.Empty;
            Checks = new List<OperationNoteCheck>();
        }
        public string Id { get; set; }
        public string Title { get; set; }
        public string BodyText { get; set; }
        public List<OperationNoteCheck> Checks { get; set; }
    }

    public sealed class OperationNoteDraft
    {
        public OperationNoteDraft()
        {
            BodyText = string.Empty;
            Checks = new List<OperationNoteCheck>();
            Pins = new List<OperationPinnedNote>();
            ActiveOpacity = 0.92;
            LockedOpacity = 0.42;
            ObservedRaidKey = string.Empty;
        }
        public string BodyText { get; set; }
        public List<OperationNoteCheck> Checks { get; set; }
        public List<OperationPinnedNote> Pins { get; set; }
        public double ActiveOpacity { get; set; }
        public double LockedOpacity { get; set; }
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public string ObservedRaidKey { get; set; }
        public long Revision { get; set; }
        public DateTime UpdatedUtc { get; set; }

        public string ToNoteText() { return RenderText(BodyText, Checks); }

        public static string RenderText(string bodyText, IEnumerable<OperationNoteCheck> checks)
        {
            var text = new StringBuilder(bodyText ?? string.Empty);
            if (checks != null)
            {
                foreach (OperationNoteCheck check in checks)
                {
                    if (check == null) continue;
                    if (text.Length > 0) text.AppendLine();
                    text.Append(check.Done ? "[x] " : "[ ] ");
                    text.Append(check.Text ?? string.Empty);
                }
            }
            return text.ToString();
        }
    }

    public sealed class OperationNoteSaveResult
    {
        public bool Saved { get; set; }
        public bool AlreadySaved { get; set; }
        public string RaidKey { get; set; }
        public int ScreenshotCount { get; set; }
        public string Message { get; set; }
    }

    public sealed class OperationNoteSnapshot
    {
        public OperationNoteSnapshot()
        {
            Checks = new List<OperationNoteCheck>();
            ScreenshotPaths = new List<string>();
        }
        public int SchemaVersion { get; set; }
        public string RaidKey { get; set; }
        public string BodyText { get; set; }
        public List<OperationNoteCheck> Checks { get; set; }
        public List<string> ScreenshotPaths { get; set; }
        public DateTime SavedUtc { get; set; }
        public DateTime RaidStartedUtc { get; set; }
        public DateTime RaidEndedUtc { get; set; }
        public string MapName { get; set; }
        public long DraftRevision { get; set; }
        public string ToNoteText() { return OperationNoteDraft.RenderText(BodyText, Checks); }
    }
}
