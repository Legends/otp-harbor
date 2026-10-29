using System.Runtime.InteropServices;
using TOTP.Core.Security.Models;

namespace TOTP.Platform.MacOS.Security;

public sealed partial class MacOSKeychainNative : IMacOSKeychainNative
{
    private const string ServiceName = "io.github.legends.totpmanager.quick-unlock";
    private const uint Utf8Encoding = 0x08000100;
    private const int Success = 0;
    private const int DuplicateItem = -25299;
    private const int ItemNotFound = -25300;
    private const int AuthFailed = -25293;
    private const int NotAvailable = -25291;
    private const int InteractionNotAllowed = -25308;
    private const int UserCanceled = -128;
    private const nuint UserPresence = 1;
    private const nuint DeviceOwnerAuthenticationPolicy = 2;

    public PlatformSecretStoreAvailability GetAvailability()
    {
        if (!OperatingSystem.IsMacOS()) return PlatformSecretStoreAvailability.NotSupported;
        _ = Symbols.LocalAuthenticationHandle;
        using var context = ObjectiveCHandle.Create("LAContext");
        if (context.IsInvalid) return PlatformSecretStoreAvailability.NotSupported;

        var canEvaluate = NativeMethods.objc_msgSend_bool_nuint_outIntPtr(
            context.Value,
            Symbols.CanEvaluatePolicy,
            DeviceOwnerAuthenticationPolicy,
            out var error);
        if (canEvaluate) return PlatformSecretStoreAvailability.Available;
        if (error == IntPtr.Zero) return PlatformSecretStoreAvailability.TemporarilyUnavailable;

        var code = NativeMethods.objc_msgSend_nint(error, Symbols.ErrorCode);
        return code switch
        {
            -5 or -7 => PlatformSecretStoreAvailability.NotConfigured,
            -6 => PlatformSecretStoreAvailability.NotSupported,
            -8 => PlatformSecretStoreAvailability.TemporarilyUnavailable,
            _ => PlatformSecretStoreAvailability.TemporarilyUnavailable
        };
    }

    public MacOSKeychainNativeStatus Store(
        string secretReference,
        ReadOnlyMemory<byte> secret)
    {
        EnsureMacOS();
        using var attributes = CreateIdentityQuery(secretReference);
        var accessible = Symbols.SecurityConstant("kSecAttrAccessibleWhenUnlockedThisDeviceOnly");
        var accessControl = NativeMethods.SecAccessControlCreateWithFlags(
            IntPtr.Zero,
            accessible,
            UserPresence,
            out var accessError);
        if (accessError != IntPtr.Zero) NativeMethods.CFRelease(accessError);
        if (accessControl == IntPtr.Zero) return MacOSKeychainNativeStatus.NotConfigured;

        using var access = CFHandle.FromOwned(accessControl);
        using var data = CFHandle.FromOwned(CreateData(secret.Span));
        NativeMethods.CFDictionarySetValue(
            attributes.Value,
            Symbols.SecurityConstant("kSecAttrAccessControl"),
            access.Value);
        NativeMethods.CFDictionarySetValue(
            attributes.Value,
            Symbols.SecurityConstant("kSecValueData"),
            data.Value);

        return MapStatus(NativeMethods.SecItemAdd(attributes.Value, IntPtr.Zero));
    }

    public MacOSKeychainReadResult Retrieve(string secretReference)
    {
        EnsureMacOS();
        using var query = CreateIdentityQuery(secretReference);
        using var prompt = CreateString("Unlock OTP Harbor");
        NativeMethods.CFDictionarySetValue(
            query.Value,
            Symbols.SecurityConstant("kSecReturnData"),
            Symbols.CoreFoundationBooleanTrue);
        NativeMethods.CFDictionarySetValue(
            query.Value,
            Symbols.SecurityConstant("kSecMatchLimit"),
            Symbols.SecurityConstant("kSecMatchLimitOne"));
        NativeMethods.CFDictionarySetValue(
            query.Value,
            Symbols.SecurityConstant("kSecUseOperationPrompt"),
            prompt.Value);

        var status = NativeMethods.SecItemCopyMatching(query.Value, out var result);
        var mapped = MapStatus(status);
        if (mapped != MacOSKeychainNativeStatus.Success || result == IntPtr.Zero)
            return new MacOSKeychainReadResult(mapped);

        using var data = CFHandle.FromOwned(result);
        var length = NativeMethods.CFDataGetLength(data.Value);
        if (length is <= 0 or > 4096) return new MacOSKeychainReadResult(MacOSKeychainNativeStatus.Failed);
        var secret = new byte[checked((int)length)];
        Marshal.Copy(NativeMethods.CFDataGetBytePtr(data.Value), secret, 0, secret.Length);
        return new MacOSKeychainReadResult(MacOSKeychainNativeStatus.Success, secret);
    }

