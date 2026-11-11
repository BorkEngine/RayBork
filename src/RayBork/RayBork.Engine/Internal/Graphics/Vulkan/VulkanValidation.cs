using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Holds the debug-utils callback, the resulting validation error queue, and the callback's
/// lifetime handle for one Vulkan instance.
/// </summary>
/// <remarks>
/// <para>
/// The validation layer invokes the callback on whichever thread produced the message. The error
/// queue and its <see cref="Lock"/> therefore make <see cref="ConsumeErrors"/> and the private
/// record path safe on any thread, including the render thread that reads them.
/// </para>
/// <para>
/// The callback is a static method because Silk.NET allocates the unmanaged function pointer from a
/// delegate. The instance is recovered through <c>pUserData</c>, a <see cref="GCHandle"/> kept
/// alive for the instance lifetime; the delegate field keeps the callback itself rooted.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanValidation : IDisposable {
    private const string VALIDATION_LAYER_NAME = "VK_LAYER_KHRONOS_validation";

    private static readonly DebugUtilsMessageSeverityFlagsEXT _messageSeverity =
        DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
        | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;

    private static readonly DebugUtilsMessageTypeFlagsEXT _messageType =
        DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
        | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
        | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt;

    private static readonly DebugUtilsMessengerCallbackFunctionEXT _callbackDelegate = OnDebugMessage;
    private static readonly PfnDebugUtilsMessengerCallbackEXT _callback = new(_callbackDelegate);

    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Queue<string> _errors = new();

    private GCHandle _selfHandle;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanValidation"/> class.
    /// </summary>
    /// <param name="logger">The renderer logger that receives validation warnings and errors.</param>
    internal VulkanValidation(ILogger logger) {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _selfHandle = GCHandle.Alloc(this);
    }

    /// <summary>
    /// Gets the name of the Khronos validation layer.
    /// </summary>
    internal static string LayerName => VALIDATION_LAYER_NAME;

    /// <summary>
    /// Creates the debug-utils messenger configuration for this validation holder.
    /// </summary>
    /// <returns>The messenger create info with the instance callback and user data set.</returns>
    internal DebugUtilsMessengerCreateInfoEXT CreateMessengerCreateInfo() => new() {
        SType = StructureType.DebugUtilsMessengerCreateInfoExt,
        MessageSeverity = _messageSeverity,
        MessageType = _messageType,
        PfnUserCallback = _callback,
        PUserData = (void*)GCHandle.ToIntPtr(_selfHandle),
    };

    /// <summary>
    /// Returns and clears the validation errors recorded since the previous call.
    /// </summary>
    /// <returns>The recorded error messages in arrival order.</returns>
    internal IReadOnlyList<string> ConsumeErrors() {
        lock (_gate) {
            if (_errors.Count == 0) {
                return [];
            }

            string[] errors = [.. _errors];
            _errors.Clear();
            return errors;
        }
    }

    /// <summary>
    /// Throws when validation errors were recorded since the previous call.
    /// </summary>
    /// <param name="operation">The operation that was in progress, for the diagnostic message.</param>
    /// <exception cref="GraphicsException">
    /// At least one validation error was recorded. The message lists every error and the diagnostic
    /// code is <c>RB3001</c>.
    /// </exception>
    internal void ThrowIfErrors(string operation) {
        IReadOnlyList<string> errors = ConsumeErrors();
        if (errors.Count == 0) {
            return;
        }

        StringBuilder message = new();
        message.Append("Vulkan validation reported ")
            .Append(errors.Count)
            .Append(" error(s) while ")
            .Append(operation)
            .Append(':');
        foreach (string error in errors) {
            message.AppendLine().Append("- ").Append(error);
        }

        throw new GraphicsException(
            message.ToString(),
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        if (_selfHandle.IsAllocated) {
            _selfHandle.Free();
        }
    }

    private static uint OnDebugMessage(
        DebugUtilsMessageSeverityFlagsEXT messageSeverity,
        DebugUtilsMessageTypeFlagsEXT messageTypes,
        DebugUtilsMessengerCallbackDataEXT* callbackData,
        void* userData) {
        if (userData is null
            || (messageSeverity & _messageSeverity) == 0) {
            return 0;
        }

        VulkanValidation? validation = GCHandle.FromIntPtr((IntPtr)userData).Target as VulkanValidation;
        if (validation is null) {
            return 0;
        }

        string message = callbackData is not null && callbackData->PMessage is not null
            ? Marshal.PtrToStringUTF8((IntPtr)callbackData->PMessage) ?? string.Empty
            : string.Empty;
        validation.Record(messageSeverity, message);
        return 0;
    }

    private void Record(DebugUtilsMessageSeverityFlagsEXT severity, string message) {
        if ((severity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0) {
            lock (_gate) {
                _errors.Enqueue(message);
            }

            _logger.LogError("Vulkan validation: {Message}", message);
        } else {
            _logger.LogWarning("Vulkan validation: {Message}", message);
        }
    }
}
