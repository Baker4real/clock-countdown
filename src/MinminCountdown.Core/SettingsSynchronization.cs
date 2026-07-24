using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace MinminCountdown.Core;

public static class SettingsMutex
{
    private const string GlobalPrefix = "Global\\MinminCountdownSettings-";
    private static readonly MutexRights WriteRights = MutexRights.Synchronize | MutexRights.Modify;

    public static string GetWriteMutexName(string settingsPath)
    {
        return GlobalPrefix + HashPath(NormalizePath(settingsPath));
    }

    public static SettingsMutexLease EnterWriteMutex(string settingsPath, int timeoutSeconds = 5)
    {
        if (timeoutSeconds is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "等待设置写入锁的时间必须在 1 到 60 秒之间。");
        }

        var mutexName = GetWriteMutexName(settingsPath);
        Mutex? mutex = null;
        try
        {
            mutex = OpenOrCreate(mutexName);
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    throw new TimeoutException($"等待设置写入锁超时（{timeoutSeconds} 秒）。");
                }
            }
            catch (AbandonedMutexException)
            {
                // The operating system releases an abandoned mutex. The caller now owns it.
            }

            return new SettingsMutexLease(mutex);
        }
        catch
        {
            mutex?.Dispose();
            throw;
        }
    }

    private static Mutex OpenOrCreate(string mutexName)
    {
        try
        {
            return MutexAcl.OpenExisting(mutexName, WriteRights);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            var security = new MutexSecurity();
            var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            security.AddAccessRule(new MutexAccessRule(
                authenticatedUsers,
                WriteRights,
                AccessControlType.Allow));

            try
            {
                return MutexAcl.Create(false, mutexName, out _, security);
            }
            catch
            {
                try
                {
                    return MutexAcl.OpenExisting(mutexName, WriteRights);
                }
                catch
                {
                    throw;
                }
            }
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("设置文件路径不能为空。", nameof(path));
        }

        return Path.GetFullPath(path).ToUpperInvariant();
    }

    internal static string HashPath(string normalizedPath)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath))).ToUpperInvariant();
    }
}

public sealed class SettingsMutexLease : IDisposable
{
    private Mutex? _mutex;

    internal SettingsMutexLease(Mutex mutex)
    {
        _mutex = mutex;
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        finally
        {
            mutex.Dispose();
        }
    }
}

public static class SingleInstanceMutex
{
    private const string LocalPrefix = "Local\\MinminCountdown-";

    public static string GetMutexName(string applicationRoot, string settingsPath, string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new ArgumentException("用户 SID 不能为空。", nameof(userSid));
        }

        var scope = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Path.GetFullPath(applicationRoot).ToUpperInvariant()}|{Path.GetFullPath(settingsPath).ToUpperInvariant()}");
        var hash = SettingsMutex.HashPath(scope);
        return $"{LocalPrefix}{userSid}-{hash[..24]}";
    }

    public static string GetCurrentUserMutexName(string applicationRoot, string settingsPath)
    {
        var userSid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new InvalidOperationException("无法获取当前 Windows 用户 SID。");
        }

        return GetMutexName(applicationRoot, settingsPath, userSid);
    }

    public static SingleInstanceMutexLease? TryAcquire(string applicationRoot, string settingsPath, string? userSid = null)
    {
        var mutexName = userSid is null
            ? GetCurrentUserMutexName(applicationRoot, settingsPath)
            : GetMutexName(applicationRoot, settingsPath, userSid);
        var mutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceMutexLease(mutex);
    }
}

public sealed class SingleInstanceMutexLease : IDisposable
{
    private Mutex? _mutex;

    internal SingleInstanceMutexLease(Mutex mutex)
    {
        _mutex = mutex;
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        finally
        {
            mutex.Dispose();
        }
    }
}
