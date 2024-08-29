using YamlDotNet.Serialization;

var modelFileName = "model.yml";
var deserializer = new DeserializerBuilder()
    .IgnoreUnmatchedProperties()
    .Build();
var model = deserializer.Deserialize<Dictionary<String, Dictionary<String, Object>>>(File.ReadAllText(modelFileName));
foreach (var (entityName, properties) in model)
{
    foreach (var (propertyName, propertyValue) in properties)
    {
        Metadata entityMetadata = propertyValue switch
        {
            string s => new Metadata("string", false, null, null),
        };
        
        Console.WriteLine($"{entityName}.{propertyName} is a {entityMetadata.Type} and is {(entityMetadata.IsRequired ? "required" : "optional")}");
    }
}

sealed record Metadata(String Type, Boolean IsRequired, UInt32? MinLength, UInt32? MaxLength);