    public MacOSKeychainNativeStatus Delete(string secretReference)
    {
        EnsureMacOS();
        using var query = CreateIdentityQuery(secretReference);
        return MapStatus(NativeMethods.SecItemDelete(query.Value));
    }

    private static CFHandle CreateIdentityQuery(string secretReference)
    {
        var dictionary = NativeMethods.CFDictionaryCreateMutable(
            IntPtr.Zero,
            0,
            Symbols.DictionaryKeyCallbacks,
            Symbols.DictionaryValueCallbacks);
        if (dictionary == IntPtr.Zero) throw new InvalidOperationException("Could not create Keychain query.");
        var result = CFHandle.FromOwned(dictionary);
        using var service = CreateString(ServiceName);
        using var account = CreateString(secretReference);
        NativeMethods.CFDictionarySetValue(
            result.Value,
            Symbols.SecurityConstant("kSecClass"),
            Symbols.SecurityConstant("kSecClassGenericPassword"));
        NativeMethods.CFDictionarySetValue(
            result.Value,
            Symbols.SecurityConstant("kSecAttrService"),
            service.Value);
        NativeMethods.CFDictionarySetValue(
            result.Value,
            Symbols.SecurityConstant("kSecAttrAccount"),
            account.Value);
        NativeMethods.CFDictionarySetValue(
            result.Value,
            Symbols.SecurityConstant("kSecUseDataProtectionKeychain"),
            Symbols.CoreFoundationBooleanTrue);
        return result;
    }

    private static CFHandle CreateString(string value) => CFHandle.FromOwned(
        NativeMethods.CFStringCreateWithCString(IntPtr.Zero, value, Utf8Encoding));

    private static unsafe IntPtr CreateData(ReadOnlySpan<byte> value)
    {
        fixed (byte* pointer = value)
        {
            return NativeMethods.CFDataCreate(IntPtr.Zero, (IntPtr)pointer, value.Length);
        }
    }

