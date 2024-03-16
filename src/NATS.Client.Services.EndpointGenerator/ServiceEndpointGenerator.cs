using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NATS.Client.Services.EndpointGenerator;

[Generator]
public class ServiceEndpointGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        // No initialization needed
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var compilation = context.Compilation;
        var symbolsWithEndpointAttribute = GetSymbolsWithEndpointAttribute(compilation);

        if (symbolsWithEndpointAttribute.Any())
        {
            var source = GenerateRegistrationCode(symbolsWithEndpointAttribute);
            context.AddSource("NatsSvcExtensions.g.cs", source);
        }
    }

    private static IEnumerable<IMethodSymbol> GetSymbolsWithEndpointAttribute(Compilation compilation)
    {
        var endpointAttributeSymbol = compilation.GetTypeByMetadataName("ServiceEndpointAttribute");
        var methodSymbols = compilation.SyntaxTrees
            .SelectMany(st => st.GetRoot().DescendantNodes())
            .OfType<MethodDeclarationSyntax>()
            .Select(mds => compilation.GetSemanticModel(mds.SyntaxTree).GetDeclaredSymbol(mds))
            .OfType<IMethodSymbol>()
            .Where(ms => ms.GetAttributes().Any(a => a.AttributeClass != null && a.AttributeClass.Equals(endpointAttributeSymbol, SymbolEqualityComparer.Default)));

        return methodSymbols;
    }

    private static string GenerateRegistrationCode(IEnumerable<IMethodSymbol> methodSymbols)
    {
        var sb = new StringBuilder();
        sb.AppendLine("public static partial class NatsSvcServerExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static async Task RegisterEndpointsAsync(this INatsSvcServer service, CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");

        foreach (var methodSymbol in methodSymbols)
        {
            var endpointAttribute = methodSymbol.GetAttributes().FirstOrDefault(a => a.AttributeClass.Name == "ServiceEndpointAttribute");
            if (endpointAttribute != null)
            {
                var endpointName = (string)endpointAttribute.ConstructorArguments[0].Value;
                var queueGroup = endpointAttribute.ConstructorArguments.Length > 1 ? (string)endpointAttribute.ConstructorArguments[1].Value : null;

                var className = methodSymbol.ContainingType.Name;
                var methodName = methodSymbol.Name;

                sb.AppendLine($"        await service.AddEndpointAsync<{methodSymbol.ReturnType}>({className}.{methodName}, name: \"{endpointName}\", queueGroup: {(queueGroup != null ? $"\"{queueGroup}\"" : "null")}, cancellationToken: cancellationToken);");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
