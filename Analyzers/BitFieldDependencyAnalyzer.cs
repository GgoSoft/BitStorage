using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace GgoSoft.Storage.Analyzers
{
	[DiagnosticAnalyzer(LanguageNames.CSharp)]
	public class BitFieldDependencyAnalyzer : DiagnosticAnalyzer
	{
		private static readonly DiagnosticDescriptor MissingGlobalRule = new DiagnosticDescriptor(
			"BIT001",
			"Missing BitFieldAttribute",
			"BitFieldLevelAttribute requires BitFieldAttribute to be present on this member",
			"Design",
			DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor BackwardsDepthRule = new DiagnosticDescriptor(
			"BIT002",
			"Invalid Depth Order",
			"Depth {0} moves backwards or is duplicated, attribute depths must strictly move forward",
			"Design",
			DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static readonly DiagnosticDescriptor DepthExceedsTypeRule = new DiagnosticDescriptor(
			"BIT003",
			"Depth Exceeds Type Capability",
			"Resolved depth {0} is invalid, the type '{1}' only supports a maximum level depth of {2}",
			"Design",
			DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
			ImmutableArray.Create(MissingGlobalRule, BackwardsDepthRule, DepthExceedsTypeRule);

		public override void Initialize(AnalysisContext context)
		{
			context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
			context.EnableConcurrentExecution();
			context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.Field, SymbolKind.Property);
		}

		private static void AnalyzeSymbol(SymbolAnalysisContext context)
		{
			var allAttributes = context.Symbol.GetAttributes();
			if (allAttributes.Length == 0) return;

			// Bulletproof attribute name detection
			bool hasGlobal = allAttributes.Any(a =>
				a.AttributeClass?.Name == "BitFieldAttribute");

			var levelAttrs = allAttributes.Where(a =>
				a.AttributeClass?.Name == "BitFieldLevelAttribute").ToList();

			if (levelAttrs.Count == 0) return;

			// Rule 1: Validate BitFieldAttribute dependency
			if (!hasGlobal)
			{
				context.ReportDiagnostic(Diagnostic.Create(MissingGlobalRule, context.Symbol.Locations.FirstOrDefault()));
				return;
			}

			int currentWatermark = -1;
			int maxAllowedDepth = CalculateTypeMaxDepth(context.Symbol);

			foreach (var attr in levelAttrs)
			{
				int? explicitDepth = null;

				// Look for named assignment [BitFieldLevel(Depth = 2)]
				if (attr.NamedArguments.Length > 0)
				{
					var match = attr.NamedArguments.FirstOrDefault(x => x.Key == "Depth");
					if (match.Key == "Depth" && match.Value.Value is int namedValue)
					{
						explicitDepth = namedValue;
					}
				}

				int resolvedDepth;

				if (explicitDepth.HasValue)
				{
					resolvedDepth = explicitDepth.Value;

					// Rule 2: Enforce strict forward-only positioning
					if (resolvedDepth <= currentWatermark)
					{
						var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
									   ?? context.Symbol.Locations.FirstOrDefault();

						context.ReportDiagnostic(Diagnostic.Create(BackwardsDepthRule, location, resolvedDepth));
						return;
					}
				}
				else
				{
					// Implicit positional allocation takes the next consecutive index slot
					resolvedDepth = currentWatermark + 1;
				}

				// Rule 3: Enforce that depth slot does not overshoot type collection geometry
				if (resolvedDepth > maxAllowedDepth)
				{
					var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
								   ?? context.Symbol.Locations.FirstOrDefault();

					string typeName = GetSymbolTypeName(context.Symbol);
					context.ReportDiagnostic(Diagnostic.Create(DepthExceedsTypeRule, location, resolvedDepth, typeName, maxAllowedDepth));
					return;
				}

				currentWatermark = resolvedDepth;
			}
		}

		/// <summary>
		/// Recursively unrolls a Type Symbol to calculate how many levels deep it goes.
		/// e.g., int = 0, List<int> = 1, List<List<int>> = 2, int[][] = 2
		/// </summary>
		private static int CalculateTypeMaxDepth(ISymbol symbol)
		{
			ITypeSymbol typeSymbol = null;

			if (symbol is IPropertySymbol propertySymbol) typeSymbol = propertySymbol.Type;
			else if (symbol is IFieldSymbol fieldSymbol) typeSymbol = fieldSymbol.Type;

			if (typeSymbol == null) return 0;

			int depth = 0;
			while (typeSymbol != null)
			{
				// 1. Handle multidimensional and jagged arrays safely (e.g., int[][])
				if (typeSymbol is IArrayTypeSymbol arrayType)
				{
					depth++;
					typeSymbol = arrayType.ElementType;
					continue;
				}

				// 2. Treat string as an enumerable of characters (System.String = IEnumerable<char>)
				if (typeSymbol.SpecialType == SpecialType.System_String)
				{
					depth++;
					break; // Stop here, as the inner type is 'char', which is a level 0 primitive
				}

				// 3. Handle Generic Collections (List<T>, IEnumerable<T>, Dictionary<K,V>)
				if (typeSymbol is INamedTypeSymbol namedType)
				{
					bool isEnumerable =
						(namedType.IsGenericType && namedType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) ||
						namedType.AllInterfaces.Any(i => i.IsGenericType && i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);

					if (isEnumerable && namedType.TypeArguments.Length > 0)
					{
						depth++;
						typeSymbol = namedType.TypeArguments[0]; // Advance inward into the collection
						continue;
					}
				}

				break; // Reached a true primitive layer (e.g., int, bool, char, custom enum)
			}

			return depth;
		}

		private static string GetSymbolTypeName(ISymbol symbol)
		{
			if (symbol is IPropertySymbol p) return p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
			if (symbol is IFieldSymbol f) return f.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
			return "Unknown";
		}
	}
}
