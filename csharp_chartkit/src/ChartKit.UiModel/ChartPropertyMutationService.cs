using System.Text.Json;
using System.Text.Json.Nodes;
using ChartKit.CSharp.ModuleHost;
using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.UiModel;

public sealed record ChangeChartPropertyCommand(
    string InstanceId,
    string PropertyId,
    JsonNode? Value);

public sealed record ChartPropertyChangeResult(
    string InstanceId,
    string PropertyId,
    bool Succeeded,
    bool Changed,
    ChartChangeImpact ChangeImpact,
    ChartModuleProfile? Profile,
    string? Error)
{
    public static ChartPropertyChangeResult Success(
        string instanceId,
        string propertyId,
        bool changed,
        ChartChangeImpact changeImpact,
        ChartModuleProfile profile) =>
        new(
            instanceId,
            propertyId,
            true,
            changed,
            changeImpact,
            profile,
            null);

    public static ChartPropertyChangeResult Failure(
        string instanceId,
        string propertyId,
        bool changed,
        string error,
        ChartModuleProfile? profile = null) =>
        new(
            instanceId,
            propertyId,
            false,
            changed,
            ChartChangeImpact.None,
            profile,
            error);
}

public sealed class ChartPropertyMutationService
{
    private readonly ChartModuleHost _host;

    public ChartPropertyMutationService(ChartModuleHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public ChartPropertyChangeResult Execute(
        ChangeChartPropertyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        string instanceId;
        string propertyId;
        try
        {
            instanceId = RequireText(
                command.InstanceId,
                nameof(command.InstanceId));
            propertyId = RequireText(
                command.PropertyId,
                nameof(command.PropertyId));
        }
        catch (Exception exception)
        {
            return ChartPropertyChangeResult.Failure(
                command.InstanceId ?? string.Empty,
                command.PropertyId ?? string.Empty,
                false,
                exception.Message);
        }

        if (!_host.TryGetSnapshot(
                instanceId,
                out ChartModuleRuntimeSnapshot? snapshot) ||
            snapshot is null)
        {
            return ChartPropertyChangeResult.Failure(
                instanceId,
                propertyId,
                false,
                "Chart module instance is not hosted.");
        }

        ChartHostedPropertyDescriptor? hostedProperty;
        try
        {
            hostedProperty = _host
                .CollectPropertyDescriptors(instanceId)
                .SingleOrDefault(property =>
                    StringComparer.Ordinal.Equals(
                        property.Descriptor.PropertyId,
                        propertyId));
        }
        catch (Exception exception)
        {
            return ChartPropertyChangeResult.Failure(
                instanceId,
                propertyId,
                false,
                exception.Message,
                snapshot.Profile);
        }

        if (hostedProperty is null)
        {
            return ChartPropertyChangeResult.Failure(
                instanceId,
                propertyId,
                false,
                "Chart property is not exposed by the hosted module.",
                snapshot.Profile);
        }

        ChartPropertyDescriptor descriptor = hostedProperty.Descriptor;
        if (descriptor.IsReadOnly)
        {
            return ChartPropertyChangeResult.Failure(
                instanceId,
                propertyId,
                false,
                "Chart property is read-only.",
                snapshot.Profile);
        }

        JsonNode normalizedValue;
        try
        {
            normalizedValue = NormalizeValue(descriptor, command.Value);
        }
        catch (Exception exception)
        {
            return ChartPropertyChangeResult.Failure(
                instanceId,
                propertyId,
                false,
                exception.Message,
                snapshot.Profile);
        }

        JsonNode? currentValue = NormalizeCurrentValue(descriptor);
        if (JsonNode.DeepEquals(currentValue, normalizedValue))
        {
            return ChartPropertyChangeResult.Success(
                instanceId,
                propertyId,
                false,
                ChartChangeImpact.None,
                snapshot.Profile);
        }

        ChartModuleProfile updatedProfile;
        try
        {
            updatedProfile = ApplyChange(
                snapshot.Profile,
                descriptor,
                normalizedValue);
        }
        catch (Exception exception)
        {
            return ChartPropertyChangeResult.Failure(
                instanceId,
                propertyId,
                false,
                exception.Message,
                snapshot.Profile);
        }

        ChartModuleOperationResult operation =
            _host.UpsertProfile(updatedProfile);
        if (!operation.Succeeded)
        {
            return ChartPropertyChangeResult.Failure(
                instanceId,
                propertyId,
                operation.Changed,
                operation.Error ?? "Chart property update failed.",
                snapshot.Profile);
        }

        if (!_host.TryGetSnapshot(
                instanceId,
                out ChartModuleRuntimeSnapshot? refreshed) ||
            refreshed is null)
        {
            return ChartPropertyChangeResult.Failure(
                instanceId,
                propertyId,
                operation.Changed,
                "Updated chart module snapshot is unavailable.");
        }

        return ChartPropertyChangeResult.Success(
            instanceId,
            propertyId,
            operation.Changed,
            operation.Changed
                ? descriptor.ChangeImpact
                : ChartChangeImpact.None,
            refreshed.Profile);
    }

    private static ChartModuleProfile ApplyChange(
        ChartModuleProfile source,
        ChartPropertyDescriptor descriptor,
        JsonNode normalizedValue)
    {
        JsonObject parameters = (JsonObject)source.Parameters.DeepClone();
        JsonObject style = (JsonObject)source.Style.DeepClone();
        JsonObject persistentState =
            (JsonObject)source.PersistentState.DeepClone();
        string placement = source.Placement;
        int zIndex = source.ZIndex;

        switch (descriptor.Storage)
        {
            case ChartPropertyStorage.Parameters:
                parameters[descriptor.PropertyId] = normalizedValue.DeepClone();
                break;
            case ChartPropertyStorage.Style:
                style[descriptor.PropertyId] = normalizedValue.DeepClone();
                break;
            case ChartPropertyStorage.PersistentState:
                persistentState[descriptor.PropertyId] =
                    normalizedValue.DeepClone();
                break;
            case ChartPropertyStorage.Placement:
                placement = normalizedValue.GetValue<string>();
                break;
            case ChartPropertyStorage.ZIndex:
                zIndex = normalizedValue.GetValue<int>();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor.Storage),
                    descriptor.Storage,
                    "Unsupported chart property storage.");
        }

