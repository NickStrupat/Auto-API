using System.Reflection;
using static System.Reflection.BindingFlags;

public static class Extensions
{
    public static void CopySharedPropertiesTo(this Object source, Object destination)
    {
        var sourceProperties = source.GetType().GetProperties(Public | Instance).Where(x => x.GetMethod is not null);
        var destinationProperties = destination.GetType().GetProperties(Public | Instance).Where(x => x.SetMethod is not null);
        var properties = sourceProperties.Join(destinationProperties, x => (x.Name, x.PropertyType), x => (x.Name, x.PropertyType), (x, y) => (Source: x, Destination: y));
        foreach (var property in properties)
            property.Destination.SetValue(destination, property.Source.GetValue(source));
    }

    private static Boolean HasPublicGetAndSet(PropertyInfo x) => x.GetMethod?.IsPublic == true && x.SetMethod?.IsPublic == true;

    public static void ApplyPropertyChangesTo(this Object source, Object destination)
    {
        var sourceProperties =
            from prop in source.GetType().GetProperties()
            let optionalInfo = prop.GetOptionalInfo()
            where optionalInfo.isOptional
            let optionalType = optionalInfo.optionOfType
            let underlyingType = Nullable.GetUnderlyingType(optionalType) ?? optionalType
            let isNullableValueType = underlyingType.IsValueType && Nullable.GetUnderlyingType(underlyingType) != null
            let isNullableReferenceType = new NullabilityInfoContext().Create(prop).GenericTypeArguments[0].WriteState == NullabilityState.Nullable
            select (
                PropertyInfo: prop,
                Name: prop.Name,
                UnderlyingType: underlyingType,
                IsNullable: isNullableValueType || isNullableReferenceType
            );

        var destinationProperties =
            from prop in destination.GetType().GetProperties()
            where HasPublicGetAndSet(prop)
            let nullabilityInfo = prop.GetNullabilityInfo()
            select (
                PropertyInfo: prop,
                Name: prop.Name,
                UnderlyingType: nullabilityInfo.underlyingType,
                IsNullable: nullabilityInfo.isNullable
            );
        var joinedProperties = sourceProperties.Join(destinationProperties, x => (x.Name, x.UnderlyingType), x => (x.Name, x.UnderlyingType), (x, y) => (Source: x, Destination: y));
        foreach (var (src, dest) in joinedProperties)
        {
            var value = (IOptional)src.PropertyInfo.GetValue(source)!;
            if (!value.HasValue)
                continue;
            if (value.Value == null && !dest.IsNullable)
                throw new InvalidOperationException($"Cannot assign null to non-nullable property: {dest.Name}");
            dest.PropertyInfo.SetValue(destination, value.Value);
        }
    }
        
    private static (Boolean isOptional, Type optionOfType) GetOptionalInfo(this PropertyInfo prop)
    {
        if (prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() == typeof(Optional<>))
            return (true, prop.PropertyType.GetGenericArguments().Single());
        return (false, default!);
    }
    
    private static (Boolean isNullable, Type underlyingType) GetNullabilityInfo(this PropertyInfo prop)
    {
        var underlyingType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        var isNullableValueType = prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) != null;
        var isNullableReferenceType = new NullabilityInfoContext().Create(prop).WriteState == NullabilityState.Nullable;
        return (isNullableValueType || isNullableReferenceType, underlyingType);
    }
}