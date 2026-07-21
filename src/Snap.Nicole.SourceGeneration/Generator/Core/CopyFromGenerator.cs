using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Snap.Nicole.SourceGeneration.Extension;
using Snap.Nicole.SourceGeneration.Model;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Snap.Nicole.SourceGeneration.Generator.WellKnownSyntax;
using static Snap.Nicole.SourceGeneration.Primitive.FastSyntaxFactory;

namespace Snap.Nicole.SourceGeneration.Generator.Core;

[Generator(LanguageNames.CSharp)]
internal sealed class CopyFromGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<CopyFromGeneratorContext> provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                WellKnownMetadataNames.GeneratedCopyFromAttributeT,
                static (node, token) => node is TypeDeclarationSyntax,
                CopyFromGeneratorContext.Create)
            .Where(static context => context is not null)
            .Select(static (context, token) => context!);

        context.RegisterSourceOutput(provider, GenerateWrapper);
    }

    private static void GenerateWrapper(SourceProductionContext production, CopyFromGeneratorContext context)
    {
        try
        {
            Generate(production, context);
        }
        catch (Exception e)
        {
            production.AddSource($"Error-{Guid.NewGuid()}.g.cs", e.ToString());
        }
    }

    private static void Generate(SourceProductionContext production, CopyFromGeneratorContext context)
    {
        CompilationUnitSyntax syntax = context.Hierarchy
            .GetCompilationUnit([.. GenerateCopyFromMethods(context)], GenerateCopyFromBaseList(context))
            .NormalizeWhitespace();

        production.AddSource(context.Hierarchy.FileNameHint, syntax.ToFullStringWithHeader());
    }

    private static BaseListSyntax GenerateCopyFromBaseList(CopyFromGeneratorContext context)
    {
        return BaseList(SeparatedList(context.Sources.Select(static source => (BaseTypeSyntax)SimpleBaseType(QualifiedName(
            ParseName("global::Snap.Nicole.Core"),
            GenericName(Identifier("ICopyFrom"))
                .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(
                    ParseTypeName(source.SourceTypeFullyQualifiedNameWithNullabilityAnnotations)))))))));
    }

    private static IEnumerable<MethodDeclarationSyntax> GenerateCopyFromMethods(CopyFromGeneratorContext context)
    {
        foreach (CopyFromSourceInfo source in context.Sources)
        {
            yield return GenerateCopyFromMethod(source);
        }
    }

    private static MethodDeclarationSyntax GenerateCopyFromMethod(CopyFromSourceInfo source)
    {
        SyntaxToken sourceParameter = Identifier("source");
        IdentifierNameSyntax sourceParameterName = IdentifierName(sourceParameter);

        ImmutableArray<StatementSyntax>.Builder statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        statements.Add(ExpressionStatement(ArgumentNullExceptionThrowIfNull(sourceParameterName)));

        foreach (CopyFromPropertyInfo property in source.Properties)
        {
            ExpressionSyntax targetExpression = SimpleMemberAccessExpression(ThisExpression(), IdentifierName(property.TargetName));
            ExpressionSyntax sourceExpression = SimpleMemberAccessExpression(sourceParameterName, IdentifierName(property.SourceName));
            statements.Add(ExpressionStatement(property.Strategy switch
            {
                CopyFromPropertyStrategy.Assignment => SimpleAssignmentExpression(targetExpression, sourceExpression),
                CopyFromPropertyStrategy.CopyFrom => InvocationExpression(SimpleMemberAccessExpression(targetExpression, IdentifierName("CopyFrom")))
                    .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(sourceExpression)))),
                _ => throw new InvalidOperationException("Unsupported copy-from property strategy: " + property.Strategy)
            }));
        }

        return MethodDeclaration(VoidType, Identifier("CopyFrom"))
            .WithModifiers(PublicTokenList)
            .WithParameterList(ParameterList(SingletonSeparatedList(
                Parameter(ParseTypeName(source.SourceTypeFullyQualifiedNameWithNullabilityAnnotations), sourceParameter))))
            .WithBody(Block(List(statements)));
    }

    private sealed record CopyFromGeneratorContext
    {
        public required HierarchyInfo Hierarchy { get; init; }

        public required EquatableArray<CopyFromSourceInfo> Sources { get; init; }

        public static CopyFromGeneratorContext? Create(GeneratorAttributeSyntaxContext context, CancellationToken token)
        {
            if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            {
                return null;
            }

            if (typeSymbol.TypeKind is not TypeKind.Class and not TypeKind.Struct)
            {
                return null;
            }

            EquatableArray<CopyFromSourceInfo> sources = GetSources(typeSymbol, context.Attributes, context.SemanticModel.Compilation, token);
            if (sources.IsEmpty)
            {
                return null;
            }

            return new()
            {
                Hierarchy = HierarchyInfo.Create(typeSymbol),
                Sources = sources,
            };
        }

        private static EquatableArray<CopyFromSourceInfo> GetSources(INamedTypeSymbol targetType, ImmutableArray<AttributeData> attributes, Compilation compilation, CancellationToken token)
        {
            HashSet<string> sourceTypeNames = [with(StringComparer.Ordinal)];
            ImmutableArray<CopyFromSourceInfo>.Builder builder = ImmutableArray.CreateBuilder<CopyFromSourceInfo>();

            foreach (AttributeData attribute in attributes)
            {
                token.ThrowIfCancellationRequested();

                if (attribute.AttributeClass is not { TypeArguments.Length: 1 } attributeClass ||
                    !attributeClass.HasFullyQualifiedMetadataName(WellKnownMetadataNames.GeneratedCopyFromAttributeT))
                {
                    continue;
                }

                ITypeSymbol sourceType = attributeClass.TypeArguments[0];
                string sourceTypeFullyQualifiedNameWithNullabilityAnnotations = sourceType.GetFullyQualifiedNameWithNullabilityAnnotations();
                if (!sourceTypeNames.Add(sourceTypeFullyQualifiedNameWithNullabilityAnnotations))
                {
                    continue;
                }

                builder.Add(new()
                {
                    SourceTypeFullyQualifiedNameWithNullabilityAnnotations = sourceTypeFullyQualifiedNameWithNullabilityAnnotations,
                    Properties = GetProperties(targetType, sourceType, compilation, token),
                });
            }

            return builder.ToImmutable();
        }

        private static EquatableArray<CopyFromPropertyInfo> GetProperties(INamedTypeSymbol targetType, ITypeSymbol sourceType, Compilation compilation, CancellationToken token)
        {
            Dictionary<string, IPropertySymbol> sourceProperties = GetSourceProperties(sourceType, token);
            ImmutableArray<CopyFromPropertyInfo>.Builder builder = ImmutableArray.CreateBuilder<CopyFromPropertyInfo>();

            foreach (IPropertySymbol targetProperty in targetType.GetMembers().OfType<IPropertySymbol>())
            {
                token.ThrowIfCancellationRequested();

                if (!sourceProperties.TryGetValue(targetProperty.Name, out IPropertySymbol? sourceProperty))
                {
                    continue;
                }

                if (!TryGetPropertyStrategy(targetProperty, sourceProperty, compilation, out CopyFromPropertyStrategy strategy))
                {
                    continue;
                }

                builder.Add(new()
                {
                    TargetName = targetProperty.Name,
                    SourceName = sourceProperty.Name,
                    Strategy = strategy,
                });
            }

            return builder.ToImmutable();
        }

        private static Dictionary<string, IPropertySymbol> GetSourceProperties(ITypeSymbol sourceType, CancellationToken token)
        {
            Dictionary<string, IPropertySymbol> result = [with(StringComparer.Ordinal)];

            foreach (IPropertySymbol property in sourceType.GetMembers().OfType<IPropertySymbol>())
            {
                token.ThrowIfCancellationRequested();

                if (!CanReadSourceProperty(property))
                {
                    continue;
                }

                if (!result.ContainsKey(property.Name))
                {
                    result.Add(property.Name, property);
                }
            }

            return result;
        }

        private static bool CanWriteTargetProperty(IPropertySymbol property)
        {
            return !property.IsStatic
                && !property.IsIndexer
                && property.SetMethod is { IsInitOnly: false };
        }

        private static bool CanReadTargetProperty(IPropertySymbol property)
        {
            return !property.IsStatic
                && !property.IsIndexer
                && property.GetMethod is not null;
        }

        private static bool CanReadSourceProperty(IPropertySymbol property)
        {
            return !property.IsStatic
                && !property.IsIndexer
                && property.GetMethod is not null
                && property.GetMethod.DeclaredAccessibility is Accessibility.Public;
        }

        private static bool TryGetPropertyStrategy(IPropertySymbol targetProperty, IPropertySymbol sourceProperty, Compilation compilation, out CopyFromPropertyStrategy strategy)
        {
            if (CanReadTargetProperty(targetProperty) && targetProperty.Type.IsReferenceType && CanCopyFromProperty(targetProperty.Type, sourceProperty.Type, compilation))
            {
                strategy = CopyFromPropertyStrategy.CopyFrom;
                return true;
            }

            if (CanWriteTargetProperty(targetProperty) && compilation.ClassifyConversion(sourceProperty.Type, targetProperty.Type).IsImplicit)
            {
                strategy = CopyFromPropertyStrategy.Assignment;
                return true;
            }

            strategy = default;
            return false;
        }

        private static bool CanCopyFromProperty(ITypeSymbol targetType, ITypeSymbol sourceType, Compilation compilation)
        {
            if (targetType is INamedTypeSymbol targetNamedType && CanCopyFromInterface(targetNamedType, sourceType, compilation))
            {
                return true;
            }

            foreach (INamedTypeSymbol interfaceType in targetType.AllInterfaces)
            {
                if (CanCopyFromInterface(interfaceType, sourceType, compilation))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanCopyFromInterface(INamedTypeSymbol interfaceType, ITypeSymbol sourceType, Compilation compilation)
        {
            return interfaceType.TypeArguments.Length is 1
                && interfaceType.HasFullyQualifiedMetadataName(WellKnownMetadataNames.ICopyFromT)
                && compilation.ClassifyConversion(sourceType, interfaceType.TypeArguments[0]).IsImplicit;
        }
    }

    private sealed record CopyFromSourceInfo
    {
        public required string SourceTypeFullyQualifiedNameWithNullabilityAnnotations { get; init; }

        public required EquatableArray<CopyFromPropertyInfo> Properties { get; init; }
    }

    private sealed record CopyFromPropertyInfo
    {
        public required string TargetName { get; init; }

        public required string SourceName { get; init; }

        public required CopyFromPropertyStrategy Strategy { get; init; }
    }

    private enum CopyFromPropertyStrategy
    {
        Assignment,
        CopyFrom,
    }
}
