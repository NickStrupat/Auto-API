using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using FluentIL;
using HotChocolate.Execution.Configuration;
using Microsoft.EntityFrameworkCore;
using static System.Reflection.BindingFlags;

namespace Api;

public static class QueryTypeBuilderExtensions
{
	public static IRequestExecutorBuilder AddQueryType(
		this IRequestExecutorBuilder builder,
		String name,
		IEnumerable<Type> entityTypes,
		Int32 defaultPageSize = 100,
		Int32 maxPageSize = 1_000)
	{
		return builder.AddQueryType(CreateQueryType(name, entityTypes, defaultPageSize, maxPageSize));
	}

	private static Type CreateQueryType(String name, IEnumerable<Type> entityTypes, Int32 defaultPageSize, Int32 maxPageSize)
	{
		var tb = TypeFactory.Default.NewType(name).Public().Class();
		var attributeBuilders = new
		{
			useOffsetPaging = CreateAttributeBuilder<UseOffsetPagingAttribute>(("IncludeTotalCount", true), ("DefaultPageSize", defaultPageSize), ("MaxPageSize", maxPageSize)),
			useProjection = CreateAttributeBuilder<UseProjectionAttribute>(),
			useFiltering = CreateAttributeBuilder<UseFilteringAttribute>(),
			useSorting = CreateAttributeBuilder<UseSortingAttribute>(),
			useSingleOrDefault = CreateAttributeBuilder<UseSingleOrDefaultAttribute>(),
			idAttribute = CreateAttributeBuilder<IDAttribute>(),
			//service = CreateAttributeBuilder<ServiceAttribute>(),
		};
		var queryBaseWhereMethod = typeof(QueryHelpers).GetMethod(nameof(QueryHelpers.Where), Public | Static) ?? throw new("Method not found");
		var keyType = typeof(EntityBase).GetProperty(nameof(EntityBase.Id))!.PropertyType;
		
		foreach (var entityType in entityTypes)
		{
			var queryMethod = tb.NewMethod(entityType.Name + "Set")
				.Public()
				.Returns(MakeIQueryable(entityType))
				.SetCustomAttribute(attributeBuilders.useOffsetPaging)
				.SetCustomAttribute(attributeBuilders.useProjection)
				.SetCustomAttribute(attributeBuilders.useFiltering)
				.SetCustomAttribute(attributeBuilders.useSorting);

			var parameter = queryMethod.CreateParam<DbContext>("context")
				//.SetCustomAttribute(attributeBuilders.service)
				;

			queryMethod
				.Param(parameter)
				.Body()
				.LdArg1()
				.CallVirt(MakeSet(entityType))
				.Ret();

			var getMethod = tb.NewMethod($"{entityType.Name}ById")
				.Public()
				.Returns(typeof(IQueryable<>).MakeGenericType(entityType))
				.SetCustomAttribute(attributeBuilders.useSingleOrDefault)
				.SetCustomAttribute(attributeBuilders.useProjection);

			getMethod
				.Param(getMethod.CreateParam(keyType, "id").SetCustomAttribute(attributeBuilders.idAttribute))
				.Param(getMethod.CreateParam<DbContext>("context"))
				.Body()
				.LdArg1()
				.LdArg2()
				.Call(queryBaseWhereMethod.MakeGenericMethod(entityType, keyType))
				.Ret();
		}
		return tb.CreateType();

		static MethodInfo MakeSet(Type entityType) => typeof(DbContext).GetMethod("Set", [])!.MakeGenericMethod(entityType);
		static Type MakeIQueryable(Type entityType) => typeof(IQueryable<>).MakeGenericType(entityType);
	}

	private static CustomAttributeBuilder CreateAttributeBuilder<T>(params (String name, Object? value)[] properties) where T : Attribute
	{
		var constructors = typeof(T).GetConstructors();
		var constructor = constructors.OrderByDescending(x => x.GetParameters().Length).First();
		var defaultArgs = constructor.GetParameters().Select(x => x.HasDefaultValue ? x.DefaultValue : null).ToArray();
		var propertyInfos = typeof(T).GetProperties().ToDictionary(x => x.Name, x => x);
		var namedProperties = properties.Select(x => propertyInfos[x.name]).ToArray();
		var propertyValues = properties.Select(x => x.value).ToArray();
		return new(constructor, defaultArgs, namedProperties, propertyValues);
	}
}

public static class MutationTypeBuilderExtensions
{
	public static IRequestExecutorBuilder AddMutationType(
		this IRequestExecutorBuilder builder,
		String name,
		IEnumerable<Type> entityTypes) =>
		builder.AddMutationType(CreateMutationType(name, entityTypes));

