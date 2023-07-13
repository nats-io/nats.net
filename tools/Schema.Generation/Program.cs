using System.Text.RegularExpressions;
using NJsonSchema;
using NJsonSchema.CodeGeneration;
using NJsonSchema.CodeGeneration.CSharp;

var enumNameGenerator = new ProperCaseEnumNameGenerator();
var propertyNameGenerator = new ProperCasePropertyNameGenerator();
var typeNameGenerator = new ProperCaseTypeNameGenerator();

var generatorSettings = new CSharpGeneratorSettings
{
    JsonLibrary = CSharpJsonLibrary.SystemTextJson,
    GenerateNativeRecords = true,
    Namespace = "NATS.Client.JetStream.Models",
    GenerateNullableReferenceTypes = true,
    EnumNameGenerator = enumNameGenerator,
    TypeNameGenerator = typeNameGenerator,
    PropertyNameGenerator = propertyNameGenerator,
};

var slnDirInfo = new DirectoryInfo(Directory.GetCurrentDirectory());
while (!File.Exists(Path.Combine(slnDirInfo.FullName, "NATS.Client.sln")))
{
    if (slnDirInfo.Parent == default)
        throw new Exception("could not locate solution root");

    slnDirInfo = new DirectoryInfo(slnDirInfo.Parent.FullName);
}

var slnDir = slnDirInfo.FullName;
var schemaProjDir = Path.Combine(slnDir, "tools", "Schema.Generation");
var jsProjDir = Path.Combine(slnDir, "src", "NATS.Client.JetStream");

var jsDefinition = Path.Combine(schemaProjDir, "schema", "jetstream.api.v1.json");
var jsModels = Path.Combine(jsProjDir, "Models");

// start with definitions
var schema = await JsonSchema.FromFileAsync(jsDefinition);

// create an "Unknown" type containing all of the definitions at the root level
// so that all definitions will be created
foreach (var (typeName, typeDef) in schema.Definitions)
{
    // default behavior is to allow additional properties
    // but if no additional property schema is defined we don't want them
    if (typeDef.AdditionalPropertiesSchema == default)
        typeDef.AllowAdditionalProperties = false;

    schema.Properties[typeName] = new JsonSchemaProperty { Reference = typeDef, };
}

// generate
var generator = new CSharpGenerator(schema, generatorSettings);
generator.GenerateFile();

var removeGeneratedCodeLine = new Regex(@"^\[System.CodeDom.Compiler.GeneratedCode.*$", RegexOptions.Multiline);
var removeExtraLineAfterAnnotation = new Regex(@"(\[System.Text.Json.Serialization.JsonPropertyName\(""\w+""\)])\n\n");

// loop through each type and write a model
foreach (var type in generator.GenerateTypes())
{
    if (type.TypeName == "Unknown")
        continue;

    var code = type.Code;
    code = removeGeneratedCodeLine.Replace(code, string.Empty);
    code = removeExtraLineAfterAnnotation.Replace(code, "$1\n");
    code = code.Replace("public partial record", "public record");
    code = $"namespace {generatorSettings.Namespace};\n\n{code}";

    var fileName = $"{type.TypeName}.cs";
    Console.WriteLine($"writing {fileName}");
    await File.WriteAllTextAsync(Path.Combine(jsModels, fileName), code);
}

public static class Utils
{
    public static string ProperCase(string str) =>
        string.IsNullOrEmpty(str)
            ? str
            : string.Concat(str.Split("_").Select(s => string.Concat(s[0].ToString().ToUpper(), s.AsSpan(1))));
}

public class ProperCaseEnumNameGenerator : IEnumNameGenerator
{
    public string Generate(int index, string name, object value, JsonSchema schema) => Utils.ProperCase(name);
}

public class ProperCasePropertyNameGenerator : IPropertyNameGenerator
{
    public string Generate(JsonSchemaProperty property) => Utils.ProperCase(property.Name);
}

public class ProperCaseTypeNameGenerator : ITypeNameGenerator
{
    public string Generate(JsonSchema schema, string typeNameHint, IEnumerable<string> reservedTypeNames) => string.IsNullOrEmpty(typeNameHint) ? "Unknown" : Utils.ProperCase(typeNameHint);
}