    private static MacOSKeychainNativeStatus MapStatus(int status) => status switch
    {
        Success => MacOSKeychainNativeStatus.Success,
        ItemNotFound => MacOSKeychainNativeStatus.NotFound,
        UserCanceled => MacOSKeychainNativeStatus.Cancelled,
        AuthFailed => MacOSKeychainNativeStatus.AccessDenied,
        NotAvailable => MacOSKeychainNativeStatus.NotSupported,
        InteractionNotAllowed => MacOSKeychainNativeStatus.TemporarilyUnavailable,
        DuplicateItem => MacOSKeychainNativeStatus.Failed,
        _ => MacOSKeychainNativeStatus.Failed
    };

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("The Keychain adapter requires macOS.");
    }

    private sealed class CFHandle : IDisposable
    {
        private IntPtr _value;
        public IntPtr Value => _value;
        public bool IsInvalid => _value == IntPtr.Zero;
        private CFHandle(IntPtr value) => _value = value;

        public static CFHandle FromOwned(IntPtr value)
        {
            if (value == IntPtr.Zero) throw new InvalidOperationException("A Core Foundation object could not be created.");
            return new CFHandle(value);
        }

        public void Dispose()
        {
            var value = Interlocked.Exchange(ref _value, IntPtr.Zero);
            if (value != IntPtr.Zero) NativeMethods.CFRelease(value);
        }
    }

    private sealed class ObjectiveCHandle : IDisposable
    {
        private IntPtr _value;
        public IntPtr Value => _value;
        public bool IsInvalid => _value == IntPtr.Zero;
        private ObjectiveCHandle(IntPtr value) => _value = value;

        public static ObjectiveCHandle Create(string className)
        {
            var type = NativeMethods.objc_getClass(className);
            return new ObjectiveCHandle(type == IntPtr.Zero
                ? IntPtr.Zero
                : NativeMethods.objc_msgSend(type, Symbols.New));
        }

        public void Dispose()
        {
            var value = Interlocked.Exchange(ref _value, IntPtr.Zero);
            if (value != IntPtr.Zero) NativeMethods.objc_release(value);
        }
    }

    private static class Symbols
    {
        private static readonly Lazy<IntPtr> SecurityLibrary = new(() => NativeLibrary.Load(
            "/System/Library/Frameworks/Security.framework/Security"));
        private static readonly Lazy<IntPtr> CoreFoundationLibrary = new(() => NativeLibrary.Load(
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation"));
        private static readonly Lazy<IntPtr> LocalAuthenticationLibrary = new(() => NativeLibrary.Load(
            "/System/Library/Frameworks/LocalAuthentication.framework/LocalAuthentication"));

        public static IntPtr New { get; } = NativeMethods.sel_registerName("new");
        public static IntPtr CanEvaluatePolicy { get; } = NativeMethods.sel_registerName("canEvaluatePolicy:error:");
        public static IntPtr ErrorCode { get; } = NativeMethods.sel_registerName("code");
        public static IntPtr LocalAuthenticationHandle => LocalAuthenticationLibrary.Value;
        public static IntPtr DictionaryKeyCallbacks => NativeLibrary.GetExport(
            CoreFoundationLibrary.Value,
            "kCFTypeDictionaryKeyCallBacks");
        public static IntPtr DictionaryValueCallbacks => NativeLibrary.GetExport(
            CoreFoundationLibrary.Value,
            "kCFTypeDictionaryValueCallBacks");
        public static IntPtr CoreFoundationBooleanTrue => Marshal.ReadIntPtr(NativeLibrary.GetExport(
            CoreFoundationLibrary.Value,
            "kCFBooleanTrue"));

        public static IntPtr SecurityConstant(string name) =>
            Marshal.ReadIntPtr(NativeLibrary.GetExport(SecurityLibrary.Value, name));
    }

    private static partial class NativeMethods
    {
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

        [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

        [LibraryImport(CoreFoundation)]
        public static partial IntPtr CFDataCreate(IntPtr allocator, IntPtr bytes, nint length);

        [LibraryImport(CoreFoundation)]
        public static partial nint CFDataGetLength(IntPtr data);

        [LibraryImport(CoreFoundation)]
        public static partial IntPtr CFDataGetBytePtr(IntPtr data);

        [LibraryImport(CoreFoundation)]
        public static partial IntPtr CFDictionaryCreateMutable(
            IntPtr allocator,
            nint capacity,
            IntPtr keyCallbacks,
            IntPtr valueCallbacks);

        [LibraryImport(CoreFoundation)]
        public static partial void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

        [LibraryImport(CoreFoundation)]
        public static partial void CFRelease(IntPtr value);

        [LibraryImport(Security)]
        public static partial IntPtr SecAccessControlCreateWithFlags(
            IntPtr allocator,
            IntPtr protection,
            nuint flags,
            out IntPtr error);

        [LibraryImport(Security)]
        public static partial int SecItemAdd(IntPtr attributes, IntPtr result);

        [LibraryImport(Security)]
        public static partial int SecItemCopyMatching(IntPtr query, out IntPtr result);

        [LibraryImport(Security)]
        public static partial int SecItemDelete(IntPtr query);

        [LibraryImport(ObjectiveC, StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr objc_getClass(string name);

        [LibraryImport(ObjectiveC, StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr sel_registerName(string name);

        [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        public static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool objc_msgSend_bool_nuint_outIntPtr(
            IntPtr receiver,
            IntPtr selector,
            nuint value,
            out IntPtr error);

        [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
        public static partial nint objc_msgSend_nint(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjectiveC)]
        public static partial void objc_release(IntPtr value);
    }
}
