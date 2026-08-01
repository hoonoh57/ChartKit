using ChartKit.CSharp.Modules.Abstractions;

namespace ChartKit.CSharp.ModuleHost;

public sealed class ChartModuleRegistry
{
    private readonly Dictionary<string, Registration> _registrations =
        new(StringComparer.Ordinal);

    public IReadOnlyList<ChartModuleDefinition> Definitions =>
        _registrations.Values
            .Select(static registration => registration.Definition)
            .OrderBy(static definition => definition.ModuleId, StringComparer.Ordinal)
            .ToArray();

    public void Register<TModule>()
        where TModule : class, IChartModule, IChartModuleFactory<TModule>
    {
        ChartModuleDefinition definition = TModule.Definition;
        if (definition is null)
            throw new InvalidOperationException(
                $"{typeof(TModule).FullName} returned a null module definition.");

        RegisterCore(
            definition,
            static instanceId => TModule.Create(instanceId));
    }

    public bool Contains(string moduleId)
    {
        string normalized = RequireText(moduleId, nameof(moduleId));
        return _registrations.ContainsKey(normalized);
    }

    public bool TryGetDefinition(
        string moduleId,
        out ChartModuleDefinition? definition)
    {
        string normalized = RequireText(moduleId, nameof(moduleId));
        if (_registrations.TryGetValue(normalized, out Registration? registration))
        {
            definition = registration.Definition;
            return true;
        }

        definition = null;
        return false;
    }

    public IChartModule Create(string moduleId, string instanceId)
    {
        string normalizedModuleId = RequireText(moduleId, nameof(moduleId));
        string normalizedInstanceId = RequireText(instanceId, nameof(instanceId));

        if (!_registrations.TryGetValue(
                normalizedModuleId,
                out Registration? registration))
        {
            throw new KeyNotFoundException(
                $"Chart module is not registered: {normalizedModuleId}");
        }

        IChartModule module = registration.Factory(normalizedInstanceId) ??
            throw new InvalidOperationException(
                $"Module factory returned null: {normalizedModuleId}");

        if (!StringComparer.Ordinal.Equals(
                module.InstanceId,
                normalizedInstanceId))
        {
            throw new InvalidOperationException(
                $"Module factory returned instance '{module.InstanceId}' " +
                $"instead of '{normalizedInstanceId}'.");
        }

        if (!ReferenceEquals(module.ModuleDefinition, registration.Definition))
        {
            throw new InvalidOperationException(
                $"{module.GetType().FullName} must expose its registered static " +
                "ChartModuleDefinition instance through ModuleDefinition.");
        }

        return module;
    }

    private void RegisterCore(
        ChartModuleDefinition definition,
        Func<string, IChartModule> factory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_registrations.TryAdd(
                definition.ModuleId,
                new Registration(definition, factory)))
        {
            throw new InvalidOperationException(
                $"Duplicate chart module id: {definition.ModuleId}");
        }
    }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be blank.", parameterName)
            : value.Trim();

    private sealed record Registration(
        ChartModuleDefinition Definition,
        Func<string, IChartModule> Factory);
}