	private static Type CreateMutationType(String name, IEnumerable<Type> entityTypes)
	{
		var tb = TypeFactory.Default.NewType(name).Public().Class();
		var attributeBuilders = new
		{
			idAttribute = CreateAttributeBuilder<IDAttribute>(),
		};
		var mutationHelpersAddAsyncMethod = typeof(MutationHelpers).GetMethod(nameof(MutationHelpers.AddAsync), Public | Static) ?? throw new("Method not found");
		var mutationHelpersUpdateAsyncMethod = typeof(MutationHelpers).GetMethod(nameof(MutationHelpers.UpdateAsync), Public | Static) ?? throw new("Method not found");
		var keyType = typeof(EntityBase).GetProperty(nameof(EntityBase.Id))!.PropertyType;
		
		foreach (var entityType in entityTypes)
		{
			var (addEntityType, updateEntityType) = CreateInputTypes(entityType);
			{
				var addMethod = tb.NewMethod("Add" + entityType.Name).Public()
					.Returns(typeof(Task<>).MakeGenericType(entityType));
			
				addMethod
					.Param(addMethod.CreateParam(addEntityType, "new"))
					.Param(addMethod.CreateParam<DbContext>("context"))
					.Param(addMethod.CreateParam<CancellationToken>("ct"))
					.Body()
					.LdArg1()
					.LdArg2()
					.LdArg3()
					.Call(mutationHelpersAddAsyncMethod.MakeGenericMethod(addEntityType, entityType))
					.Ret();
			}
			{
				var updateMethod = tb.NewMethod("Update" + entityType.Name).Public()
					.Returns(typeof(Task<>).MakeGenericType(entityType));

				updateMethod
					.Param(updateMethod.CreateParam(keyType, "id").SetCustomAttribute(attributeBuilders.idAttribute))
					.Param(updateMethod.CreateParam(updateEntityType, "update"))
					.Param(updateMethod.CreateParam<DbContext>("context"))
					.Param(updateMethod.CreateParam<CancellationToken>("ct"))
					.Body()
					.LdArg1()
					.LdArg2()
					.LdArg3()
					.LdArgS(4)
					.Call(mutationHelpersUpdateAsyncMethod.MakeGenericMethod(updateEntityType, entityType))
					.Ret();
			}
		}
		return tb.CreateType();
	}

	private static CustomAttributeBuilder CreateAttributeBuilder<T>(params Type[] defaultParamTypes) where T : Attribute
	{
		var constructors = typeof(T).GetConstructors();
		var constructor = constructors.OrderByDescending(x => x.GetParameters().Length).First();
		var defaultArgs = constructor.GetParameters().Select(x => x.HasDefaultValue ? x.DefaultValue : null).ToArray();
		return new(constructor, defaultArgs);
	}

	private static (Type add, Type update) CreateInputTypes(Type entityType)
	{
		var propertyInfos = entityType.GetProperties(Public | Instance | DeclaredOnly).Where(x => x.SetMethod is { IsPublic: true });
		
		var addTb = TypeFactory.Default.NewType($"Add{entityType.Name}Input").Public().Class();
		var customAttributesData = entityType.GetCustomAttributesData();
		foreach (var data in customAttributesData)
		{
			addTb.SetCustomAttribute(new(
				con: data.Constructor,
				constructorArgs: data.ConstructorArguments.Select(x => x.Value).ToArray(),
				namedProperties: data.NamedArguments.Where(x => !x.IsField).Select(x => x.MemberInfo).OfType<PropertyInfo>().ToArray(),
				propertyValues: data.NamedArguments.Where(x => !x.IsField).Select(x => x.TypedValue.Value).ToArray(),
				namedFields: data.NamedArguments.Where(x => x.IsField).Select(x => x.MemberInfo).OfType<FieldInfo>().ToArray(),
				fieldValues: data.NamedArguments.Where(x => x.IsField).Select(x => x.TypedValue.Value).ToArray()
			));
		}
		
		foreach (var pi in propertyInfos)
			addTb.NewAutoProperty(pi.Name, pi.PropertyType);
		
		var updateTb = TypeFactory.Default.NewType($"Update{entityType.Name}Input").Public().Class();
		foreach (var pi in propertyInfos)
		{
			var optionalOfType = pi.PropertyType.IsValueType
				? typeof(Nullable<>).MakeGenericType(pi.PropertyType)
				: pi.PropertyType;
			var nullableAttributeConstructorInfo = typeof(NullableAttribute).GetConstructor([typeof(Byte[])])!;
			var nullableAttribute = new CustomAttributeBuilder(nullableAttributeConstructorInfo, [new Byte[] {0, 2}]);
			var optionalType = typeof(Optional<>).MakeGenericType(pi.PropertyType);
			var property = updateTb.NewAutoProperty(pi.Name, optionalType);
			property.Define().SetCustomAttribute(nullableAttribute);
		}
		
		return (addTb.CreateType(), updateTb.CreateType());
	}
}

public static class TypeBuilderExtensions
{
	public static IPropertyBuilder NewAutoProperty<T>(this ITypeBuilder tb, String name)
	{
		return tb.NewAutoProperty(name, typeof(T));
	}

	public static IPropertyBuilder NewAutoProperty(this ITypeBuilder tb, String name, Type type)
	{
		var field = tb.NewField($"<{name}>k__BackingField", type).Private();
		return tb.NewProperty(name, type)
			.Getter(m => m.Public().Body().LdArg0().LdFld(field).Ret())
			.Setter(m => m.Public().Body().LdArg0().LdArg1().StFld(field).Ret());
	}
}