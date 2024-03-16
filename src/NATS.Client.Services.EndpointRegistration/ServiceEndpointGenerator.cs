using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NATS.Client.Services.EndpointRegistration;

[Generator]
public class ServiceEndpointGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context) { }

    public void Execute(GeneratorExecutionContext context)
    {
        var compilation = context.Compilation;
        var symbolsWithEndpointAttribute = GetSymbolsWithEndpointAttribute(compilation);

        if (symbolsWithEndpointAttribute.Any())
        {
            var source = GenerateRegistrationCode(symbolsWithEndpointAttribute);
            context.AddSource("NatsSvcServerExtensions.Generated.cs", source);
        }
    }

    private static IEnumerable<IMethodSymbol> GetSymbolsWithEndpointAttribute(Compilation compilation)
    {
        var endpointAttributeSymbol = compilation.GetTypeByMetadataName("NATS.Client.Services.ServiceEndpointAttribute");
        if (endpointAttributeSymbol == null) return Enumerable.Empty<IMethodSymbol>();

        return compilation.SyntaxTrees
            .SelectMany(st => st.GetRoot().DescendantNodes())
            .OfType<MethodDeclarationSyntax>()
            .Select(mds => compilation.GetSemanticModel(mds.SyntaxTree).GetDeclaredSymbol(mds))
            .OfType<IMethodSymbol>()
            .Where(ms => ms.GetAttributes().Any(a => a.AttributeClass != null && SymbolEqualityComparer.Default.Equals(a.AttributeClass, endpointAttributeSymbol)));
    }

    private static string GenerateRegistrationCode(IEnumerable<IMethodSymbol> methodSymbols)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using NATS.Client.Services;");
        sb.AppendLine("using NATS.Client.Core;"); // Assuming this is where INatsSvcServer and other relevant types are defined
        sb.AppendLine();
        sb.AppendLine("namespace NATS.Client.Services.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    public class EndpointRegistrarFactory : IEndpointRegistrarFactory");
        sb.AppendLine("    {");
        sb.AppendLine("        public IEndpointRegistrar CreateRegistrar()");
        sb.AppendLine("        {");
        sb.AppendLine("            return new EndpointRegistrar();");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        sb.AppendLine("namespace NATS.Client.Services.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    public class EndpointRegistrar : IEndpointRegistrar");
        sb.AppendLine("    {");
        sb.AppendLine("        public async Task RegisterEndpointsAsync(INatsSvcServer service, CancellationToken cancellationToken = default)");
        sb.AppendLine("        {");


        foreach (var methodSymbol in methodSymbols)
        {
            var endpointAttribute = methodSymbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ServiceEndpointAttribute");
            if (endpointAttribute != null)
            {
                var endpointName = (string)endpointAttribute.ConstructorArguments[0].Value;
                var queueGroup = endpointAttribute.ConstructorArguments.Length > 1 ? (string)endpointAttribute.ConstructorArguments[1].Value : "null";

                var className = methodSymbol.ContainingType.ToDisplayString();
                var methodName = methodSymbol.Name;
                var parameterType = methodSymbol.Parameters.FirstOrDefault()?.Type.ToString();
                var returnType = methodSymbol.ReturnType.ToString();

                // Ensuring async lambda usage and proper exception handling
                sb.AppendLine($"            service.AddEndpointAsync<{parameterType}>(");
                sb.AppendLine($"                name: \"{endpointName}\",");
                sb.AppendLine($"                handler: async m =>");
                sb.AppendLine($"                {{");
                sb.AppendLine($"                    try");
                sb.AppendLine($"                    {{");
                // This assumes your method might want to directly use the message payload (m.Data)
                // Adjust based on your method's expected parameters
                sb.AppendLine($"                        var result = await new {className}().{methodName}(m.Data);");
                sb.AppendLine($"                        await m.ReplyAsync(result);");
                sb.AppendLine($"                    }}");
                sb.AppendLine($"                    catch (System.Exception ex)");
                sb.AppendLine($"                    {{");
                sb.AppendLine($"                        await m.ReplyErrorAsync(500, ex.Message);");
                sb.AppendLine($"                    }}");
                sb.AppendLine($"                }},");
                sb.AppendLine($"                queueGroup: {(queueGroup != "null" ? $"\"{queueGroup}\"" : "null")},");
                sb.AppendLine($"                cancellationToken: cancellationToken);");
            }
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }


}
