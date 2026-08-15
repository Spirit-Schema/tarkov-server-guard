using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal sealed class ApplicationUpdate
    {
        public string VersionText { get; private set; }
        public object NativeUpdate { get; private set; }

        public ApplicationUpdate(string versionText, object nativeUpdate)
        {
            VersionText = versionText ?? string.Empty;
            NativeUpdate = nativeUpdate;
        }
    }

    internal interface IApplicationUpdateEngine
    {
        bool IsAvailable { get; }
        Task<ApplicationUpdate> CheckForUpdateAsync(CancellationToken cancellationToken);
        Task DownloadAndApplyAsync(
            ApplicationUpdate update,
            Action<int> progress,
            CancellationToken cancellationToken);
    }

    internal interface IUpdateClock
    {
        DateTime UtcNow { get; }
    }

    internal interface IUpdateCheckStateStore
    {
        UpdateCheckState Load();
        void Save(UpdateCheckState state);
    }

    internal sealed class UpdateCheckState
    {
        public DateTime LastCheckUtc { get; set; }
        public string DeferredVersion { get; set; }
        public DateTime DeferredUntilUtc { get; set; }

        public UpdateCheckState Clone()
        {
            return new UpdateCheckState
            {
                LastCheckUtc = LastCheckUtc,
                DeferredVersion = DeferredVersion,
                DeferredUntilUtc = DeferredUntilUtc
            };
        }
    }

    internal sealed class SystemUpdateClock : IUpdateClock
    {
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
    }

    internal sealed class FileUpdateCheckStateStore : IUpdateCheckStateStore
    {
        private const long MaximumStateFileBytes = 64 * 1024;
        private readonly string _statePath;
        private readonly string _backupPath;
        private readonly object _sync = new object();

        public FileUpdateCheckStateStore(string storageRoot)
        {
            if (string.IsNullOrWhiteSpace(storageRoot))
                throw new ArgumentException("Update storage root is required.", "storageRoot");
            _statePath = Path.Combine(storageRoot, "update-check-state.json");
            _backupPath = _statePath + ".bak";
        }

        public UpdateCheckState Load()
        {
            lock (_sync)
            {
                UpdateCheckState state;
                if (TryLoad(_statePath, out state)) return state;
                if (TryLoad(_backupPath, out state)) return state;
                return new UpdateCheckState();
            }
        }

        public void Save(UpdateCheckState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            lock (_sync)
            {
                string directory = Path.GetDirectoryName(_statePath);
                Directory.CreateDirectory(directory);
                string temporaryPath = _statePath + ".tmp." + Guid.NewGuid().ToString("N");
                try
                {
                    string json = new JavaScriptSerializer().Serialize(state);
                    File.WriteAllText(temporaryPath, json);
                    if (File.Exists(_statePath))
                    {
                        try
                        {
                            File.Replace(temporaryPath, _statePath, _backupPath, true);
                        }
                        catch (PlatformNotSupportedException)
                        {
                            CopyFallback(temporaryPath);
                        }
                        catch (IOException)
                        {
                            CopyFallback(temporaryPath);
                        }
                    }
                    else
                    {
                        File.Move(temporaryPath, _statePath);
                    }
                }
                finally
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                    catch { }
                }
            }
        }

        private void CopyFallback(string temporaryPath)
        {
            if (File.Exists(_statePath)) File.Copy(_statePath, _backupPath, true);
            File.Copy(temporaryPath, _statePath, true);
            File.Delete(temporaryPath);
        }

        private static bool TryLoad(string path, out UpdateCheckState state)
        {
            state = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaximumStateFileBytes)
                    return false;
                state = new JavaScriptSerializer().Deserialize<UpdateCheckState>(File.ReadAllText(path));
                return state != null;
            }
            catch
            {
                state = null;
                return false;
            }
        }
    }

    internal sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        private readonly string[] _prereleaseParts;

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        public string Prerelease { get; private set; }
        public bool IsPrerelease { get { return !string.IsNullOrEmpty(Prerelease); } }

        private SemanticVersion(int major, int minor, int patch, string prerelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
            _prereleaseParts = string.IsNullOrEmpty(prerelease)
                ? new string[0]
                : prerelease.Split('.');
        }

        public static bool TryParse(string value, out SemanticVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string text = value.Trim();
            if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V')) text = text.Substring(1);

            int buildIndex = text.IndexOf('+');
            if (buildIndex >= 0)
            {
                if (!IsValidIdentifierList(text.Substring(buildIndex + 1), false)) return false;
                text = text.Substring(0, buildIndex);
            }

            string prerelease = null;
            int prereleaseIndex = text.IndexOf('-');
            if (prereleaseIndex >= 0)
            {
                prerelease = text.Substring(prereleaseIndex + 1);
                text = text.Substring(0, prereleaseIndex);
                if (!IsValidIdentifierList(prerelease, true)) return false;
            }

            string[] numeric = text.Split('.');
            if (numeric.Length != 3) return false;
            int major;
            int minor;
            int patch;
            if (!TryParseCoreNumber(numeric[0], out major)
                || !TryParseCoreNumber(numeric[1], out minor)
                || !TryParseCoreNumber(numeric[2], out patch)) return false;
            version = new SemanticVersion(major, minor, patch, prerelease);
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            if (ReferenceEquals(other, null)) return 1;
            int result = Major.CompareTo(other.Major);
            if (result != 0) return result;
            result = Minor.CompareTo(other.Minor);
            if (result != 0) return result;
            result = Patch.CompareTo(other.Patch);
            if (result != 0) return result;
            if (!IsPrerelease && !other.IsPrerelease) return 0;
            if (!IsPrerelease) return 1;
            if (!other.IsPrerelease) return -1;

            int count = Math.Max(_prereleaseParts.Length, other._prereleaseParts.Length);
            for (int index = 0; index < count; index++)
            {
                if (index >= _prereleaseParts.Length) return -1;
                if (index >= other._prereleaseParts.Length) return 1;
                result = ComparePrereleasePart(_prereleaseParts[index], other._prereleaseParts[index]);
                if (result != 0) return result;
            }
            return 0;
        }

        public override string ToString()
        {
            string core = Major + "." + Minor + "." + Patch;
            return IsPrerelease ? core + "-" + Prerelease : core;
        }

        private static int ComparePrereleasePart(string left, string right)
        {
            int leftNumber;
            int rightNumber;
            bool leftNumeric = int.TryParse(left, out leftNumber);
            bool rightNumeric = int.TryParse(right, out rightNumber);
            if (leftNumeric && rightNumeric) return leftNumber.CompareTo(rightNumber);
            if (leftNumeric) return -1;
            if (rightNumeric) return 1;
            return string.CompareOrdinal(left, right);
        }

        private static bool TryParseCoreNumber(string text, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(text) || (text.Length > 1 && text[0] == '0')) return false;
            for (int index = 0; index < text.Length; index++)
                if (text[index] < '0' || text[index] > '9') return false;
            return int.TryParse(text, out number) && number >= 0;
        }

        private static bool IsValidIdentifierList(string value, bool rejectLeadingZeroNumbers)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split('.');
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part)) return false;
                bool numeric = true;
                for (int index = 0; index < part.Length; index++)
                {
                    char character = part[index];
                    if (character < '0' || character > '9') numeric = false;
                    if (!((character >= '0' && character <= '9')
                        || (character >= 'A' && character <= 'Z')
                        || (character >= 'a' && character <= 'z')
                        || character == '-')) return false;
                }
                if (rejectLeadingZeroNumbers && numeric && part.Length > 1 && part[0] == '0') return false;
            }
            return true;
        }
    }

    internal sealed class GitHubUpdateService
    {
        internal const string RepositoryUrl = "https://github.com/Spirit-Schema/tarkov-server-guard";
        internal static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        internal static readonly TimeSpan DeferInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan FutureClockTolerance = TimeSpan.FromMinutes(5);

        private readonly SemanticVersion _currentVersion;
        private readonly IApplicationUpdateEngine _engine;
        private readonly IUpdateCheckStateStore _stateStore;
        private readonly IUpdateClock _clock;
        private int _checkInProgress;

        internal GitHubUpdateService(
            string currentVersion,
            IApplicationUpdateEngine engine,
            IUpdateCheckStateStore stateStore,
            IUpdateClock clock)
        {
            SemanticVersion parsedCurrentVersion;
            if (!SemanticVersion.TryParse(currentVersion, out parsedCurrentVersion))
                throw new ArgumentException("A three-part semantic version is required.", "currentVersion");
            if (engine == null) throw new ArgumentNullException("engine");
            if (stateStore == null) throw new ArgumentNullException("stateStore");
            if (clock == null) throw new ArgumentNullException("clock");
            _currentVersion = parsedCurrentVersion;
            _engine = engine;
            _stateStore = stateStore;
            _clock = clock;
        }

        internal static GitHubUpdateService CreateProduction(string currentVersion)
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard");
            return new GitHubUpdateService(
                currentVersion,
                VelopackReflectionUpdateEngine.CreateGitHub(RepositoryUrl),
                new FileUpdateCheckStateStore(root),
                new SystemUpdateClock());
        }

        internal static void TryRunVelopackStartupHooks()
        {
            VelopackReflectionUpdateEngine.TryRunStartupHooks();
        }

        internal async Task<ApplicationUpdate> CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            if (!_engine.IsAvailable) return null;
            if (Interlocked.CompareExchange(ref _checkInProgress, 1, 0) != 0) return null;
            try
            {
                DateTime nowUtc = EnsureUtc(_clock.UtcNow);
                UpdateCheckState state = SafeLoadState();
                if (!IsCheckDue(state, nowUtc)) return null;

                state.LastCheckUtc = nowUtc;
                SafeSaveState(state);

                ApplicationUpdate candidate;
                try
                {
                    candidate = await _engine.CheckForUpdateAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return null;
                }
                if (candidate == null) return null;

                SemanticVersion candidateVersion;
                if (!SemanticVersion.TryParse(candidate.VersionText, out candidateVersion)
                    || candidateVersion.IsPrerelease
                    || candidateVersion.CompareTo(_currentVersion) <= 0) return null;

                if (string.Equals(state.DeferredVersion, candidateVersion.ToString(),
                    StringComparison.OrdinalIgnoreCase)
                    && IsDeferralActive(state.DeferredUntilUtc, nowUtc)) return null;

                return new ApplicationUpdate(candidateVersion.ToString(), candidate.NativeUpdate);
            }
            finally
            {
                Interlocked.Exchange(ref _checkInProgress, 0);
            }
        }

        internal async Task CheckAfterUiShownAsync(
            IWin32Window owner,
            CancellationToken cancellationToken)
        {
            ApplicationUpdate update;
            try
            {
                update = await CheckForUpdateAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (update == null || cancellationToken.IsCancellationRequested) return;

            using (var prompt = new UpdatePromptForm(update.VersionText))
            {
                bool applying = false;
                prompt.UpdateRequested += async delegate
                {
                    if (applying) return;
                    applying = true;
                    prompt.BeginDownload();
                    try
                    {
                        await _engine.DownloadAndApplyAsync(
                            update,
                            prompt.ReportProgress,
                            cancellationToken);
                        prompt.ShowApplyDidNotRestartError();
                    }
                    catch (OperationCanceledException)
                    {
                        prompt.CloseAfterCancellation();
                    }
                    catch
                    {
                        prompt.ShowDownloadError();
                    }
                    finally
                    {
                        applying = false;
                    }
                };

                DialogResult result;
                try
                {
                    result = owner == null ? prompt.ShowDialog() : prompt.ShowDialog(owner);
                }
                catch
                {
                    return;
                }
                if (result == DialogResult.Cancel)
                    Defer(update.VersionText);
            }
        }

        internal void Defer(string versionText)
        {
            SemanticVersion version;
            if (!SemanticVersion.TryParse(versionText, out version) || version.IsPrerelease) return;
            UpdateCheckState state = SafeLoadState();
            DateTime nowUtc = EnsureUtc(_clock.UtcNow);
            state.DeferredVersion = version.ToString();
            state.DeferredUntilUtc = nowUtc.Add(DeferInterval);
            SafeSaveState(state);
        }

        internal static bool IsCheckDue(UpdateCheckState state, DateTime nowUtc)
        {
            if (state == null || state.LastCheckUtc == default(DateTime)) return true;
            DateTime lastCheckUtc = EnsureUtc(state.LastCheckUtc);
            if (lastCheckUtc > nowUtc.Add(FutureClockTolerance)) return true;
            return nowUtc - lastCheckUtc >= CheckInterval;
        }

        private static bool IsDeferralActive(DateTime deferredUntilUtc, DateTime nowUtc)
        {
            if (deferredUntilUtc == default(DateTime)) return false;
            DateTime untilUtc = EnsureUtc(deferredUntilUtc);
            if (untilUtc > nowUtc.Add(DeferInterval).Add(FutureClockTolerance)) return false;
            return untilUtc > nowUtc;
        }

        private UpdateCheckState SafeLoadState()
        {
            try { return _stateStore.Load() ?? new UpdateCheckState(); }
            catch { return new UpdateCheckState(); }
        }

        private void SafeSaveState(UpdateCheckState state)
        {
            try { _stateStore.Save(state); }
            catch { }
        }

        private static DateTime EnsureUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }

    internal sealed class VelopackReflectionUpdateEngine : IApplicationUpdateEngine
    {
        private const string AssemblyName = "Velopack";
        private readonly string _source;
        private readonly bool _isGitHub;
        private readonly Assembly _assembly;
        private object _manager;

        private VelopackReflectionUpdateEngine(string source, bool isGitHub)
        {
            _source = source;
            _isGitHub = isGitHub;
            _assembly = TryLoadAssembly();
        }

        internal bool IsRuntimePresent { get { return _assembly != null; } }

        public bool IsAvailable
        {
            get
            {
                if (_assembly == null) return false;
                try
                {
                    object manager = GetOrCreateManager();
                    PropertyInfo installed = manager.GetType().GetProperty(
                        "IsInstalled", BindingFlags.Public | BindingFlags.Instance);
                    return installed != null && (bool)installed.GetValue(manager, null);
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static VelopackReflectionUpdateEngine CreateGitHub(string repositoryUrl)
        {
            if (!string.Equals(repositoryUrl, GitHubUpdateService.RepositoryUrl, StringComparison.Ordinal))
                throw new InvalidOperationException("The production update repository is fixed.");
            return new VelopackReflectionUpdateEngine(repositoryUrl, true);
        }

        internal static VelopackReflectionUpdateEngine CreateLocalFeed(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("A local Velopack feed is required.", "directory");
            return new VelopackReflectionUpdateEngine(Path.GetFullPath(directory), false);
        }

        internal static void TryRunStartupHooks()
        {
            try
            {
                Assembly assembly = TryLoadAssembly();
                if (assembly == null) return;
                Type appType = assembly.GetType("Velopack.VelopackApp", false);
                if (appType == null) return;
                MethodInfo build = appType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
                if (build == null) return;
                object builder = build.Invoke(null, null);
                MethodInfo run = builder.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(delegate(MethodInfo method) { return method.Name == "Run"; });
                if (run == null) return;
                run.Invoke(builder, BuildOptionalArguments(run.GetParameters(), null, null, CancellationToken.None));
            }
            catch
            {
                // A missing or incompatible updater must never prevent the main application from opening.
            }
        }

        public async Task<ApplicationUpdate> CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            if (!IsAvailable) return null;
            object manager = GetOrCreateManager();
            MethodInfo method = FindMethod(manager.GetType(), "CheckForUpdatesAsync", null);
            if (method == null) throw new MissingMethodException("Velopack CheckForUpdatesAsync was not found.");
            object update = await InvokeTaskAsync(manager, method, null, null, cancellationToken);
            if (update == null) return null;
            string version = ReadTargetVersion(update);
            return string.IsNullOrWhiteSpace(version) ? null : new ApplicationUpdate(version, update);
        }

        public async Task DownloadAndApplyAsync(
            ApplicationUpdate update,
            Action<int> progress,
            CancellationToken cancellationToken)
        {
            if (update == null || update.NativeUpdate == null) throw new ArgumentNullException("update");
            object manager = GetOrCreateManager();
            MethodInfo download = FindMethod(manager.GetType(), "DownloadUpdatesAsync", update.NativeUpdate);
            if (download == null) throw new MissingMethodException("Velopack DownloadUpdatesAsync was not found.");
            await InvokeTaskAsync(manager, download, update.NativeUpdate, progress, cancellationToken);

            object targetRelease = ReadProperty(update.NativeUpdate, "TargetFullRelease")
                ?? ReadProperty(update.NativeUpdate, "TargetRelease");
            if (targetRelease == null)
                throw new InvalidOperationException("Velopack did not provide a target release to apply.");
            MethodInfo apply = FindMethod(manager.GetType(), "ApplyUpdatesAndRestart", targetRelease);
            if (apply == null) throw new MissingMethodException("Velopack ApplyUpdatesAndRestart was not found.");
            try
            {
                apply.Invoke(manager, BuildOptionalArguments(
                    apply.GetParameters(), targetRelease, null, cancellationToken));
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private object GetOrCreateManager()
        {
            if (_manager != null) return _manager;
            TryRunStartupHooks();
            Type managerType = _assembly.GetType("Velopack.UpdateManager", true);
            if (_isGitHub)
            {
                Type sourceType = _assembly.GetType("Velopack.Sources.GithubSource", true);
                object githubSource = CreateGithubSource(sourceType, _source);
                ConstructorInfo constructor = managerType.GetConstructors()
                    .Where(delegate(ConstructorInfo item)
                    {
                        ParameterInfo[] parameters = item.GetParameters();
                        return parameters.Length > 0
                            && parameters[0].ParameterType.IsAssignableFrom(sourceType);
                    })
                    .OrderBy(delegate(ConstructorInfo item) { return item.GetParameters().Length; })
                    .FirstOrDefault();
                if (constructor == null) throw new MissingMethodException("Velopack UpdateManager source constructor was not found.");
                _manager = constructor.Invoke(BuildOptionalArguments(
                    constructor.GetParameters(), githubSource, null, CancellationToken.None));
            }
            else
            {
                ConstructorInfo constructor = managerType.GetConstructors()
                    .Where(delegate(ConstructorInfo item)
                    {
                        ParameterInfo[] parameters = item.GetParameters();
                        return parameters.Length > 0 && parameters[0].ParameterType == typeof(string);
                    })
                    .OrderBy(delegate(ConstructorInfo item) { return item.GetParameters().Length; })
                    .FirstOrDefault();
                if (constructor == null) throw new MissingMethodException("Velopack UpdateManager path constructor was not found.");
                _manager = constructor.Invoke(BuildOptionalArguments(
                    constructor.GetParameters(), _source, null, CancellationToken.None));
            }
            return _manager;
        }

        private static object CreateGithubSource(Type sourceType, string repositoryUrl)
        {
            foreach (ConstructorInfo constructor in sourceType.GetConstructors()
                .OrderBy(delegate(ConstructorInfo item) { return item.GetParameters().Length; }))
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(string)) continue;
                object[] arguments = new object[parameters.Length];
                bool valid = true;
                for (int index = 0; index < parameters.Length; index++)
                {
                    ParameterInfo parameter = parameters[index];
                    if (index == 0) arguments[index] = repositoryUrl;
                    else if (string.Equals(parameter.Name, "accessToken", StringComparison.OrdinalIgnoreCase))
                        arguments[index] = null;
                    else if (string.Equals(parameter.Name, "prerelease", StringComparison.OrdinalIgnoreCase))
                        arguments[index] = false;
                    else if (parameter.HasDefaultValue)
                        arguments[index] = NormalizeDefaultValue(parameter);
                    else if (!parameter.ParameterType.IsValueType)
                        arguments[index] = null;
                    else
                    {
                        valid = false;
                        break;
                    }
                }
                if (valid) return constructor.Invoke(arguments);
            }
            throw new MissingMethodException("A compatible Velopack GithubSource constructor was not found.");
        }

        private static MethodInfo FindMethod(Type type, string name, object firstArgument)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(delegate(MethodInfo method)
                {
                    if (!string.Equals(method.Name, name, StringComparison.Ordinal)) return false;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (firstArgument == null) return parameters.Length == 0 || parameters.All(IsOptionalParameter);
                    return parameters.Length > 0
                        && parameters[0].ParameterType.IsInstanceOfType(firstArgument);
                })
                .OrderBy(delegate(MethodInfo method) { return method.GetParameters().Length; })
                .FirstOrDefault();
        }

        private static async Task<object> InvokeTaskAsync(
            object target,
            MethodInfo method,
            object firstArgument,
            Action<int> progress,
            CancellationToken cancellationToken)
        {
            object result;
            try
            {
                result = method.Invoke(target, BuildOptionalArguments(
                    method.GetParameters(), firstArgument, progress, cancellationToken));
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
            Task task = result as Task;
            if (task == null) return result;
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                TargetInvocationException targetException = exception as TargetInvocationException;
                throw targetException == null ? exception : targetException.InnerException ?? targetException;
            }
            PropertyInfo resultProperty = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
            return resultProperty == null ? null : resultProperty.GetValue(task, null);
        }

        private static object[] BuildOptionalArguments(
            ParameterInfo[] parameters,
            object firstArgument,
            Action<int> progress,
            CancellationToken cancellationToken)
        {
            var arguments = new object[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                ParameterInfo parameter = parameters[index];
                if (index == 0 && firstArgument != null
                    && parameter.ParameterType.IsInstanceOfType(firstArgument))
                    arguments[index] = firstArgument;
                else if (parameter.ParameterType == typeof(Action<int>))
                    arguments[index] = progress;
                else if (parameter.ParameterType == typeof(CancellationToken))
                    arguments[index] = cancellationToken;
                else if (parameter.HasDefaultValue)
                    arguments[index] = NormalizeDefaultValue(parameter);
                else if (!parameter.ParameterType.IsValueType)
                    arguments[index] = null;
                else
                    arguments[index] = Activator.CreateInstance(parameter.ParameterType);
            }
            return arguments;
        }

        private static bool IsOptionalParameter(ParameterInfo parameter)
        {
            return parameter.HasDefaultValue
                || parameter.ParameterType == typeof(CancellationToken)
                || !parameter.ParameterType.IsValueType;
        }

        private static object NormalizeDefaultValue(ParameterInfo parameter)
        {
            object value = parameter.DefaultValue;
            if (value == DBNull.Value || value == Missing.Value)
                return parameter.ParameterType.IsValueType
                    ? Activator.CreateInstance(parameter.ParameterType)
                    : null;
            return value;
        }

        private static string ReadTargetVersion(object update)
        {
            object release = ReadProperty(update, "TargetFullRelease")
                ?? ReadProperty(update, "TargetRelease");
            object version = release == null ? null : ReadProperty(release, "Version");
            return version == null ? null : version.ToString();
        }

        private static object ReadProperty(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return property == null ? null : property.GetValue(target, null);
        }

        private static Assembly TryLoadAssembly()
        {
            try
            {
                Assembly loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(delegate(Assembly assembly)
                {
                    return string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase);
                });
                if (loaded != null) return loaded;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AssemblyName + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
