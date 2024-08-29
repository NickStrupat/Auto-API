using System.Collections.Immutable;using System.ComponentModel.DataAnnotations.Schema;
using Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Ct = System.Threading.CancellationToken;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGraphQLServer()
    .RegisterDbContext<DbContext>()
    .AddQueryType("Query", EntityBase.DerivedTypes)
    .AddMutationType("Mutation", EntityBase.DerivedTypes)
    // .AddQueryType<Query>()
    // .AddMutationType<Mutation>()
    .AddProjections()
    .AddFiltering()
    .AddSorting()
    ;

void ModelAction(ModelBuilder mb) => mb.AddEntitiesThatAreSubclassesOf<EntityBase>();

builder.Services.AddDbContext(
    ob => ob.UseSqlite(SqliteInMemoryConnection.Shared), 
    ModelAction,
    cb => cb.Properties<Enum>().HaveConversion<String>()
);

var app = builder.Build();
using (var scope = app.Services.CreateScope())
using (var context = scope.ServiceProvider.GetRequiredService<DbContext>())
{
    context.Database.EnsureCreated();
    context.AddRange(
        new Thing { Name = "Thing 1", Description = "Description 1" },
        new Thing { Name = "Thing 2", Description = "Description 2" }
    );
    context.SaveChanges();
}

app.MapGraphQL();
app.UseHttpsRedirection();
app.Run();

sealed class SqliteInMemoryConnection : SqliteConnection
{
    public static SqliteInMemoryConnection Shared { get; } = new();
    public SqliteInMemoryConnection() : base("Data Source=:memory:") => Open();
}

public class UpdateThingInput// : IReadOnlyThing
{
    public Int64 Id { get; set; }
    public Optional<String?> Name { get; set; }
    public Optional<String?> Description { get; set; }
    public Optional<Int32?> Count { get; set; }
    
    public Optional<List<UpdateThingInput_OtherThingInput>?> OtherThings { get; set; } = default!;
}

public class UpdateThingInput_OtherThingInput
{
    public Int64 Id { get; set; }
    public Optional<DateOnly?> CreationDate { get; set; }
}

public class AddThingInput : IThing
{
    public String Name { get; set; } = default!;
    public String Description { get; set; } = default!;
    public Int32 Count { get; set; }
    
    public List<AddThingInput_OtherThingInput> OtherThings { get; set; } = default!;
}

public class AddThingInput_OtherThingInput
{
    public DateOnly CreationDate { get; set; }
}

public interface IReadOnlyThing
{
    String Name { get; }
    String Description { get; }
    Int32 Count { get; }
}

public interface IThing : IReadOnlyThing
{
    new String Name { get; set; }
    new String Description { get; set; }
    new Int32 Count { get; set; }
    
    String IReadOnlyThing.Name => Name;
    String IReadOnlyThing.Description => Description;
    Int32 IReadOnlyThing.Count => Count;
}

public class Thing : EntityBase, IThing
{
    public String Name { get; set; } = default!;
    public String Description { get; set; } = default!;
    public Int32 Count { get; set; }
    
    [UseFiltering, UseSorting]
    public List<OtherThing> OtherThings { get; set; } = default!;
}

public class OtherThing : EntityBase
{
    public DateOnly CreationDate { get; set; }
    
    public Thing Thing { get; set; } = default!;
}

public abstract class EntityBase
{
    //[GraphQLType<IdType>]
    public Int64 Id { get; set; }
    // public Audit<String> Created { get; private set; } = new(DateTime.UtcNow, "System");
    // public Audit<String>? Updated { get; private set; }

    public static readonly ImmutableList<Type> DerivedTypes =
        typeof(EntityBase).Assembly.GetTypes().Where(x => x.IsSubclassOf(typeof(EntityBase))).ToImmutableList();
}

[ComplexType]
public sealed class Audit<TUserId>(DateTime at, TUserId by)
{
    public DateTime At { get; } = at.Kind == DateTimeKind.Utc ? at : throw new ArgumentException("DateTime must be in UTC", nameof(at));
    public TUserId By { get; } = by;
}

public sealed class Query
{
    [UseOffsetPaging(IncludeTotalCount = true, DefaultPageSize = 100, MaxPageSize = 1000)]
    [UseProjection, UseFiltering, UseSorting]
    public IQueryable<Thing> ThingSet(DbContext context) => context.Set<Thing>();
    
    [UseSingleOrDefault, UseProjection]
    public IQueryable<Thing> GetThing([ID] Int64 id, DbContext context) => QueryHelpers.Where<Thing, Int64>(id, context);
}

public static class QueryHelpers
{
    public static IQueryable<T> Where<T, TKey>(TKey id, DbContext context) where T : EntityBase =>
        context.Set<T>().Where(x => x.Id.Equals(id));
}

public sealed class Mutation
{
    public Task<Thing> AddThing(AddThingInput addThing, DbContext context, Ct ct) =>
        MutationHelpers.AddAsync<AddThingInput, Thing>(addThing, context, ct);
    
    public Task<Thing> UpdateThing([ID] Int64 id, UpdateThingInput thing, DbContext context, Ct ct) =>
        MutationHelpers.UpdateAsync<UpdateThingInput, Thing>(id, thing, context, ct);
}

public static class MutationHelpers
{
    public static async Task<TEntity> AddAsync<TInput, TEntity>(TInput input, DbContext context, Ct ct)
        where TEntity : EntityBase, new()
        where TInput : notnull
    {
        var entity = new TEntity();
        input.CopySharedPropertiesTo(entity);
        context.Add(entity);
        await context.SaveChangesAsync(ct);
        return entity;
    }
    
    public static async Task<TEntity> UpdateAsync<TInput, TEntity>(Int64 id, TInput thing, DbContext context, Ct ct)
        where TEntity : EntityBase, new()
        where TInput : notnull
    {
        var entity = await context.Set<TEntity>().SingleAsync(x => x.Id.Equals(id), ct);
        thing.ApplyPropertyChangesTo(entity);
        await context.SaveChangesAsync(ct);
        return entity;
    }
}