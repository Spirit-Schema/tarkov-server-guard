// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    public enum PartyBlockInputTokenStatus
    {
        Valid,
        Duplicate,
        InvalidPublicIpv4,
        AddressLimitExceeded,
        InputTooLong
    }

    public sealed class PartyBlockInputToken
    {
        public string OriginalText { get; set; }
        public string IpAddress { get; set; }
        public PartyBlockInputTokenStatus Status { get; set; }
    }

    public sealed class PartyBlockInputParseResult
    {
        public PartyBlockInputParseResult()
        {
            Items = new List<PartyBlockInputToken>();
            UniqueAddresses = new List<string>();
        }

        public IList<PartyBlockInputToken> Items { get; set; }
        public IList<string> UniqueAddresses { get; set; }
    }

    public static class PartyBlockInputParser
    {
        public const int MaximumAddressCount = 1024;
        public const int MaximumInputLength = 32768;

        public static PartyBlockInputParseResult Parse(string input)
        {
            var result = new PartyBlockInputParseResult();
            string raw = input ?? string.Empty;
            if (raw.Length > MaximumInputLength)
            {
                result.Items.Add(new PartyBlockInputToken
                {
                    OriginalText = raw.Substring(0, Math.Min(80, raw.Length)),
                    Status = PartyBlockInputTokenStatus.InputTooLong
                });
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string token in Regex.Split(raw, @"[\s,;]+"))
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                if (!FirewallRuleManager.IsPublicIpv4(token))
                {
                    result.Items.Add(new PartyBlockInputToken
                    {
                        OriginalText = token,
                        Status = PartyBlockInputTokenStatus.InvalidPublicIpv4
                    });
                    continue;
                }
                if (!seen.Add(token))
                {
                    result.Items.Add(new PartyBlockInputToken
                    {
                        OriginalText = token,
                        IpAddress = token,
                        Status = PartyBlockInputTokenStatus.Duplicate
                    });
                    continue;
                }
                if (result.UniqueAddresses.Count >= MaximumAddressCount)
                {
                    result.Items.Add(new PartyBlockInputToken
                    {
                        OriginalText = token,
                        IpAddress = token,
                        Status = PartyBlockInputTokenStatus.AddressLimitExceeded
                    });
                    continue;
                }

                result.UniqueAddresses.Add(token);
                result.Items.Add(new PartyBlockInputToken
                {
                    OriginalText = token,
                    IpAddress = token,
                    Status = PartyBlockInputTokenStatus.Valid
                });
            }
            return result;
        }
    }

    public enum PartyBlockMemberState
    {
        Active,
        PendingApply,
        PendingRelease
    }

    public enum PartyBlockBundleLifecycleState
    {
        Active,
        PendingApply,
        PendingRelease,
        NeedsReconcile
    }

    public sealed class PartyBlockBundleMember
    {
        public string IpAddress { get; set; }
        public PartyBlockMemberState State { get; set; }
        public string LastError { get; set; }
    }

    public sealed class PartyBlockBundle
    {
        public PartyBlockBundle()
        {
            Members = new List<PartyBlockBundleMember>();
        }

        public string BundleId { get; set; }
        public string SourceName { get; set; }
        public string ReasonCode { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public PartyBlockBundleLifecycleState State { get; set; }
        public IList<PartyBlockBundleMember> Members { get; set; }
    }

    public sealed class PartyBlockAddressLease
    {
        public PartyBlockAddressLease()
        {
            BundleIds = new List<string>();
        }

        public string IpAddress { get; set; }
        public bool WasBlockedBeforeFirstBundle { get; set; }
        public bool PreserveAfterBundles { get; set; }
        public IList<string> BundleIds { get; set; }
    }

    public sealed class PartyBlockBundleSnapshot
    {
        public PartyBlockBundleSnapshot()
        {
            Bundles = new List<PartyBlockBundle>();
            AddressLeases = new List<PartyBlockAddressLease>();
        }

        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public IList<PartyBlockBundle> Bundles { get; set; }
        public IList<PartyBlockAddressLease> AddressLeases { get; set; }
    }

    public sealed class PartyBlockBundleSummary
    {
        public string BundleId { get; set; }
        public string SourceName { get; set; }
        public string ReasonCode { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public PartyBlockBundleLifecycleState State { get; set; }
    }

    public sealed class PartyBlockSourceInfo
    {
        public PartyBlockSourceInfo()
        {
            Bundles = new List<PartyBlockBundleSummary>();
        }

        public string IpAddress { get; set; }
        public bool WasBlockedBeforeFirstBundle { get; set; }
        public bool PreserveAfterBundles { get; set; }
        public bool HasPendingReconcile { get; set; }
        public IList<PartyBlockBundleSummary> Bundles { get; set; }

        public bool IsTemporaryOnly
        {
            get { return !WasBlockedBeforeFirstBundle && !PreserveAfterBundles; }
        }

        public DateTime? EarliestCreatedAtUtc
        {
            get
            {
                return Bundles.Count == 0
                    ? (DateTime?)null
                    : Bundles.Min(item => item.CreatedAtUtc);
            }
        }
    }

    public sealed class PartyBlockStoreResult
    {
        public PartyBlockStoreResult()
        {
            AddressesToChange = new List<string>();
            AllAddresses = new List<string>();
        }

        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string BundleId { get; set; }
        public IList<string> AddressesToChange { get; set; }
        public IList<string> AllAddresses { get; set; }
        public int ActiveCount { get; set; }
        public int PendingCount { get; set; }
        public bool BundleCompleted { get; set; }
    }

    public enum PartyBlockReleaseDisposition
    {
        Remove,
        PreserveBaseline,
        PreservePermanent,
        PreserveOverlap,
        AlreadyMissing,
        PendingReconcile
    }

    public sealed class PartyBlockReleasePreviewItem
    {
        public string IpAddress { get; set; }
        public PartyBlockReleaseDisposition Disposition { get; set; }
        public int OtherActiveBundleCount { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class PartyBlockReleasePreview
    {
        public PartyBlockReleasePreview()
        {
            Items = new List<PartyBlockReleasePreviewItem>();
        }

        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public PartyBlockBundle Bundle { get; set; }
        public IList<PartyBlockReleasePreviewItem> Items { get; set; }
    }

    public sealed class PartyBlockBundleStore
    {
        private enum StoreFileLoadState
        {
            Missing,
            Valid,
            Invalid
        }

        private sealed class StoreDocument
        {
            public int Version { get; set; }
            public List<BundleItem> Bundles { get; set; }
            public List<AddressItem> Addresses { get; set; }
        }

        private sealed class BundleItem
        {
            public string Id { get; set; }
            public string Source { get; set; }
            public string Reason { get; set; }
            public string CreatedAtUtc { get; set; }
            public string State { get; set; }
            public List<MemberItem> Members { get; set; }
        }

        private sealed class MemberItem
        {
            public string Ip { get; set; }
            public string State { get; set; }
            public string LastError { get; set; }
        }

        private sealed class AddressItem
        {
            public string Ip { get; set; }
            // Missing ownership flags are unknown, never implicit permission
            // to remove a preexisting or explicitly preserved personal block.
            // Keep raw JSON primitive types here so the serializer cannot coerce
            // strings/numbers into booleans before ownership is validated.
            public object BaselineBlocked { get; set; }
            public object Preserve { get; set; }
            public List<string> BundleIds { get; set; }
        }

        public const int MaximumSourceNameLength = 60;
        public const int MaximumReasonCodeLength = 40;
        private const int CurrentVersion = 1;
        private const int MaximumBundleCount = 256;
        private const int MaximumTrackedAddressCount = 4096;
        private const int MaximumFileBytes = 2 * 1024 * 1024;
        private const int MaximumStoredErrorLength = 500;
        private static readonly object SyncRoot = new object();
        private static readonly PartyBlockBundleStore DefaultInstance =
            new PartyBlockBundleStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard"));

        private readonly string _storeDirectory;
        private readonly string _storePath;
        private readonly string _backupPath;
        private readonly Action _beforePrimaryCommit;

        public PartyBlockBundleStore(string storeDirectory)
            : this(storeDirectory, null)
        {
        }

        internal PartyBlockBundleStore(string storeDirectory, Action beforePrimaryCommit)
        {
            if (string.IsNullOrWhiteSpace(storeDirectory))
                throw new ArgumentException("Store directory is required.", "storeDirectory");
            _storeDirectory = Path.GetFullPath(storeDirectory);
            _storePath = Path.Combine(_storeDirectory, "party-block-bundles.json");
            _backupPath = _storePath + ".bak";
            _beforePrimaryCommit = beforePrimaryCommit;
        }

        public static PartyBlockBundleStore Default
        {
            get { return DefaultInstance; }
        }

        internal string StorePath
        {
            get { return _storePath; }
        }

        internal string BackupPath
        {
            get { return _backupPath; }
        }

        public PartyBlockBundleSnapshot LoadSnapshot()
        {
            lock (SyncRoot)
            {
                if (HasUnresolvedRecovery())
                    return CreateSnapshot(false,
                        "A previous party ownership transaction has unresolved recovery files. "
                            + "The files were preserved; mutation is disabled until the ownership data is recovered.",
                        new List<PartyBlockBundle>(), new List<PartyBlockAddressLease>());
                List<PartyBlockBundle> bundles;
                List<PartyBlockAddressLease> addresses;
                StoreFileLoadState primary = LoadFile(_storePath, out bundles, out addresses);
                if (primary == StoreFileLoadState.Valid)
                    return CreateSnapshot(true, null, bundles, addresses);

                StoreFileLoadState backup = LoadFile(_backupPath, out bundles, out addresses);
                if (backup == StoreFileLoadState.Valid)
                    return CreateSnapshot(true, null, bundles, addresses);
                if (primary == StoreFileLoadState.Missing && backup == StoreFileLoadState.Missing)
                    return CreateSnapshot(
                        true,
                        null,
                        new List<PartyBlockBundle>(),
                        new List<PartyBlockAddressLease>());
                return CreateSnapshot(
                    false,
                    "Party bundle metadata is invalid; mutation is disabled.",
                    new List<PartyBlockBundle>(),
                    new List<PartyBlockAddressLease>());
            }
        }

        public PartyBlockStoreResult BeginApply(
            string sourceName,
            string reasonCode,
            IEnumerable<string> ipAddresses,
            IDictionary<string, FirewallQueryResult> initialStates)
        {
            var result = new PartyBlockStoreResult();
            string source = NormalizeSingleLine(sourceName, MaximumSourceNameLength);
            string reason = NormalizeReasonCode(reasonCode);
            IList<string> addressesToAdd = NormalizeAddresses(ipAddresses);
            if (source == null || reason == null || addressesToAdd.Count == 0)
            {
                result.ErrorMessage = "Invalid party bundle input.";
                return result;
            }
            if (addressesToAdd.Count > PartyBlockInputParser.MaximumAddressCount)
            {
                result.ErrorMessage = "Party bundle address limit exceeded.";
                return result;
            }
            foreach (string ipAddress in addressesToAdd)
            {
                FirewallQueryResult state;
                if (initialStates == null
                    || !initialStates.TryGetValue(ipAddress, out state)
                    || state == null
                    || !state.Success)
                {
                    result.ErrorMessage = "Initial firewall state is unavailable.";
                    return result;
                }
            }

            lock (SyncRoot)
            {
                List<PartyBlockBundle> bundles;
                List<PartyBlockAddressLease> leases;
                if (!TryLoadForMutation(out bundles, out leases))
                {
                    result.ErrorMessage = "Party bundle metadata is unavailable.";
                    return result;
                }
                if (bundles.Count >= MaximumBundleCount)
                {
                    result.ErrorMessage = "Party bundle count limit exceeded.";
                    return result;
                }

                var bundle = new PartyBlockBundle
                {
                    BundleId = Guid.NewGuid().ToString("N"),
                    SourceName = source,
                    ReasonCode = reason,
                    CreatedAtUtc = DateTime.UtcNow,
                    State = PartyBlockBundleLifecycleState.Active
                };
                foreach (string ipAddress in addressesToAdd)
                {
                    FirewallQueryResult state = initialStates[ipAddress];
                    PartyBlockAddressLease lease = FindLease(leases, ipAddress);
                    if (lease == null)
                    {
                        if (leases.Count >= MaximumTrackedAddressCount)
                        {
                            result.ErrorMessage = "Party bundle address store limit exceeded.";
                            return result;
                        }
                        lease = new PartyBlockAddressLease
                        {
                            IpAddress = ipAddress,
                            WasBlockedBeforeFirstBundle = state.IsBlocked
                        };
                        leases.Add(lease);
                    }
                    if (!lease.BundleIds.Contains(bundle.BundleId, StringComparer.OrdinalIgnoreCase))
                        lease.BundleIds.Add(bundle.BundleId);

                    PartyBlockMemberState memberState = state.IsBlocked
                        ? PartyBlockMemberState.Active
                        : PartyBlockMemberState.PendingApply;
                    bundle.Members.Add(new PartyBlockBundleMember
                    {
                        IpAddress = ipAddress,
                        State = memberState
                    });
                    if (memberState == PartyBlockMemberState.PendingApply)
                    {
                        result.AddressesToChange.Add(ipAddress);
                        MarkAddressMembersPendingApply(bundles, ipAddress);
                    }
                }
                bundle.State = result.AddressesToChange.Count == 0
                    ? PartyBlockBundleLifecycleState.Active
                    : PartyBlockBundleLifecycleState.PendingApply;
                bundles.Add(bundle);
                UpdateBundleStates(bundles);
                if (!SaveCore(bundles, leases))
                {
                    result.ErrorMessage = "Party bundle metadata could not be saved.";
                    return result;
                }

                result.Success = true;
                result.BundleId = bundle.BundleId;
                result.AllAddresses = addressesToAdd.ToList();
                result.ActiveCount = bundle.Members.Count(
                    item => item.State == PartyBlockMemberState.Active);
                result.PendingCount = bundle.Members.Count - result.ActiveCount;
                return result;
            }
        }

        public PartyBlockStoreResult CompleteApply(
            string bundleId,
            IDictionary<string, FirewallQueryResult> finalStates,
            FirewallBatchChangeResult batchResult)
        {
            lock (SyncRoot)
            {
                List<PartyBlockBundle> bundles;
                List<PartyBlockAddressLease> leases;
                if (!TryLoadForMutation(out bundles, out leases))
                    return Failure("Party bundle metadata is unavailable.");
                PartyBlockBundle target = FindBundle(bundles, bundleId);
                if (target == null) return Failure("Party bundle was not found.");

                IDictionary<string, string> batchErrors = CreateBatchErrorMap(batchResult);
                foreach (PartyBlockBundleMember member in target.Members.ToList())
                {
                    FirewallQueryResult state;
                    bool verifiedBlocked = finalStates != null
                        && finalStates.TryGetValue(member.IpAddress, out state)
                        && state != null
                        && state.Success
                        && state.IsBlocked;
                    if (verifiedBlocked)
                    {
                        MarkAddressMembersActive(bundles, member.IpAddress);
                    }
                    else
                    {
                        member.State = PartyBlockMemberState.PendingApply;
                        member.LastError = GetStateError(member.IpAddress, finalStates, batchErrors);
                    }
                }
                UpdateBundleStates(bundles);
                if (!SaveCore(bundles, leases))
                    return Failure("Party bundle result could not be saved.");
                return BuildBundleResult(target, false);
            }
        }

        public PartyBlockStoreResult AbandonBundle(string bundleId)
        {
            lock (SyncRoot)
            {
                List<PartyBlockBundle> bundles;
                List<PartyBlockAddressLease> leases;
                if (!TryLoadForMutation(out bundles, out leases))
                    return Failure("Party bundle metadata is unavailable.");
                PartyBlockBundle target = FindBundle(bundles, bundleId);
                if (target == null) return Failure("Party bundle was not found.");
                IList<string> all = target.Members.Select(item => item.IpAddress).ToList();
                DetachBundle(bundles, leases, target);
                if (!SaveCore(bundles, leases))
                    return Failure("Party bundle metadata could not be saved.");
                return new PartyBlockStoreResult
                {
                    Success = true,
                    BundleId = bundleId,
                    AllAddresses = all,
                    BundleCompleted = true
                };
            }
        }

        public PartyBlockReleasePreview CreateReleasePreview(
            string bundleId,
            IDictionary<string, FirewallQueryResult> states)
        {
            PartyBlockBundleSnapshot snapshot = LoadSnapshot();
            if (!snapshot.Success)
                return new PartyBlockReleasePreview
                {
                    ErrorMessage = snapshot.ErrorMessage
                };
            PartyBlockBundle bundle = FindBundle(snapshot.Bundles, bundleId);
            if (bundle == null)
                return new PartyBlockReleasePreview { ErrorMessage = "Party bundle was not found." };

            var preview = new PartyBlockReleasePreview
            {
                Success = true,
                Bundle = CloneBundle(bundle)
            };
            foreach (PartyBlockBundleMember member in bundle.Members)
            {
                PartyBlockAddressLease lease = FindLease(snapshot.AddressLeases, member.IpAddress);
                preview.Items.Add(CreateReleasePreviewItem(
                    bundle.BundleId,
                    member.IpAddress,
                    lease,
                    states));
            }
            return preview;
        }

        public PartyBlockStoreResult BeginRelease(
            string bundleId,
            IDictionary<string, FirewallQueryResult> currentStates)
        {
            lock (SyncRoot)
            {
                List<PartyBlockBundle> bundles;
                List<PartyBlockAddressLease> leases;
                if (!TryLoadForMutation(out bundles, out leases))
                    return Failure("Party bundle metadata is unavailable.");
                PartyBlockBundle target = FindBundle(bundles, bundleId);
                if (target == null) return Failure("Party bundle was not found.");

                var result = new PartyBlockStoreResult
                {
                    Success = true,
                    BundleId = target.BundleId,
                    AllAddresses = target.Members.Select(item => item.IpAddress).ToList()
                };
                foreach (PartyBlockBundleMember member in target.Members.ToList())
                {
                    PartyBlockAddressLease lease = FindLease(leases, member.IpAddress);
                    PartyBlockReleasePreviewItem preview = CreateReleasePreviewItem(
                        target.BundleId,
                        member.IpAddress,
                        lease,
                        currentStates);
                    if (preview.Disposition == PartyBlockReleaseDisposition.PreserveBaseline
                        || preview.Disposition == PartyBlockReleaseDisposition.PreservePermanent
                        || preview.Disposition == PartyBlockReleaseDisposition.PreserveOverlap
                        || preview.Disposition == PartyBlockReleaseDisposition.AlreadyMissing)
                    {
                        DetachMember(target, leases, member.IpAddress);
                        continue;
                    }

                    member.State = PartyBlockMemberState.PendingRelease;
                    member.LastError = preview.ErrorMessage;
                    if (preview.Disposition == PartyBlockReleaseDisposition.Remove)
                        result.AddressesToChange.Add(member.IpAddress);
                }
                CleanupEmptyBundlesAndLeases(bundles, leases);
                UpdateBundleStates(bundles);
                PartyBlockBundle remaining = FindBundle(bundles, bundleId);
                result.BundleCompleted = remaining == null;
                if (!SaveCore(bundles, leases))
                    return Failure("Party bundle release journal could not be saved.");
                if (remaining != null)
                {
                    PartyBlockStoreResult counts = BuildBundleResult(remaining, false);
                    result.ActiveCount = counts.ActiveCount;
                    result.PendingCount = counts.PendingCount;
                }
                return result;
            }
        }

        public PartyBlockStoreResult CompleteRelease(
            string bundleId,
            IDictionary<string, FirewallQueryResult> finalStates,
            FirewallBatchChangeResult batchResult)
        {
            lock (SyncRoot)
            {
                List<PartyBlockBundle> bundles;
                List<PartyBlockAddressLease> leases;
                if (!TryLoadForMutation(out bundles, out leases))
                    return Failure("Party bundle metadata is unavailable.");
                PartyBlockBundle target = FindBundle(bundles, bundleId);
                if (target == null)
                    return new PartyBlockStoreResult
                    {
                        Success = true,
                        BundleId = bundleId,
                        BundleCompleted = true
                    };

                IDictionary<string, string> batchErrors = CreateBatchErrorMap(batchResult);
                foreach (PartyBlockBundleMember member in target.Members.ToList())
                {
                    if (member.State != PartyBlockMemberState.PendingRelease) continue;
                    PartyBlockAddressLease lease = FindLease(leases, member.IpAddress);
                    int otherReferences = CountOtherReferences(lease, target.BundleId);
                    if (lease == null
                        || lease.WasBlockedBeforeFirstBundle
                        || lease.PreserveAfterBundles
                        || otherReferences > 0)
                    {
                        DetachMember(target, leases, member.IpAddress);
                        continue;
                    }

                    FirewallQueryResult state;
                    bool verifiedMissing = finalStates != null
                        && finalStates.TryGetValue(member.IpAddress, out state)
                        && state != null
                        && state.Success
                        && !state.IsBlocked;
                    if (verifiedMissing)
                    {
                        DetachMember(target, leases, member.IpAddress);
                    }
                    else
                    {
                        member.LastError = GetStateError(
                            member.IpAddress,
                            finalStates,
                            batchErrors);
                    }
                }
                CleanupEmptyBundlesAndLeases(bundles, leases);
                UpdateBundleStates(bundles);
                PartyBlockBundle remaining = FindBundle(bundles, bundleId);
                if (!SaveCore(bundles, leases))
                    return Failure("Party bundle release result could not be saved.");
                return remaining == null
                    ? new PartyBlockStoreResult
                    {
                        Success = true,
                        BundleId = bundleId,
                        BundleCompleted = true
                    }
                    : BuildBundleResult(remaining, false);
            }
        }

        public PartyBlockStoreResult ReconcileStates(
            IDictionary<string, FirewallQueryResult> states)
        {
            lock (SyncRoot)
            {
                List<PartyBlockBundle> bundles;
                List<PartyBlockAddressLease> leases;
                if (!TryLoadForMutation(out bundles, out leases))
                    return Failure("Party bundle metadata is unavailable.");
                bool changed = false;
                foreach (PartyBlockBundle bundle in bundles.ToList())
                {
                    foreach (PartyBlockBundleMember member in bundle.Members.ToList())
                    {
                        FirewallQueryResult state;
                        if (states == null
                            || !states.TryGetValue(member.IpAddress, out state)
                            || state == null
                            || !state.Success)
                            continue;

                        PartyBlockAddressLease lease = FindLease(leases, member.IpAddress);
                        if (member.State == PartyBlockMemberState.PendingRelease)
                        {
                            if (lease == null
                                || lease.WasBlockedBeforeFirstBundle
                                || lease.PreserveAfterBundles
                                || CountOtherReferences(lease, bundle.BundleId) > 0
                                || !state.IsBlocked)
                            {
                                DetachMember(bundle, leases, member.IpAddress);
                                changed = true;
                            }
                            continue;
                        }

                        if (state.IsBlocked)
                        {
                            if (member.State != PartyBlockMemberState.Active
                                || !string.IsNullOrWhiteSpace(member.LastError))
                            {
                                member.State = PartyBlockMemberState.Active;
                                member.LastError = null;
                                changed = true;
                            }
                        }
                        else if (member.State != PartyBlockMemberState.PendingApply)
                        {
                            member.State = PartyBlockMemberState.PendingApply;
                            member.LastError = "Managed block is not currently present.";
                            changed = true;
                        }
                    }
                }
                CleanupEmptyBundlesAndLeases(bundles, leases);
                changed |= UpdateBundleStates(bundles);
                if (changed && !SaveCore(bundles, leases))
                    return Failure("Party bundle reconciliation could not be saved.");
                return new PartyBlockStoreResult
                {
                    Success = true,
                    ActiveCount = bundles.Sum(bundle => bundle.Members.Count(
                        item => item.State == PartyBlockMemberState.Active)),
                    PendingCount = bundles.Sum(bundle => bundle.Members.Count(
                        item => item.State != PartyBlockMemberState.Active))
                };
            }
        }

        public PartyBlockStoreResult PromoteAddresses(IEnumerable<string> ipAddresses)
        {
            IList<string> addresses = NormalizeAddresses(ipAddresses);
            lock (SyncRoot)
            {
                List<PartyBlockBundle> bundles;
                List<PartyBlockAddressLease> leases;
                if (!TryLoadForMutation(out bundles, out leases))
                    return Failure("Party bundle metadata is unavailable.");
                bool changed = false;
                foreach (string ipAddress in addresses)
                {
                    PartyBlockAddressLease lease = FindLease(leases, ipAddress);
                    if (lease == null || lease.PreserveAfterBundles) continue;
                    lease.PreserveAfterBundles = true;
                    changed = true;
                }
                if (changed && !SaveCore(bundles, leases))
                    return Failure("Party bundle promotion could not be saved.");
                return new PartyBlockStoreResult { Success = true };
            }
        }

        public PartyBlockStoreResult ForgetAddresses(IEnumerable<string> ipAddresses)
        {
            IList<string> addresses = NormalizeAddresses(ipAddresses);
            lock (SyncRoot)
            {
                List<PartyBlockBundle> bundles;
                List<PartyBlockAddressLease> leases;
                if (!TryLoadForMutation(out bundles, out leases))
                    return Failure("Party bundle metadata is unavailable.");
                bool changed = false;
                foreach (string ipAddress in addresses)
                {
                    foreach (PartyBlockBundle bundle in bundles)
                    {
                        PartyBlockBundleMember member = FindMember(bundle, ipAddress);
                        if (member == null) continue;
                        bundle.Members.Remove(member);
                        changed = true;
                    }
                    PartyBlockAddressLease lease = FindLease(leases, ipAddress);
                    if (lease != null)
                    {
                        leases.Remove(lease);
                        changed = true;
                    }
                }
                CleanupEmptyBundlesAndLeases(bundles, leases);
                changed |= UpdateBundleStates(bundles);
                if (changed && !SaveCore(bundles, leases))
                    return Failure("Party bundle metadata cleanup could not be saved.");
                return new PartyBlockStoreResult { Success = true };
            }
        }

        public static IDictionary<string, PartyBlockSourceInfo> CreateSourceMap(
            PartyBlockBundleSnapshot snapshot)
        {
            var map = new Dictionary<string, PartyBlockSourceInfo>(
                StringComparer.OrdinalIgnoreCase);
            if (snapshot == null || !snapshot.Success) return map;
            foreach (PartyBlockAddressLease lease in snapshot.AddressLeases)
            {
                var info = new PartyBlockSourceInfo
                {
                    IpAddress = lease.IpAddress,
                    WasBlockedBeforeFirstBundle = lease.WasBlockedBeforeFirstBundle,
                    PreserveAfterBundles = lease.PreserveAfterBundles
                };
                foreach (string bundleId in lease.BundleIds)
                {
                    PartyBlockBundle bundle = FindBundle(snapshot.Bundles, bundleId);
                    if (bundle == null) continue;
                    PartyBlockBundleMember member = FindMember(bundle, lease.IpAddress);
                    info.Bundles.Add(new PartyBlockBundleSummary
                    {
                        BundleId = bundle.BundleId,
                        SourceName = bundle.SourceName,
                        ReasonCode = bundle.ReasonCode,
                        CreatedAtUtc = bundle.CreatedAtUtc,
                        State = bundle.State
                    });
                    if (member != null && member.State != PartyBlockMemberState.Active)
                        info.HasPendingReconcile = true;
                }
                if (info.Bundles.Count > 0) map[info.IpAddress] = info;
            }
            return map;
        }

        private PartyBlockReleasePreviewItem CreateReleasePreviewItem(
            string bundleId,
            string ipAddress,
            PartyBlockAddressLease lease,
            IDictionary<string, FirewallQueryResult> states)
        {
            var item = new PartyBlockReleasePreviewItem { IpAddress = ipAddress };
            if (lease == null)
            {
                item.Disposition = PartyBlockReleaseDisposition.PendingReconcile;
                item.ErrorMessage = "Address lease is missing.";
                return item;
            }
            if (lease.WasBlockedBeforeFirstBundle)
            {
                item.Disposition = PartyBlockReleaseDisposition.PreserveBaseline;
                return item;
            }
            if (lease.PreserveAfterBundles)
            {
                item.Disposition = PartyBlockReleaseDisposition.PreservePermanent;
                return item;
            }
            item.OtherActiveBundleCount = CountOtherReferences(lease, bundleId);
            if (item.OtherActiveBundleCount > 0)
            {
                item.Disposition = PartyBlockReleaseDisposition.PreserveOverlap;
                return item;
            }

            FirewallQueryResult state = null;
            if (states == null
                || !states.TryGetValue(ipAddress, out state)
                || state == null
                || !state.Success)
            {
                item.Disposition = PartyBlockReleaseDisposition.PendingReconcile;
                item.ErrorMessage = state == null || string.IsNullOrWhiteSpace(state.ErrorMessage)
                    ? "Firewall state is unavailable."
                    : state.ErrorMessage;
                return item;
            }
            item.Disposition = state.IsBlocked
                ? PartyBlockReleaseDisposition.Remove
                : PartyBlockReleaseDisposition.AlreadyMissing;
            return item;
        }

        private bool TryLoadForMutation(
            out List<PartyBlockBundle> bundles,
            out List<PartyBlockAddressLease> addresses)
        {
            if (HasUnresolvedRecovery())
            {
                bundles = null;
                addresses = null;
                return false;
            }
            StoreFileLoadState primary = LoadFile(_storePath, out bundles, out addresses);
            if (primary == StoreFileLoadState.Valid) return true;
            StoreFileLoadState backup = LoadFile(_backupPath, out bundles, out addresses);
            if (backup == StoreFileLoadState.Valid) return true;
            if (primary == StoreFileLoadState.Missing && backup == StoreFileLoadState.Missing)
            {
                bundles = new List<PartyBlockBundle>();
                addresses = new List<PartyBlockAddressLease>();
                return true;
            }
            bundles = null;
            addresses = null;
            return false;
        }

        private bool HasUnresolvedRecovery()
        {
            try
            {
                if (!Directory.Exists(_storeDirectory)) return false;
                // A retained previous backup means a two-file commit or its
                // rollback did not finish. Never interpret the possibly newer
                // backup as committed ownership, and never delete evidence here.
                return Directory.EnumerateFiles(_storeDirectory,
                    Path.GetFileName(_backupPath) + ".previous.*", SearchOption.TopDirectoryOnly).Any();
            }
            catch
            {
                // Failure to establish whether recovery is pending is itself
                // insufficient evidence to remove somebody's existing block.
                return true;
            }
        }

        private StoreFileLoadState LoadFile(
            string path,
            out List<PartyBlockBundle> bundles,
            out List<PartyBlockAddressLease> addresses)
        {
            bundles = null;
            addresses = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    return Directory.Exists(path)
                        ? StoreFileLoadState.Invalid
                        : StoreFileLoadState.Missing;
                if (info.Length < 2 || info.Length > MaximumFileBytes)
                    return StoreFileLoadState.Invalid;

                byte[] bytes;
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                {
                    if (stream.Length < 2 || stream.Length > MaximumFileBytes)
                        return StoreFileLoadState.Invalid;
                    bytes = new byte[(int)stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) return StoreFileLoadState.Invalid;
                        offset += read;
                    }
                    if (stream.ReadByte() >= 0) return StoreFileLoadState.Invalid;
                }

                string json = new UTF8Encoding(false, true).GetString(bytes);
                if (json.Length > 0 && json[0] == '\ufeff') json = json.Substring(1);
                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumFileBytes };
                StoreDocument document = serializer.Deserialize<StoreDocument>(json);
                if (document == null
                    || document.Version != CurrentVersion
                    || document.Bundles == null
                    || document.Addresses == null
                    || document.Bundles.Count > MaximumBundleCount
                    || document.Addresses.Count > MaximumTrackedAddressCount)
                    return StoreFileLoadState.Invalid;

                var parsedBundles = new List<PartyBlockBundle>();
                var bundleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int totalMembers = 0;
                foreach (BundleItem item in document.Bundles)
                {
                    Guid id;
                    DateTime createdAt;
                    PartyBlockBundleLifecycleState bundleState;
                    string source = NormalizeSingleLine(
                        item == null ? null : item.Source,
                        MaximumSourceNameLength);
                    string reason = NormalizeReasonCode(item == null ? null : item.Reason);
                    if (item == null
                        || !Guid.TryParseExact(item.Id, "N", out id)
                        || id == Guid.Empty
                        || !bundleIds.Add(item.Id)
                        || source == null
                        || !string.Equals(source, item.Source, StringComparison.Ordinal)
                        || reason == null
                        || !string.Equals(reason, item.Reason, StringComparison.Ordinal)
                        || !TryParseUtc(item.CreatedAtUtc, out createdAt)
                        || !Enum.TryParse(item.State, false, out bundleState)
                        || !Enum.IsDefined(typeof(PartyBlockBundleLifecycleState), bundleState)
                        || item.Members == null
                        || item.Members.Count == 0
                        || item.Members.Count > PartyBlockInputParser.MaximumAddressCount)
                        return StoreFileLoadState.Invalid;

                    var bundle = new PartyBlockBundle
                    {
                        BundleId = item.Id,
                        SourceName = source,
                        ReasonCode = reason,
                        CreatedAtUtc = createdAt,
                        State = bundleState
                    };
                    var memberIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (MemberItem memberItem in item.Members)
                    {
                        PartyBlockMemberState memberState;
                        string lastError = NormalizeSingleLine(
                            memberItem == null ? null : memberItem.LastError,
                            MaximumStoredErrorLength);
                        if (memberItem == null
                            || !FirewallRuleManager.IsPublicIpv4(memberItem.Ip)
                            || !memberIps.Add(memberItem.Ip)
                            || !Enum.TryParse(memberItem.State, false, out memberState)
                            || !Enum.IsDefined(typeof(PartyBlockMemberState), memberState)
                            || (memberItem.LastError != null
                                && !string.Equals(lastError, memberItem.LastError, StringComparison.Ordinal)))
                            return StoreFileLoadState.Invalid;
                        bundle.Members.Add(new PartyBlockBundleMember
                        {
                            IpAddress = memberItem.Ip,
                            State = memberState,
                            LastError = lastError
                        });
                    }
                    totalMembers += bundle.Members.Count;
                    if (totalMembers > MaximumTrackedAddressCount * 2)
                        return StoreFileLoadState.Invalid;
                    parsedBundles.Add(bundle);
                }

                var parsedAddresses = new List<PartyBlockAddressLease>();
                var addressIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (AddressItem item in document.Addresses)
                {
                    if (item == null
                        || !FirewallRuleManager.IsPublicIpv4(item.Ip)
                        || !addressIps.Add(item.Ip)
                        || !(item.BaselineBlocked is bool)
                        || !(item.Preserve is bool)
                        || item.BundleIds == null
                        || item.BundleIds.Count == 0
                        || item.BundleIds.Count > MaximumBundleCount)
                        return StoreFileLoadState.Invalid;
                    var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string id in item.BundleIds)
                    {
                        if (!bundleIds.Contains(id) || !refs.Add(id))
                            return StoreFileLoadState.Invalid;
                        PartyBlockBundle bundle = FindBundle(parsedBundles, id);
                        if (bundle == null || FindMember(bundle, item.Ip) == null)
                            return StoreFileLoadState.Invalid;
                    }
                    parsedAddresses.Add(new PartyBlockAddressLease
                    {
                        IpAddress = item.Ip,
                        WasBlockedBeforeFirstBundle = (bool)item.BaselineBlocked,
                        PreserveAfterBundles = (bool)item.Preserve,
                        BundleIds = refs.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
                    });
                }
                foreach (PartyBlockBundle bundle in parsedBundles)
                {
                    foreach (PartyBlockBundleMember member in bundle.Members)
                    {
                        PartyBlockAddressLease lease = FindLease(parsedAddresses, member.IpAddress);
                        if (lease == null
                            || !lease.BundleIds.Contains(bundle.BundleId, StringComparer.OrdinalIgnoreCase))
                            return StoreFileLoadState.Invalid;
                    }
                }

                bundles = parsedBundles;
                addresses = parsedAddresses;
                return StoreFileLoadState.Valid;
            }
            catch
            {
                return StoreFileLoadState.Invalid;
            }
        }

        private bool SaveCore(
            IList<PartyBlockBundle> bundles,
            IList<PartyBlockAddressLease> addresses)
        {
            try
            {
                return SaveCoreUnsafe(bundles, addresses);
            }
            catch
            {
                return false;
            }
        }

        private bool SaveCoreUnsafe(
            IList<PartyBlockBundle> bundles,
            IList<PartyBlockAddressLease> addresses)
        {
            if (bundles == null
                || addresses == null
                || bundles.Count > MaximumBundleCount
                || addresses.Count > MaximumTrackedAddressCount)
                return false;
            if (!Directory.Exists(_storeDirectory)) Directory.CreateDirectory(_storeDirectory);

            var document = new StoreDocument
            {
                Version = CurrentVersion,
                Bundles = bundles
                    .OrderBy(item => item.CreatedAtUtc)
                    .ThenBy(item => item.BundleId, StringComparer.OrdinalIgnoreCase)
                    .Select(bundle => new BundleItem
                    {
                        Id = bundle.BundleId,
                        Source = NormalizeSingleLine(bundle.SourceName, MaximumSourceNameLength),
                        Reason = NormalizeReasonCode(bundle.ReasonCode),
                        CreatedAtUtc = FormatUtc(bundle.CreatedAtUtc),
                        State = bundle.State.ToString(),
                        Members = bundle.Members
                            .OrderBy(member => member.IpAddress, StringComparer.OrdinalIgnoreCase)
                            .Select(member => new MemberItem
                            {
                                Ip = member.IpAddress,
                                State = member.State.ToString(),
                                LastError = NormalizeSingleLine(
                                    member.LastError,
                                    MaximumStoredErrorLength)
                            })
                            .ToList()
                    })
                    .ToList(),
                Addresses = addresses
                    .OrderBy(item => item.IpAddress, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new AddressItem
                    {
                        Ip = item.IpAddress,
                        BaselineBlocked = item.WasBlockedBeforeFirstBundle,
                        Preserve = item.PreserveAfterBundles,
                        BundleIds = item.BundleIds
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    })
                    .ToList()
            };
            var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumFileBytes };
            string json = serializer.Serialize(document);
            if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes) return false;

            string temporaryPath = _storePath + ".tmp." + Guid.NewGuid().ToString("N");
            string backupTemporaryPath = _backupPath + ".tmp." + Guid.NewGuid().ToString("N");
            string replacedPath = _storePath + ".previous." + Guid.NewGuid().ToString("N");
            string replacedBackupPath = _backupPath + ".previous." + Guid.NewGuid().ToString("N");
            bool backupExisted = File.Exists(_backupPath);
            bool backupCommitted = false;
            bool primaryCommitted = false;
            bool backupRestored = false;
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                WriteNewFile(temporaryPath, bytes);
                WriteNewFile(backupTemporaryPath, bytes);
                // Commit the recovery copy before changing authoritative state.
                // A failed backup write must never silently activate a failed
                // promotion, release, or bundle creation on the next load.
                if (backupExisted)
                    File.Replace(backupTemporaryPath, _backupPath, replacedBackupPath, true);
                else
                    File.Move(backupTemporaryPath, _backupPath);
                backupCommitted = true;

                if (_beforePrimaryCommit != null) _beforePrimaryCommit();
                if (File.Exists(_storePath))
                    File.Replace(temporaryPath, _storePath, replacedPath, true);
                else
                    File.Move(temporaryPath, _storePath);
                primaryCommitted = true;
                return true;
            }
            catch
            {
                if (backupCommitted && !primaryCommitted)
                {
                    // The primary stayed unchanged. Restore its matching recovery
                    // copy as well, including when the primary was already damaged
                    // and this mutation started from the valid backup.
                    if (backupExisted)
                        File.Replace(replacedBackupPath, _backupPath, null, true);
                    else
                        File.Delete(_backupPath);
                    backupRestored = true;
                }
                throw;
            }
            finally
            {
                DeleteTemporaryFile(temporaryPath);
                DeleteTemporaryFile(backupTemporaryPath);
                DeleteTemporaryFile(replacedPath);
                // If rollback itself fails, retain the previous recovery bytes
                // rather than erasing the only copy of the old ownership state.
                if (primaryCommitted || backupRestored || !backupCommitted)
                    DeleteTemporaryFile(replacedBackupPath);
            }
        }

        private static void WriteNewFile(string path, byte[] bytes)
        {
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }

        private static void DeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static PartyBlockBundleSnapshot CreateSnapshot(
            bool success,
            string error,
            IEnumerable<PartyBlockBundle> bundles,
            IEnumerable<PartyBlockAddressLease> addresses)
        {
            return new PartyBlockBundleSnapshot
            {
                Success = success,
                ErrorMessage = error,
                Bundles = (bundles ?? Enumerable.Empty<PartyBlockBundle>())
                    .Select(CloneBundle)
                    .ToList(),
                AddressLeases = (addresses ?? Enumerable.Empty<PartyBlockAddressLease>())
                    .Select(CloneLease)
                    .ToList()
            };
        }

        private static PartyBlockBundle CloneBundle(PartyBlockBundle source)
        {
            return new PartyBlockBundle
            {
                BundleId = source.BundleId,
                SourceName = source.SourceName,
                ReasonCode = source.ReasonCode,
                CreatedAtUtc = source.CreatedAtUtc,
                State = source.State,
                Members = source.Members.Select(member => new PartyBlockBundleMember
                {
                    IpAddress = member.IpAddress,
                    State = member.State,
                    LastError = member.LastError
                }).ToList()
            };
        }

        private static PartyBlockAddressLease CloneLease(PartyBlockAddressLease source)
        {
            return new PartyBlockAddressLease
            {
                IpAddress = source.IpAddress,
                WasBlockedBeforeFirstBundle = source.WasBlockedBeforeFirstBundle,
                PreserveAfterBundles = source.PreserveAfterBundles,
                BundleIds = source.BundleIds.ToList()
            };
        }

        private static PartyBlockStoreResult BuildBundleResult(
            PartyBlockBundle bundle,
            bool completed)
        {
            return new PartyBlockStoreResult
            {
                Success = true,
                BundleId = bundle == null ? null : bundle.BundleId,
                AllAddresses = bundle == null
                    ? new List<string>()
                    : bundle.Members.Select(item => item.IpAddress).ToList(),
                ActiveCount = bundle == null
                    ? 0
                    : bundle.Members.Count(item => item.State == PartyBlockMemberState.Active),
                PendingCount = bundle == null
                    ? 0
                    : bundle.Members.Count(item => item.State != PartyBlockMemberState.Active),
                BundleCompleted = completed
            };
        }

        private static PartyBlockStoreResult Failure(string error)
        {
            return new PartyBlockStoreResult { ErrorMessage = error };
        }

        private static PartyBlockBundle FindBundle(
            IEnumerable<PartyBlockBundle> bundles,
            string bundleId)
        {
            return (bundles ?? Enumerable.Empty<PartyBlockBundle>()).FirstOrDefault(
                item => string.Equals(
                    item.BundleId,
                    bundleId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static PartyBlockBundleMember FindMember(
            PartyBlockBundle bundle,
            string ipAddress)
        {
            return bundle == null
                ? null
                : bundle.Members.FirstOrDefault(item => string.Equals(
                    item.IpAddress,
                    ipAddress,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static PartyBlockAddressLease FindLease(
            IEnumerable<PartyBlockAddressLease> leases,
            string ipAddress)
        {
            return (leases ?? Enumerable.Empty<PartyBlockAddressLease>()).FirstOrDefault(
                item => string.Equals(
                    item.IpAddress,
                    ipAddress,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void MarkAddressMembersPendingApply(
            IEnumerable<PartyBlockBundle> bundles,
            string ipAddress)
        {
            foreach (PartyBlockBundle bundle in bundles)
            {
                PartyBlockBundleMember member = FindMember(bundle, ipAddress);
                if (member == null || member.State == PartyBlockMemberState.PendingRelease) continue;
                member.State = PartyBlockMemberState.PendingApply;
                member.LastError = null;
            }
        }

        private static void MarkAddressMembersActive(
            IEnumerable<PartyBlockBundle> bundles,
            string ipAddress)
        {
            foreach (PartyBlockBundle bundle in bundles)
            {
                PartyBlockBundleMember member = FindMember(bundle, ipAddress);
                if (member == null || member.State == PartyBlockMemberState.PendingRelease) continue;
                member.State = PartyBlockMemberState.Active;
                member.LastError = null;
            }
        }

        private static void DetachMember(
            PartyBlockBundle bundle,
            IList<PartyBlockAddressLease> leases,
            string ipAddress)
        {
            PartyBlockBundleMember member = FindMember(bundle, ipAddress);
            if (member != null) bundle.Members.Remove(member);
            PartyBlockAddressLease lease = FindLease(leases, ipAddress);
            if (lease == null) return;
            lease.BundleIds = lease.BundleIds
                .Where(id => !string.Equals(id, bundle.BundleId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (lease.BundleIds.Count == 0) leases.Remove(lease);
        }

        private static void DetachBundle(
            IList<PartyBlockBundle> bundles,
            IList<PartyBlockAddressLease> leases,
            PartyBlockBundle bundle)
        {
            foreach (PartyBlockBundleMember member in bundle.Members.ToList())
                DetachMember(bundle, leases, member.IpAddress);
            bundles.Remove(bundle);
            CleanupEmptyBundlesAndLeases(bundles, leases);
        }

        private static void CleanupEmptyBundlesAndLeases(
            IList<PartyBlockBundle> bundles,
            IList<PartyBlockAddressLease> leases)
        {
            foreach (PartyBlockBundle bundle in bundles.Where(
                item => item.Members.Count == 0).ToList())
                bundles.Remove(bundle);
            var validBundleIds = new HashSet<string>(
                bundles.Select(item => item.BundleId),
                StringComparer.OrdinalIgnoreCase);
            foreach (PartyBlockAddressLease lease in leases.ToList())
            {
                lease.BundleIds = lease.BundleIds.Where(validBundleIds.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (lease.BundleIds.Count == 0) leases.Remove(lease);
            }
        }

        private static bool UpdateBundleStates(IEnumerable<PartyBlockBundle> bundles)
        {
            bool changed = false;
            foreach (PartyBlockBundle bundle in bundles)
            {
                PartyBlockBundleLifecycleState next;
                bool hasApply = bundle.Members.Any(
                    item => item.State == PartyBlockMemberState.PendingApply);
                bool hasRelease = bundle.Members.Any(
                    item => item.State == PartyBlockMemberState.PendingRelease);
                if (hasApply && hasRelease)
                    next = PartyBlockBundleLifecycleState.NeedsReconcile;
                else if (hasApply)
                    next = PartyBlockBundleLifecycleState.NeedsReconcile;
                else if (hasRelease)
                    next = PartyBlockBundleLifecycleState.PendingRelease;
                else
                    next = PartyBlockBundleLifecycleState.Active;
                if (bundle.State == next) continue;
                bundle.State = next;
                changed = true;
            }
            return changed;
        }

        private static int CountOtherReferences(PartyBlockAddressLease lease, string bundleId)
        {
            return lease == null
                ? 0
                : lease.BundleIds.Count(id => !string.Equals(
                    id,
                    bundleId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static IDictionary<string, string> CreateBatchErrorMap(
            FirewallBatchChangeResult batchResult)
        {
            if (batchResult == null || batchResult.Items == null)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return batchResult.Items
                .Where(item => item != null && !item.Success)
                .GroupBy(item => item.IpAddress, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.ErrorMessage)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static string GetStateError(
            string ipAddress,
            IDictionary<string, FirewallQueryResult> states,
            IDictionary<string, string> batchErrors)
        {
            string batchError;
            if (batchErrors != null
                && batchErrors.TryGetValue(ipAddress, out batchError)
                && !string.IsNullOrWhiteSpace(batchError))
                return NormalizeSingleLine(batchError, MaximumStoredErrorLength);
            FirewallQueryResult state;
            if (states != null
                && states.TryGetValue(ipAddress, out state)
                && state != null
                && !string.IsNullOrWhiteSpace(state.ErrorMessage))
                return NormalizeSingleLine(state.ErrorMessage, MaximumStoredErrorLength);
            return "Final firewall state was not verified.";
        }

        private static IList<string> NormalizeAddresses(IEnumerable<string> values)
        {
            var result = new List<string>();
            if (values == null) return result;
            foreach (string value in values)
            {
                if (!FirewallRuleManager.IsPublicIpv4(value)
                    || result.Contains(value, StringComparer.OrdinalIgnoreCase))
                    continue;
                result.Add(value);
            }
            return result;
        }

        private static string NormalizeReasonCode(string value)
        {
            string normalized = NormalizeSingleLine(value, MaximumReasonCodeLength);
            if (normalized == null || !Regex.IsMatch(normalized, @"^[a-z0-9-]+$")) return null;
            return normalized;
        }

        private static string NormalizeSingleLine(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var builder = new StringBuilder(value.Length);
            bool previousSpace = false;
            foreach (char character in value.Trim())
            {
                if (char.IsControl(character)
                    || character == '\u2028'
                    || character == '\u2029')
                {
                    if (!previousSpace && builder.Length > 0) builder.Append(' ');
                    previousSpace = true;
                    continue;
                }
                if (char.IsWhiteSpace(character))
                {
                    if (!previousSpace && builder.Length > 0) builder.Append(' ');
                    previousSpace = true;
                    continue;
                }
                builder.Append(character);
                previousSpace = false;
                if (builder.Length > maximumLength) return null;
            }
            string normalized = builder.ToString().Trim();
            return normalized.Length == 0 || normalized.Length > maximumLength
                ? null
                : normalized;
        }

        private static string FormatUtc(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc
                ? value
                : value.ToUniversalTime();
            return utc.ToString("o", CultureInfo.InvariantCulture);
        }

        private static bool TryParseUtc(string value, out DateTime parsed)
        {
            return DateTime.TryParseExact(
                value,
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed)
                && parsed.Kind == DateTimeKind.Utc;
        }
    }
}