        return source with
        {
            Placement = placement,
            ZIndex = zIndex,
            Parameters = parameters,
            Style = style,
            PersistentState = persistentState
        };
    }

    private static JsonNode? NormalizeCurrentValue(
        ChartPropertyDescriptor descriptor)
    {
        if (descriptor.Value is null)
            return null;

        JsonNode? node = JsonSerializer.SerializeToNode(descriptor.Value);
        return NormalizeValue(descriptor, node);
    }

    private static JsonNode NormalizeValue(
        ChartPropertyDescriptor descriptor,
        JsonNode? node)
    {
        if (node is null)
        {
            throw new InvalidOperationException(
                $"Property '{descriptor.PropertyId}' does not accept null.");
        }

        return descriptor.ValueKind switch
        {
            ChartPropertyValueKind.Boolean =>
                JsonValue.Create(ReadBoolean(node, descriptor.PropertyId)),
            ChartPropertyValueKind.Integer =>
                JsonValue.Create(ValidateInteger(
                    ReadInteger(node, descriptor.PropertyId),
                    descriptor)),
            ChartPropertyValueKind.Decimal =>
                JsonValue.Create(ValidateDecimal(
                    ReadDecimal(node, descriptor.PropertyId),
                    descriptor)),
            ChartPropertyValueKind.String or
            ChartPropertyValueKind.Enum or
            ChartPropertyValueKind.Color or
            ChartPropertyValueKind.LineStyle or
            ChartPropertyValueKind.Symbol or
            ChartPropertyValueKind.Timeframe or
            ChartPropertyValueKind.PanelId or
            ChartPropertyValueKind.Formula =>
                JsonValue.Create(ValidateString(
                    ReadString(node, descriptor.PropertyId),
                    descriptor)),
            ChartPropertyValueKind.DateRange or
            ChartPropertyValueKind.Collection =>
                CloneStructuredValue(node, descriptor.PropertyId),
            _ => throw new ArgumentOutOfRangeException(
                nameof(descriptor.ValueKind),
                descriptor.ValueKind,
                "Unsupported chart property value kind.")
        };
    }

    private static bool ReadBoolean(JsonNode node, string propertyId)
    {
        if (node is JsonValue value && value.TryGetValue(out bool result))
            return result;

        throw new InvalidOperationException(
            $"Property '{propertyId}' requires a boolean value.");
    }

    private static int ReadInteger(JsonNode node, string propertyId)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out int intValue))
                return intValue;
            if (value.TryGetValue(out long longValue) &&
                longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                return (int)longValue;
            }
        }

        throw new InvalidOperationException(
            $"Property '{propertyId}' requires an integer value.");
    }

    private static double ReadDecimal(JsonNode node, string propertyId)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out double doubleValue))
                return doubleValue;
            if (value.TryGetValue(out decimal decimalValue))
                return (double)decimalValue;
            if (value.TryGetValue(out long longValue))
                return longValue;
        }

        throw new InvalidOperationException(
            $"Property '{propertyId}' requires a numeric value.");
    }

    private static string ReadString(JsonNode node, string propertyId)
    {
        if (node is JsonValue value &&
            value.TryGetValue(out string? result) &&
            !string.IsNullOrWhiteSpace(result))
        {
            return result.Trim();
        }

        throw new InvalidOperationException(
            $"Property '{propertyId}' requires a non-empty string value.");
    }

    private static int ValidateInteger(
        int value,
        ChartPropertyDescriptor descriptor)
    {
        ValidateRange(value, descriptor);
        return value;
    }

    private static double ValidateDecimal(
        double value,
        ChartPropertyDescriptor descriptor)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException(
                $"Property '{descriptor.PropertyId}' requires a finite value.");
        }

        ValidateRange(value, descriptor);
        return value;
    }

    private static void ValidateRange(
        double value,
        ChartPropertyDescriptor descriptor)
    {
        if (descriptor.Minimum.HasValue && value < descriptor.Minimum.Value)
        {
            throw new InvalidOperationException(
                $"Property '{descriptor.PropertyId}' must be at least " +
                $"{descriptor.Minimum.Value}.");
        }
        if (descriptor.Maximum.HasValue && value > descriptor.Maximum.Value)
        {
            throw new InvalidOperationException(
                $"Property '{descriptor.PropertyId}' must be at most " +
                $"{descriptor.Maximum.Value}.");
        }
    }

    private static string ValidateString(
        string value,
        ChartPropertyDescriptor descriptor)
    {
        if (descriptor.AllowedValues.Count > 0 &&
            !descriptor.AllowedValues.Contains(value, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Property '{descriptor.PropertyId}' does not allow value " +
                $"'{value}'.");
        }

        return value;
    }

    private static JsonNode CloneStructuredValue(
        JsonNode node,
        string propertyId) =>
        node is JsonObject or JsonArray
            ? node.DeepClone()
            : throw new InvalidOperationException(
                $"Property '{propertyId}' requires an object or array value.");

    private static string RequireText(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();
}
