using System.Collections;
using System.Reflection;

const string baseTypeName = "ChartKit.Indicators.IncrementalIndicatorBase";
Assembly assembly = typeof(ChartKit.Core.IndicatorEngine).Assembly;
Type baseType = assembly.GetType(baseTypeName, throwOnError: true)!;

Type[] indicatorTypes = assembly.GetTypes()
    .Where(type => type.IsClass && !type.IsAbstract && baseType.IsAssignableFrom(type))
    .OrderBy(type => type.FullName, StringComparer.Ordinal)
    .ToArray();

Console.WriteLine($"legacy_indicator_count={indicatorTypes.Length}");
foreach (Type type in indicatorTypes)
{
    object instance = CreateDefault(type);
    PropertyInfo? nameProperty = type.GetProperty("Name");
    PropertyInfo? displayNameProperty = type.GetProperty("DisplayName");
    PropertyInfo? panelProperty = type.GetProperty("PanelIndex");
    PropertyInfo? parametersProperty = type.GetProperty("Parameters");

    Console.WriteLine($"indicator_type={type.FullName}");
    Console.WriteLine($"indicator_name={nameProperty?.GetValue(instance)}");
    Console.WriteLine($"indicator_display_name={displayNameProperty?.GetValue(instance)}");
    Console.WriteLine($"indicator_panel={panelProperty?.GetValue(instance)}");

    if (parametersProperty?.GetValue(instance) is IEnumerable parameters)
    {
        foreach ((object? Key, object? Value) entry in EnumeratePairs(parameters)
                     .OrderBy(entry => entry.Key?.ToString(), StringComparer.Ordinal))
        {
            string valueType = entry.Value?.GetType().FullName ?? "null";
            Console.WriteLine($"parameter={entry.Key}|{entry.Value}|{valueType}");
        }
    }

    object candles = CreateCandles(assembly);
    MethodInfo calculate = type.GetMethod("Calculate")
        ?? throw new InvalidOperationException($"Calculate missing: {type.FullName}");
    if (calculate.Invoke(instance, new[] { candles }) is IEnumerable results)
    {
        object? last = results.Cast<object>().LastOrDefault();
        if (last is not null)
        {
            PropertyInfo? valuesProperty = last.GetType().GetProperty("Values");
            if (valuesProperty?.GetValue(last) is IEnumerable values)
            {
                Console.WriteLine("result_keys=" + string.Join("|", EnumeratePairs(values)
                    .Select(pair => pair.Key?.ToString())
                    .OrderBy(key => key, StringComparer.Ordinal)));
            }
        }
    }
    Console.WriteLine("indicator_end");
}
Console.WriteLine("legacy_inventory=PASS");
return 0;

static IEnumerable<(object? Key, object? Value)> EnumeratePairs(IEnumerable source)
{
    foreach (object item in source)
    {
        Type pairType = item.GetType();
        PropertyInfo key = pairType.GetProperty("Key")
            ?? throw new InvalidOperationException($"Dictionary item has no Key: {pairType.FullName}");
        PropertyInfo value = pairType.GetProperty("Value")
            ?? throw new InvalidOperationException($"Dictionary item has no Value: {pairType.FullName}");
        yield return (key.GetValue(item), value.GetValue(item));
    }
}

static object CreateDefault(Type type)
{
    foreach (ConstructorInfo constructor in type.GetConstructors()
                 .OrderBy(constructor => constructor.GetParameters().Length))
    {
        ParameterInfo[] parameters = constructor.GetParameters();
        if (parameters.Any(parameter => !parameter.IsOptional && !parameter.HasDefaultValue))
        {
            continue;
        }

        object?[] arguments = parameters.Select(parameter =>
        {
            object? value = parameter.DefaultValue;
            if (value is null || value is DBNull || value == Missing.Value)
            {
                return parameter.ParameterType.IsValueType
                    ? Activator.CreateInstance(parameter.ParameterType)
                    : null;
            }
            return value;
        }).ToArray();
        return constructor.Invoke(arguments);
    }
    throw new InvalidOperationException($"No optional constructor: {type.FullName}");
}

static object CreateCandles(Assembly assembly)
{
    Type candleType = assembly.GetType("ChartKit.Models.CandleItem", throwOnError: true)!;
    Type listType = typeof(List<>).MakeGenericType(candleType);
    IList list = (IList)Activator.CreateInstance(listType)!;
    DateTime start = new(2026, 7, 30, 9, 0, 0, DateTimeKind.Unspecified);
    float previous = 1000f;

    for (int index = 0; index < 96; index++)
    {
        float close = 1000f + index * 0.18f + (float)Math.Sin(index / 4d) * 4.5f;
        object candle = Activator.CreateInstance(candleType)!;
        Set(candle, "Dt", start.AddMinutes(index));
        Set(candle, "Sequence", (long)index);
        Set(candle, "OpenTime", start.AddMinutes(index));
        Set(candle, "CloseTime", start.AddMinutes(index + 1));
        Set(candle, "IsFinal", true);
        Set(candle, "Open", previous);
        Set(candle, "High", Math.Max(previous, close) + 1f);
        Set(candle, "Low", Math.Min(previous, close) - 1f);
        Set(candle, "Close", close);
        Set(candle, "Volume", 1000L + index * 17L);
        list.Add(candle);
        previous = close;
    }
    return list;
}

static void Set(object target, string propertyName, object value)
{
    PropertyInfo property = target.GetType().GetProperty(propertyName)
        ?? throw new InvalidOperationException($"Property missing: {propertyName}");
    property.SetValue(target, value);
}
