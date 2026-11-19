using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>Holds the debug-utils callback, error queue, and callback lifetime.</summary>
/// <remarks>
/// The callback runs on the producing thread; <see cref="Lock"/> serializes the record path and
/// <see cref="ConsumeErrors"/>. A <see cref="GCHandle"/> in <c>pUserData</c> recovers the
/// instance, and the static delegate keeps the callback rooted.
/// </remarks>
internal sealed unsafe class VulkanValidation : IDisposable
{
    private const string VALIDATION_LAYER_NAME = "VK_LAYER_KHRONOS_validation";

    private const DebugUtilsMessageSeverityFlagsEXT MESSAGE_SEVERITY =
        DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
        | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;

    private const DebugUtilsMessageTypeFlagsEXT MESSAGE_TYPE =
        DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
        | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
        | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt;

    private static readonly DebugUtilsMessengerCallbackFunctionEXT _callbackDelegate =
        OnDebugMessage;
    private static readonly PfnDebugUtilsMessengerCallbackEXT _callback = new(_callbackDelegate);

    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Queue<string> _errors = new();

    private GCHandle _selfHandle;
    private bool _disposed;

    /// <summary>Initializes a validation holder for one instance.</summary>
    /// <param name="logger">The renderer logger that receives validation messages.</param>
    internal VulkanValidation(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _selfHandle = GCHandle.Alloc(this);
    }

    /// <summary>Gets the Khronos validation layer name.</summary>
    internal static string LayerName => VALIDATION_LAYER_NAME;

    /// <summary>Creates the messenger create info for this holder.</summary>
    internal DebugUtilsMessengerCreateInfoEXT CreateMessengerCreateInfo() =>
        new()
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = MESSAGE_SEVERITY,
            MessageType = MESSAGE_TYPE,
            PfnUserCallback = _callback,
            PUserData = (void*)GCHandle.ToIntPtr(_selfHandle),
        };

    /// <summary>Returns and clears the recorded validation errors in arrival order.</summary>
    internal IReadOnlyList<string> ConsumeErrors()
    {
        lock (_gate)
        {
            if (_errors.Count == 0)
            {
                return [];
            }

            string[] errors = [.. _errors];
            _errors.Clear();
            return errors;
        }
    }

    /// <summary>Throws when a validation error was recorded since the previous call.</summary>
    /// <exception cref="GraphicsException">
    /// At least one error was recorded; the message lists them and the code is <c>RB3001</c>.
    /// </exception>
    internal void ThrowIfErrors(string operation)
    {
        IReadOnlyList<string> errors = ConsumeErrors();
        if (errors.Count == 0)
        {
            return;
        }

        StringBuilder message = new();
        message
            .Append("Vulkan validation reported ")
            .Append(errors.Count)
            .Append(" error(s) while ")
            .Append(operation)
            .Append(':');
        foreach (string error in errors)
        {
            message.AppendLine().Append("- ").Append(error);
        }

        throw new GraphicsException(
            message.ToString(),
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private static uint OnDebugMessage(
        DebugUtilsMessageSeverityFlagsEXT messageSeverity,
        DebugUtilsMessageTypeFlagsEXT messageTypes,
        DebugUtilsMessengerCallbackDataEXT* callbackData,
        void* userData
    )
    {
        if (userData is null || (messageSeverity & MESSAGE_SEVERITY) == 0)
        {
            return 0;
        }

        VulkanValidation? validation =
            GCHandle.FromIntPtr((IntPtr)userData).Target as VulkanValidation;
        if (validation is null)
        {
            return 0;
        }

        string message =
            callbackData is not null && callbackData->PMessage is not null
                ? Marshal.PtrToStringUTF8((IntPtr)callbackData->PMessage) ?? string.Empty
                : string.Empty;
        validation.Record(messageSeverity, message);
        return 0;
    }

    private void Record(DebugUtilsMessageSeverityFlagsEXT severity, string message)
    {
        if ((severity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0)
        {
            lock (_gate)
            {
                _errors.Enqueue(message);
            }

            _logger.LogError("Vulkan validation: {Message}", message);
        }
        else
        {
            _logger.LogWarning("Vulkan validation: {Message}", message);
        }
    }
}
