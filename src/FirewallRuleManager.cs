// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TarkovServerReporter
{
    public sealed class FirewallQueryResult
    {
        public bool Success { get; set; }
        public bool IsBlocked { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class FirewallChangeResult
    {
        public bool Success { get; set; }
        public bool IsBlocked { get; set; }
        public bool Cancelled { get; set; }
        public string ErrorMessage { get; set; }
    }

    public enum ManagedFirewallRuleKind
    {
        Current,
        Legacy,
        CurrentAndLegacy
    }

    public sealed class ManagedBlockedServer
    {
        public string IpAddress { get; set; }
        public ManagedFirewallRuleKind RuleKind { get; set; }

        public string StatusText
        {
            get { return "차단 중"; }
        }

        public string RuleKindText
        {
            get
            {
                if (RuleKind == ManagedFirewallRuleKind.Legacy) return "v1.1.1 규칙";
                if (RuleKind == ManagedFirewallRuleKind.CurrentAndLegacy) return "현재 + v1.1.1";
                return "현재 규칙";
            }
        }
    }

    public sealed class ManagedBlockedServerQueryResult
    {
        public ManagedBlockedServerQueryResult()
        {
            Servers = new List<ManagedBlockedServer>();
        }

        public bool Success { get; set; }
        public IList<ManagedBlockedServer> Servers { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class FirewallBatchItemResult
    {
        public string IpAddress { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class FirewallBatchChangeResult
    {
        public FirewallBatchChangeResult()
        {
            Items = new List<FirewallBatchItemResult>();
        }

        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string ErrorMessage { get; set; }
        public IList<FirewallBatchItemResult> Items { get; set; }
    }

    internal interface IInitialFirewallStateQueryGateway
    {
        Dictionary<string, FirewallQueryResult> QueryMany(IEnumerable<string> ipAddresses);
    }

    internal sealed class SystemInitialFirewallStateQueryGateway
        : IInitialFirewallStateQueryGateway
    {
        public Dictionary<string, FirewallQueryResult> QueryMany(
            IEnumerable<string> ipAddresses)
        {
            return FirewallRuleManager.QueryMany(ipAddresses);
        }
    }

    internal sealed class InitialFirewallStateRefreshResult
    {
        public InitialFirewallStateRefreshResult()
        {
            TargetIpAddresses = new List<string>();
            States = new Dictionary<string, FirewallQueryResult>(
                StringComparer.OrdinalIgnoreCase);
        }

        public int Generation { get; set; }
        public bool Succeeded { get; set; }
        public IList<string> TargetIpAddresses { get; set; }
        public IDictionary<string, FirewallQueryResult> States { get; set; }
    }

    /// <summary>
    /// Runs the inexpensive, read-only firewall lookup used immediately after raid logs load.
    /// It is deliberately separate from the user-triggered ping/location query so it never
    /// controls that button's busy state and can be replaced safely by a newer session load.
    /// </summary>
    internal sealed class InitialFirewallStateRefreshCoordinator : IDisposable
    {
        private readonly object _sync = new object();
        private readonly IInitialFirewallStateQueryGateway _gateway;
        private CancellationTokenSource _currentCancellation;
        private int _generation;
        private bool _disposed;

        public InitialFirewallStateRefreshCoordinator(
            IInitialFirewallStateQueryGateway gateway)
        {
            if (gateway == null) throw new ArgumentNullException("gateway");
            _gateway = gateway;
        }

        public Task<InitialFirewallStateRefreshResult> RefreshAsync(
            IEnumerable<string> candidateIpAddresses)
        {
            IList<string> targets = InitialFirewallStateRefreshPolicy
                .GetUniqueValidAddresses(candidateIpAddresses);
            CancellationTokenSource cancellation;
            int generation;
            lock (_sync)
            {
                ThrowIfDisposed();
                CancelWithoutDisposing(_currentCancellation);
                cancellation = new CancellationTokenSource();
                _currentCancellation = cancellation;
                generation = ++_generation;
            }
            return QueryAsync(generation, targets, cancellation);
        }

        public void Invalidate()
        {
            lock (_sync)
            {
                if (_disposed) return;
                ++_generation;
                CancellationTokenSource cancellation = _currentCancellation;
                _currentCancellation = null;
                CancelWithoutDisposing(cancellation);
            }
        }

        public bool IsCurrent(InitialFirewallStateRefreshResult result)
        {
            if (result == null) return false;
            lock (_sync)
            {
                return !_disposed && result.Generation == _generation;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                ++_generation;
                CancellationTokenSource cancellation = _currentCancellation;
                _currentCancellation = null;
                CancelWithoutDisposing(cancellation);
            }
        }

        private async Task<InitialFirewallStateRefreshResult> QueryAsync(
            int generation,
            IList<string> targets,
            CancellationTokenSource cancellation)
        {
            var result = new InitialFirewallStateRefreshResult
            {
                Generation = generation,
                TargetIpAddresses = targets
            };
            try
            {
                if (targets.Count == 0)
                {
                    result.Succeeded = true;
                    return result;
                }

                Dictionary<string, FirewallQueryResult> states = await Task.Run(
                    () => _gateway.QueryMany(targets),
                    cancellation.Token).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                result.States = states
                    ?? new Dictionary<string, FirewallQueryResult>(
                        StringComparer.OrdinalIgnoreCase);
                result.Succeeded = true;
                return result;
            }
            catch (OperationCanceledException)
            {
                return result;
            }
            catch
            {
                // Initial state decoration must never prevent normal log loading or the
                // explicit query workflow. Unknown rows keep their existing fallback text.
                return result;
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_currentCancellation, cancellation))
                        _currentCancellation = null;
                }
                cancellation.Dispose();
            }
        }

        private static void CancelWithoutDisposing(
            CancellationTokenSource cancellation)
        {
            if (cancellation == null) return;
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(
                "InitialFirewallStateRefreshCoordinator");
        }
    }

    internal static class InitialFirewallStateRefreshPolicy
    {
        public static IList<string> GetUniqueValidAddresses(
            IEnumerable<string> candidateIpAddresses)
        {
            var result = new List<string>();
            if (candidateIpAddresses == null) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in candidateIpAddresses)
            {
                string normalized = string.IsNullOrWhiteSpace(candidate)
                    ? null
                    : candidate.Trim();
                if (!FirewallRuleManager.IsValidIpv4(normalized)
                    || !seen.Add(normalized))
                    continue;
                result.Add(normalized);
            }
            return result;
        }

        public static IDictionary<string, FirewallQueryResult>
            GetApplicableSuccessfulStates(
                InitialFirewallStateRefreshResult queryResult,
                IEnumerable<string> currentCandidateIpAddresses)
        {
            var applicable = new Dictionary<string, FirewallQueryResult>(
                StringComparer.OrdinalIgnoreCase);
            if (queryResult == null || !queryResult.Succeeded
                || queryResult.States == null)
                return applicable;

            var requested = new HashSet<string>(
                GetUniqueValidAddresses(queryResult.TargetIpAddresses),
                StringComparer.OrdinalIgnoreCase);
            var current = new HashSet<string>(
                GetUniqueValidAddresses(currentCandidateIpAddresses),
                StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, FirewallQueryResult> pair in queryResult.States)
            {
                FirewallQueryResult state = pair.Value;
                if (!requested.Contains(pair.Key)
                    || !current.Contains(pair.Key)
                    || state == null
                    || !state.Success)
                    continue;
                applicable[pair.Key] = state;
            }
            return applicable;
        }
    }

    public static class FirewallRuleManager
    {
        internal enum ElevatedHelperWaitOutcome
        {
            Completed,
            TimedOut
        }

        private sealed class ManagedRuleInspection
        {
            public bool QuerySucceeded { get; set; }
            public bool HasOwnedRule { get; set; }
            public bool HasNameCollision { get; set; }
            public string ErrorMessage { get; set; }
        }

        internal const string RuleNamePrefix = "TarkovServerGuard_Block_";
        internal const string LegacyRuleNamePrefix = "EFT_ExcludeChinaHighPingServer_";
        private const string BatchRemovePrefix = "batch:";
        private const string BatchAddPrefix = "batch-add:";
        private const int MaximumBatchAddressCount = 1024;
        internal const int ElevatedHelperTimeoutMilliseconds = 120 * 1000;

        private const int NetFwActionBlock = 0;
        private const int NetFwRuleDirectionOutbound = 2;
        private const int NetFwIpProtocolAny = 256;
        private const int NetFwProfileAll = Int32.MaxValue;

        public static bool TryParseHelperCommand(
            string[] args,
            out bool shouldBlock,
            out string ipAddress)
        {
            shouldBlock = false;
            ipAddress = null;
            if (args == null || args.Length != 2) return false;

            if (string.Equals(args[0], "--firewall-add", StringComparison.OrdinalIgnoreCase))
                shouldBlock = true;
            else if (!string.Equals(args[0], "--firewall-remove", StringComparison.OrdinalIgnoreCase))
                return false;

            ipAddress = args[1];
            return true;
        }

        public static int ExecuteHelperCommand(bool shouldBlock, string ipAddress)
        {
            IList<string> batchAddresses;
            if (shouldBlock && TryParseBatchAddToken(ipAddress, out batchAddresses))
                return ExecuteBatchAddHelper(batchAddresses);
            if (!shouldBlock && TryParseBatchRemoveToken(ipAddress, out batchAddresses))
                return ExecuteBatchRemoveHelper(batchAddresses);

            if (!IsValidIpv4(ipAddress)) return 2;

            dynamic policy = null;
            try
            {
                policy = OpenPolicy();
                if (shouldBlock)
                    AddOrReplaceManagedRule(policy, ipAddress);
                else
                    RemoveManagedRules(policy, ipAddress);

                FirewallQueryResult verified = QueryWithPolicy(
                    policy,
                    ipAddress,
                    shouldBlock);
                return verified.Success && verified.IsBlocked == shouldBlock ? 0 : 3;
            }
            catch
            {
                return 1;
            }
            finally
            {
                ReleaseComObject(policy);
            }
        }

        public static ManagedBlockedServerQueryResult QueryManagedBlockedServers()
        {
            var result = new ManagedBlockedServerQueryResult();
            dynamic policy = null;
            try
            {
                policy = OpenPolicy();
                var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var legacy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (dynamic existingRule in policy.Rules)
                {
                    string name = existingRule.Name as string;
                    string ipAddress;
                    if (!TryGetManagedAddress(name, out ipAddress)) continue;
                    if (!IsOwnedBlockingRule(existingRule, name, ipAddress, true)) continue;

                    if (string.Equals(name, GetLegacyRuleName(ipAddress), StringComparison.OrdinalIgnoreCase))
                        legacy.Add(ipAddress);
                    else
                        current.Add(ipAddress);
                }

                foreach (string ipAddress in current.Concat(legacy)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(ParseIpv4SortKey))
                {
                    bool hasCurrent = current.Contains(ipAddress);
                    bool hasLegacy = legacy.Contains(ipAddress);
                    result.Servers.Add(new ManagedBlockedServer
                    {
                        IpAddress = ipAddress,
                        RuleKind = hasCurrent && hasLegacy
                            ? ManagedFirewallRuleKind.CurrentAndLegacy
                            : (hasLegacy ? ManagedFirewallRuleKind.Legacy : ManagedFirewallRuleKind.Current)
                    });
                }
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "서버차단현황 확인 실패: " + ex.Message;
            }
            finally
            {
                ReleaseComObject(policy);
            }
            return result;
        }

        public static Task<FirewallBatchChangeResult> RemoveManyWithElevationAsync(
            IEnumerable<string> ipAddresses)
        {
            return Task.Run(() => RemoveManyWithElevation(ipAddresses));
        }

        public static Task<FirewallBatchChangeResult> AddManyWithElevationAsync(
            IEnumerable<string> ipAddresses)
        {
            return Task.Run(() => AddManyWithElevation(ipAddresses));
        }

        public static Dictionary<string, FirewallQueryResult> QueryMany(IEnumerable<string> ipAddresses)
        {
            var results = new Dictionary<string, FirewallQueryResult>(StringComparer.OrdinalIgnoreCase);
            if (ipAddresses == null) return results;

            foreach (string value in ipAddresses)
            {
                string ipAddress = value == null ? null : value.Trim();
                if (string.IsNullOrWhiteSpace(ipAddress) || results.ContainsKey(ipAddress)) continue;
                results[ipAddress] = IsValidIpv4(ipAddress)
                    ? new FirewallQueryResult()
                    : new FirewallQueryResult { ErrorMessage = "유효하지 않은 IPv4 주소" };
            }

            dynamic policy = null;
            try
            {
                policy = OpenPolicy();
                foreach (KeyValuePair<string, FirewallQueryResult> pair in results)
                {
                    if (IsValidIpv4(pair.Key)) pair.Value.Success = true;
                }

                foreach (dynamic existingRule in policy.Rules)
                {
                    string name = existingRule.Name as string;
                    string managedAddress;
                    if (!TryGetManagedAddress(name, out managedAddress)) continue;

                    FirewallQueryResult state;
                    if (!results.TryGetValue(managedAddress, out state)) continue;
                    bool enabled = Convert.ToBoolean(existingRule.Enabled);
                    int direction = Convert.ToInt32(existingRule.Direction);
                    int action = Convert.ToInt32(existingRule.Action);
                    int protocol = Convert.ToInt32(existingRule.Protocol);
                    string remoteAddresses = existingRule.RemoteAddresses as string;
                    if (enabled
                        && direction == NetFwRuleDirectionOutbound
                        && action == NetFwActionBlock
                        && protocol == NetFwIpProtocolAny
                        && TargetsOnlyAddress(remoteAddresses, managedAddress))
                    {
                        state.IsBlocked = true;
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (KeyValuePair<string, FirewallQueryResult> pair in results)
                {
                    pair.Value.Success = false;
                    pair.Value.IsBlocked = false;
                    pair.Value.ErrorMessage = "방화벽 확인 실패: " + ex.Message;
                }
            }
            finally
            {
                ReleaseComObject(policy);
            }

            return results;
        }

        public static FirewallQueryResult Query(string ipAddress)
        {
            Dictionary<string, FirewallQueryResult> results = QueryMany(new[] { ipAddress });
            FirewallQueryResult result;
            return ipAddress != null && results.TryGetValue(ipAddress.Trim(), out result)
                ? result
                : new FirewallQueryResult { ErrorMessage = "유효하지 않은 IPv4 주소" };
        }

        public static Task<FirewallChangeResult> ChangeWithElevationAsync(string ipAddress, bool shouldBlock)
        {
            return Task.Run(() =>
            {
                var result = new FirewallChangeResult();
                if (!IsValidIpv4(ipAddress))
                {
                    result.ErrorMessage = "유효하지 않은 IPv4 주소입니다.";
                    return result;
                }

                string executablePath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    result.ErrorMessage = "실행 파일 경로를 확인할 수 없습니다.";
                    return result;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = (shouldBlock ? "--firewall-add " : "--firewall-remove ") + ipAddress,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                int exitCode = -1;
                bool timedOut = false;
                try
                {
                    using (Process process = Process.Start(startInfo))
                    {
                        if (process == null)
                        {
                            result.ErrorMessage = "관리자 권한 작업을 시작하지 못했습니다.";
                            return result;
                        }

                        ElevatedHelperWaitOutcome waitOutcome = WaitForElevatedHelperExit(
                            process.WaitForExit);
                        timedOut = waitOutcome == ElevatedHelperWaitOutcome.TimedOut;
                        if (!timedOut) exitCode = process.ExitCode;
                    }
                }
                catch (Win32Exception ex)
                {
                    if (ex.NativeErrorCode == 1223)
                    {
                        result.Cancelled = true;
                        result.ErrorMessage = "관리자 권한 요청이 취소되었습니다.";
                    }
                    else
                    {
                        result.ErrorMessage = "관리자 권한 작업 실패: " + ex.Message;
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = "방화벽 작업 실패: " + ex.Message;
                    return result;
                }

                if (timedOut)
                {
                    FirewallQueryResult timeoutState = Query(ipAddress);
                    result.IsBlocked = timeoutState.IsBlocked;
                    string stateSummary = timeoutState.Success
                        ? (timeoutState.IsBlocked
                            ? "현재 조회에서는 차단 상태가 확인됩니다."
                            : "현재 조회에서는 차단되지 않은 상태입니다.")
                        : (timeoutState.ErrorMessage
                            ?? "현재 방화벽 상태도 확인하지 못했습니다.");
                    result.ErrorMessage = BuildElevatedHelperTimeoutMessage(stateSummary);
                    return result;
                }
                if (exitCode != 0)
                {
                    result.ErrorMessage = "방화벽 작업이 완료되지 않았습니다. (코드 " + exitCode + ")";
                    return result;
                }

                FirewallQueryResult verified = Query(ipAddress);
                result.IsBlocked = verified.IsBlocked;
                result.Success = verified.Success && verified.IsBlocked == shouldBlock;
                result.ErrorMessage = result.Success
                    ? null
                    : (verified.ErrorMessage ?? "방화벽 상태를 최종 확인하지 못했습니다.");
                return result;
            });
        }

        private static FirewallBatchChangeResult AddManyWithElevation(IEnumerable<string> ipAddresses)
        {
            var result = new FirewallBatchChangeResult();
            IList<string> addresses = NormalizeBatchAddAddresses(ipAddresses);
            if (addresses.Count == 0)
            {
                result.ErrorMessage = "차단할 공인 IPv4 서버를 선택해 주세요.";
                return result;
            }
            if (addresses.Count > MaximumBatchAddressCount)
            {
                result.ErrorMessage = "한 번에 차단할 수 있는 서버 수를 초과했습니다.";
                return result;
            }

            string executablePath = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                result.ErrorMessage = "실행 파일 경로를 확인할 수 없습니다.";
                return result;
            }

            string token = BatchAddPrefix + string.Join(",", addresses);
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--firewall-add " + token,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            int exitCode = -1;
            bool timedOut = false;
            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        result.ErrorMessage = "관리자 권한 작업을 시작하지 못했습니다.";
                        return result;
                    }
                    ElevatedHelperWaitOutcome waitOutcome = WaitForElevatedHelperExit(
                        process.WaitForExit);
                    timedOut = waitOutcome == ElevatedHelperWaitOutcome.TimedOut;
                    if (!timedOut) exitCode = process.ExitCode;
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                {
                    result.Cancelled = true;
                    result.ErrorMessage = "관리자 권한 요청이 취소되었습니다.";
                }
                else
                {
                    result.ErrorMessage = "관리자 권한 작업 실패: " + ex.Message;
                }
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "방화벽 작업 실패: " + ex.Message;
                return result;
            }

            Dictionary<string, FirewallQueryResult> states = QueryMany(addresses);
            foreach (string ipAddress in addresses)
            {
                FirewallQueryResult state;
                if (!states.TryGetValue(ipAddress, out state) || state == null || !state.Success)
                {
                    result.Items.Add(new FirewallBatchItemResult
                    {
                        IpAddress = ipAddress,
                        ErrorMessage = state == null || string.IsNullOrWhiteSpace(state.ErrorMessage)
                            ? "방화벽 상태를 최종 확인하지 못했습니다."
                            : state.ErrorMessage
                    });
                }
                else if (!state.IsBlocked)
                {
                    result.Items.Add(new FirewallBatchItemResult
                    {
                        IpAddress = ipAddress,
                        ErrorMessage = "앱 관리 차단 규칙이 확인되지 않았습니다."
                    });
                }
                else
                {
                    result.Items.Add(new FirewallBatchItemResult
                    {
                        IpAddress = ipAddress,
                        Success = true
                    });
                }
            }

            result.Success = !timedOut && result.Items.All(item => item.Success);
            if (timedOut)
            {
                int verifiedCount = result.Items.Count(item => item.Success);
                result.ErrorMessage = BuildElevatedHelperTimeoutMessage(string.Format(
                    "현재 상태 조회에서 {0}개 중 {1}개의 차단을 확인했습니다.",
                    result.Items.Count,
                    verifiedCount));
                return result;
            }
            if (!result.Success)
            {
                int succeeded = result.Items.Count(item => item.Success);
                result.ErrorMessage = exitCode == 0
                    ? "일부 규칙의 최종 상태를 확인하지 못했습니다."
                    : string.Format("{0}개 중 {1}개를 차단했습니다.", result.Items.Count, succeeded);
            }
            return result;
        }

        private static FirewallBatchChangeResult RemoveManyWithElevation(IEnumerable<string> ipAddresses)
        {
            var result = new FirewallBatchChangeResult();
            IList<string> addresses = NormalizeBatchAddresses(ipAddresses);
            if (addresses.Count == 0)
            {
                result.ErrorMessage = "해제할 서버를 선택해 주세요.";
                return result;
            }
            if (addresses.Count > MaximumBatchAddressCount)
            {
                result.ErrorMessage = "한 번에 해제할 수 있는 서버 수를 초과했습니다.";
                return result;
            }

            string executablePath = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                result.ErrorMessage = "실행 파일 경로를 확인할 수 없습니다.";
                return result;
            }

            string token = BatchRemovePrefix + string.Join(",", addresses);
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--firewall-remove " + token,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            int exitCode = -1;
            bool timedOut = false;
            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        result.ErrorMessage = "관리자 권한 작업을 시작하지 못했습니다.";
                        return result;
                    }
                    ElevatedHelperWaitOutcome waitOutcome = WaitForElevatedHelperExit(
                        process.WaitForExit);
                    timedOut = waitOutcome == ElevatedHelperWaitOutcome.TimedOut;
                    if (!timedOut) exitCode = process.ExitCode;
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                {
                    result.Cancelled = true;
                    result.ErrorMessage = "관리자 권한 요청이 취소되었습니다.";
                }
                else
                {
                    result.ErrorMessage = "관리자 권한 작업 실패: " + ex.Message;
                }
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "방화벽 작업 실패: " + ex.Message;
                return result;
            }

            Dictionary<string, ManagedRuleInspection> inspections = InspectManagedRules(addresses);
            foreach (string ipAddress in addresses)
            {
                ManagedRuleInspection inspection;
                if (!inspections.TryGetValue(ipAddress, out inspection) || !inspection.QuerySucceeded)
                {
                    result.Items.Add(new FirewallBatchItemResult
                    {
                        IpAddress = ipAddress,
                        ErrorMessage = inspection == null
                            ? "방화벽 상태를 최종 확인하지 못했습니다."
                            : inspection.ErrorMessage
                    });
                }
                else if (inspection.HasNameCollision)
                {
                    result.Items.Add(new FirewallBatchItemResult
                    {
                        IpAddress = ipAddress,
                        ErrorMessage = "같은 이름의 다른 방화벽 규칙이 있어 삭제하지 않았습니다."
                    });
                }
                else if (inspection.HasOwnedRule)
                {
                    result.Items.Add(new FirewallBatchItemResult
                    {
                        IpAddress = ipAddress,
                        ErrorMessage = "관리 규칙이 남아 있어 해제를 완료하지 못했습니다."
                    });
                }
                else
                {
                    result.Items.Add(new FirewallBatchItemResult
                    {
                        IpAddress = ipAddress,
                        Success = true
                    });
                }
            }

            result.Success = !timedOut && result.Items.All(item => item.Success);
            if (timedOut)
            {
                int verifiedCount = result.Items.Count(item => item.Success);
                result.ErrorMessage = BuildElevatedHelperTimeoutMessage(string.Format(
                    "현재 상태 조회에서 {0}개 중 {1}개의 해제를 확인했습니다.",
                    result.Items.Count,
                    verifiedCount));
                return result;
            }
            if (!result.Success)
            {
                int succeeded = result.Items.Count(item => item.Success);
                result.ErrorMessage = exitCode == 0
                    ? "일부 규칙의 최종 상태를 확인하지 못했습니다."
                    : string.Format("{0}개 중 {1}개를 해제했습니다.", result.Items.Count, succeeded);
            }
            return result;
        }

        internal static ElevatedHelperWaitOutcome WaitForElevatedHelperExit(
            Func<int, bool> waitForExit)
        {
            if (waitForExit == null) throw new ArgumentNullException("waitForExit");
            // A timeout is observational only. The caller releases its Process handle
            // but deliberately never kills a helper that may still be inside a privileged
            // Windows Firewall operation, then performs a separate read-only state query.
            return waitForExit(ElevatedHelperTimeoutMilliseconds)
                ? ElevatedHelperWaitOutcome.Completed
                : ElevatedHelperWaitOutcome.TimedOut;
        }

        internal static string BuildElevatedHelperTimeoutMessage(string stateSummary)
        {
            string message = string.Format(
                "관리자 권한 방화벽 작업이 {0}초 안에 끝나지 않았습니다. "
                    + "프로세스는 강제 종료하지 않았으며 아직 진행 중일 수 있습니다.",
                ElevatedHelperTimeoutMilliseconds / 1000);
            return string.IsNullOrWhiteSpace(stateSummary)
                ? message
                : message + " " + stateSummary;
        }

        private static int ExecuteBatchAddHelper(IList<string> ipAddresses)
        {
            if (ipAddresses == null || ipAddresses.Count == 0) return 2;

            bool allSucceeded = true;
            dynamic policy = null;
            try
            {
                policy = OpenPolicy();
                foreach (string ipAddress in ipAddresses)
                {
                    try
                    {
                        AddOrReplaceManagedRule(policy, ipAddress);
                        FirewallQueryResult verified = QueryWithPolicy(policy, ipAddress);
                        if (!verified.Success || !verified.IsBlocked) allSucceeded = false;
                    }
                    catch
                    {
                        // Keep processing independently validated addresses so one collision or
                        // transient COM error does not discard the rest of the requested restore.
                        allSucceeded = false;
                    }
                }
                return allSucceeded ? 0 : 4;
            }
            catch
            {
                return 1;
            }
            finally
            {
                ReleaseComObject(policy);
            }
        }

        private static int ExecuteBatchRemoveHelper(IList<string> ipAddresses)
        {
            if (ipAddresses == null || ipAddresses.Count == 0) return 2;

            bool allSucceeded = true;
            dynamic policy = null;
            try
            {
                policy = OpenPolicy();
                foreach (string ipAddress in ipAddresses)
                {
                    try
                    {
                        RemoveManagedRules(policy, ipAddress);
                        FirewallQueryResult verified = QueryWithPolicy(policy, ipAddress, false);
                        if (!verified.Success || verified.IsBlocked) allSucceeded = false;
                    }
                    catch
                    {
                        // Continue so one conflicting or transient rule does not prevent safe
                        // removal of the remaining independently validated addresses.
                        allSucceeded = false;
                    }
                }
                return allSucceeded ? 0 : 4;
            }
            catch
            {
                return 1;
            }
            finally
            {
                ReleaseComObject(policy);
            }
        }

        private static bool TryParseBatchRemoveToken(string value, out IList<string> addresses)
        {
            addresses = null;
            if (string.IsNullOrWhiteSpace(value)
                || !value.StartsWith(BatchRemovePrefix, StringComparison.Ordinal))
                return false;

            string payload = value.Substring(BatchRemovePrefix.Length);
            if (payload.Length == 0 || payload.Length > 20000) return false;

            string[] items = payload.Split(',');
            if (items.Length == 0 || items.Length > MaximumBatchAddressCount) return false;
            var parsed = new List<string>(items.Length);
            foreach (string item in items)
            {
                if (!IsValidIpv4(item) || parsed.Contains(item, StringComparer.OrdinalIgnoreCase))
                    return false;
                parsed.Add(item);
            }
            addresses = parsed;
            return true;
        }

        private static bool TryParseBatchAddToken(string value, out IList<string> addresses)
        {
            addresses = null;
            if (string.IsNullOrWhiteSpace(value)
                || !value.StartsWith(BatchAddPrefix, StringComparison.Ordinal))
                return false;

            string payload = value.Substring(BatchAddPrefix.Length);
            if (payload.Length == 0 || payload.Length > 20000) return false;

            string[] items = payload.Split(',');
            if (items.Length == 0 || items.Length > MaximumBatchAddressCount) return false;
            var parsed = new List<string>(items.Length);
            foreach (string item in items)
            {
                if (!IsPublicIpv4(item) || parsed.Contains(item, StringComparer.OrdinalIgnoreCase))
                    return false;
                parsed.Add(item);
            }
            addresses = parsed;
            return true;
        }

        private static IList<string> NormalizeBatchAddresses(IEnumerable<string> ipAddresses)
        {
            var addresses = new List<string>();
            if (ipAddresses == null) return addresses;
            foreach (string value in ipAddresses)
            {
                if (!IsValidIpv4(value) || addresses.Contains(value, StringComparer.OrdinalIgnoreCase))
                    continue;
                addresses.Add(value);
            }
            return addresses;
        }

        private static IList<string> NormalizeBatchAddAddresses(IEnumerable<string> ipAddresses)
        {
            var addresses = new List<string>();
            if (ipAddresses == null) return addresses;
            foreach (string value in ipAddresses)
            {
                if (!IsPublicIpv4(value) || addresses.Contains(value, StringComparer.OrdinalIgnoreCase))
                    continue;
                addresses.Add(value);
            }
            return addresses;
        }

        private static Dictionary<string, ManagedRuleInspection> InspectManagedRules(
            IEnumerable<string> ipAddresses)
        {
            var results = new Dictionary<string, ManagedRuleInspection>(StringComparer.OrdinalIgnoreCase);
            foreach (string ipAddress in ipAddresses)
                results[ipAddress] = new ManagedRuleInspection();

            dynamic policy = null;
            try
            {
                policy = OpenPolicy();
                foreach (ManagedRuleInspection inspection in results.Values)
                    inspection.QuerySucceeded = true;

                foreach (dynamic existingRule in policy.Rules)
                {
                    string name = existingRule.Name as string;
                    string ipAddress;
                    if (!TryGetManagedAddress(name, out ipAddress)) continue;

                    ManagedRuleInspection inspection;
                    if (!results.TryGetValue(ipAddress, out inspection)) continue;
                    if (IsOwnedBlockingRule(existingRule, name, ipAddress, false))
                        inspection.HasOwnedRule = true;
                    else
                        inspection.HasNameCollision = true;
                }
            }
            catch (Exception ex)
            {
                foreach (ManagedRuleInspection inspection in results.Values)
                {
                    inspection.QuerySucceeded = false;
                    inspection.ErrorMessage = "방화벽 확인 실패: " + ex.Message;
                }
            }
            finally
            {
                ReleaseComObject(policy);
            }
            return results;
        }

        private static uint ParseIpv4SortKey(string ipAddress)
        {
            byte[] bytes = IPAddress.Parse(ipAddress).GetAddressBytes();
            return ((uint)bytes[0] << 24)
                | ((uint)bytes[1] << 16)
                | ((uint)bytes[2] << 8)
                | bytes[3];
        }

        internal static string GetRuleName(string ipAddress)
        {
            return RuleNamePrefix + ipAddress;
        }

        internal static string GetLegacyRuleName(string ipAddress)
        {
            return LegacyRuleNamePrefix + ipAddress;
        }

        internal static bool IsManagedRuleName(string ruleName, string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ruleName) || !IsValidIpv4(ipAddress)) return false;
            return string.Equals(ruleName, GetRuleName(ipAddress), StringComparison.OrdinalIgnoreCase)
                || string.Equals(ruleName, GetLegacyRuleName(ipAddress), StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryGetManagedAddress(string ruleName, out string ipAddress)
        {
            ipAddress = null;
            if (string.IsNullOrWhiteSpace(ruleName)) return false;

            string candidate;
            if (ruleName.StartsWith(RuleNamePrefix, StringComparison.OrdinalIgnoreCase))
                candidate = ruleName.Substring(RuleNamePrefix.Length);
            else if (ruleName.StartsWith(LegacyRuleNamePrefix, StringComparison.OrdinalIgnoreCase))
                candidate = ruleName.Substring(LegacyRuleNamePrefix.Length);
            else
                return false;

            if (!IsValidIpv4(candidate)) return false;
            ipAddress = candidate;
            return true;
        }

        internal static bool IsValidIpv4(string value)
        {
            IPAddress parsed;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string trimmed = value.Trim();
            if (!string.Equals(value, trimmed, StringComparison.Ordinal)
                || !IPAddress.TryParse(trimmed, out parsed)
                || parsed.AddressFamily != AddressFamily.InterNetwork
                || !string.Equals(parsed.ToString(), trimmed, StringComparison.Ordinal))
                return false;

            byte[] bytes = parsed.GetAddressBytes();
            return bytes[0] != 0
                && bytes[0] != 127
                && bytes[0] < 224;
        }

        internal static bool IsPublicIpv4(string value)
        {
            if (!IsValidIpv4(value)) return false;
            byte[] bytes = IPAddress.Parse(value).GetAddressBytes();
            int first = bytes[0];
            int second = bytes[1];
            int third = bytes[2];
            int fourth = bytes[3];

            if (first == 10
                || (first == 100 && second >= 64 && second <= 127)
                || (first == 169 && second == 254)
                || (first == 172 && second >= 16 && second <= 31)
                || (first == 192 && second == 168)
                || (first == 198 && (second == 18 || second == 19)))
                return false;

            // IANA special-purpose, documentation, benchmarking, and deprecated relay ranges
            // are not globally routable server destinations. 192.0.0.9 and .10 are the two
            // globally reachable anycast exceptions inside 192.0.0.0/24.
            if (first == 192 && second == 0 && third == 0
                && !(fourth == 9 || fourth == 10))
                return false;
            if ((first == 192 && second == 0 && third == 2)
                || (first == 192 && second == 88 && third == 99)
                || (first == 198 && second == 51 && third == 100)
                || (first == 203 && second == 0 && third == 113))
                return false;
            return true;
        }

        private static dynamic OpenPolicy()
        {
            Type policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType == null)
                throw new InvalidOperationException("Windows 방화벽을 사용할 수 없습니다.");
            return Activator.CreateInstance(policyType);
        }

        private static FirewallQueryResult QueryWithPolicy(dynamic policy, string ipAddress)
        {
            return QueryWithPolicy(policy, ipAddress, true);
        }

        private static FirewallQueryResult QueryWithPolicy(
            dynamic policy,
            string ipAddress,
            bool requireEnabled)
        {
            var result = new FirewallQueryResult { Success = true };
            try
            {
                foreach (dynamic existingRule in policy.Rules)
                {
                    string name = existingRule.Name as string;
                    if (!IsManagedRuleName(name, ipAddress)) continue;
                    if (IsOwnedBlockingRule(
                        existingRule,
                        name,
                        ipAddress,
                        requireEnabled))
                    {
                        result.IsBlocked = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.IsBlocked = false;
                result.ErrorMessage = "방화벽 확인 실패: " + ex.Message;
            }
            return result;
        }

        private static bool IsOwnedBlockingRule(
            dynamic rule,
            string ruleName,
            string ipAddress,
            bool requireEnabled)
        {
            if (!IsManagedRuleName(ruleName, ipAddress)) return false;
            bool enabled = Convert.ToBoolean(rule.Enabled);
            int direction = Convert.ToInt32(rule.Direction);
            int action = Convert.ToInt32(rule.Action);
            int protocol = Convert.ToInt32(rule.Protocol);
            string remoteAddresses = rule.RemoteAddresses as string;
            return (!requireEnabled || enabled)
                && direction == NetFwRuleDirectionOutbound
                && action == NetFwActionBlock
                && protocol == NetFwIpProtocolAny
                && TargetsOnlyAddress(remoteAddresses, ipAddress);
        }

        private static void AddOrReplaceManagedRule(dynamic policy, string ipAddress)
        {
            RemoveRuleIfPresent(policy, GetRuleName(ipAddress), ipAddress);

            Type ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
            if (ruleType == null)
                throw new InvalidOperationException("Windows 방화벽 규칙 기능을 사용할 수 없습니다.");

            dynamic rule = null;
            try
            {
                rule = Activator.CreateInstance(ruleType);
                rule.Name = GetRuleName(ipAddress);
                rule.Description =
                    "Blocks outbound traffic to Escape from Tarkov server " + ipAddress
                    + ". Created by Tarkov Server Guard.";
                rule.Direction = NetFwRuleDirectionOutbound;
                rule.Action = NetFwActionBlock;
                rule.Protocol = NetFwIpProtocolAny;
                rule.RemoteAddresses = ipAddress;
                rule.Profiles = NetFwProfileAll;
                rule.Enabled = true;
                policy.Rules.Add(rule);
            }
            finally
            {
                ReleaseComObject(rule);
            }
        }

        private static void RemoveManagedRules(dynamic policy, string ipAddress)
        {
            ValidateRuleNameOwnership(policy, GetRuleName(ipAddress), ipAddress);
            ValidateRuleNameOwnership(policy, GetLegacyRuleName(ipAddress), ipAddress);
            RemoveRuleIfPresent(policy, GetRuleName(ipAddress), ipAddress);
            RemoveRuleIfPresent(policy, GetLegacyRuleName(ipAddress), ipAddress);
        }

        private static void RemoveRuleIfPresent(dynamic policy, string ruleName, string ipAddress)
        {
            ValidateRuleNameOwnership(policy, ruleName, ipAddress);
            bool exists = false;
            foreach (dynamic existingRule in policy.Rules)
            {
                string existingName = existingRule.Name as string;
                if (!string.Equals(existingName, ruleName, StringComparison.OrdinalIgnoreCase)) continue;

                exists = true;
                break;
            }

            if (exists) policy.Rules.Remove(ruleName);
        }

        private static void ValidateRuleNameOwnership(dynamic policy, string ruleName, string ipAddress)
        {
            foreach (dynamic existingRule in policy.Rules)
            {
                string existingName = existingRule.Name as string;
                if (!string.Equals(existingName, ruleName, StringComparison.OrdinalIgnoreCase)) continue;

                if (!IsOwnedBlockingRule(existingRule, existingName, ipAddress, false))
                {
                    throw new InvalidOperationException(
                        "같은 이름의 다른 방화벽 규칙이 있어 안전하게 변경할 수 없습니다: " + ruleName);
                }
            }
        }

        internal static bool TargetsOnlyAddress(string remoteAddresses, string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteAddresses) || !IsValidIpv4(ipAddress)) return false;
            string[] items = remoteAddresses.Split(',');
            if (items.Length != 1) return false;

            string candidate = items[0].Trim();
            if (string.Equals(candidate, ipAddress, StringComparison.OrdinalIgnoreCase)) return true;

            int slashIndex = candidate.IndexOf('/');
            if (slashIndex <= 0 || slashIndex == candidate.Length - 1) return false;

            string addressPart = candidate.Substring(0, slashIndex).Trim();
            string maskPart = candidate.Substring(slashIndex + 1).Trim();
            return string.Equals(addressPart, ipAddress, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(maskPart, "32", StringComparison.Ordinal)
                    || string.Equals(maskPart, "255.255.255.255", StringComparison.Ordinal));
        }

        private static void ReleaseComObject(object value)
        {
            try
            {
                if (value != null && Marshal.IsComObject(value))
                    Marshal.FinalReleaseComObject(value);
            }
            catch
            {
                // COM cleanup is best-effort only.
            }
        }
    }
}
